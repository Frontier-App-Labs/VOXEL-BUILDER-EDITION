#!/usr/bin/env python3
"""
Voxel Siege -- AI Texture Generator
====================================
Uses Google Gemini (gemini-3.1-flash-image-preview) to generate seamless
tileable voxel textures, then downscales them for a pixelized art style.

Usage:
    python generate_textures.py --api-key YOUR_KEY
    python generate_textures.py                       # uses GEMINI_API_KEY env var
    python generate_textures.py --force                # regenerate even if files exist
    python generate_textures.py --materials stone,wood # generate only specific materials
    python generate_textures.py --dry-run              # show prompts without calling API

Requirements:
    pip install google-generativeai Pillow
"""

from __future__ import annotations

import argparse
import base64
import io
import os
import sys
import time
from pathlib import Path
from typing import NamedTuple

try:
    from PIL import Image
except ImportError:
    sys.exit("ERROR: Pillow is not installed. Run: pip install Pillow")

try:
    from google import genai
    from google.genai import types
except ImportError:
    sys.exit("ERROR: google-genai is not installed. Run: pip install google-genai")

# ---------------------------------------------------------------------------
# Material definitions -- mirrors VoxelMaterialType enum in C#
# ---------------------------------------------------------------------------

class MaterialInfo(NamedTuple):
    name: str           # lower-case id used in filenames
    display: str        # human-readable name for prompts
    color_hex: str      # preview color from VoxelMaterials.GetPreviewColor
    prompt_hint: str    # extra description for the AI prompt


MATERIALS: list[MaterialInfo] = [
    MaterialInfo(
        "dirt", "Dirt / Grass", "4a8c3f",
        "earthy grass and dirt surface with sparse green tufts on brown soil"
    ),
    MaterialInfo(
        "wood", "Wood", "9b6a3c",
        "warm brown wood grain planks with visible grain lines"
    ),
    MaterialInfo(
        "stone", "Stone", "7c8797",
        "rough grey stone surface with subtle cracks and pebble detail"
    ),
    MaterialInfo(
        "brick", "Brick", "a45442",
        "red-brown brick wall pattern with mortar lines between bricks"
    ),
    MaterialInfo(
        "concrete", "Concrete", "8f9499",
        "smooth light grey concrete with faint surface imperfections"
    ),
    MaterialInfo(
        "metal", "Metal", "7ea0b8",
        "brushed silvery-blue metal surface with faint scratches and rivets"
    ),
    MaterialInfo(
        "reinforcedsteel", "Reinforced Steel", "4f5f70",
        "dark steel plate with cross-hatch reinforcement pattern and bolts"
    ),
    MaterialInfo(
        "glass", "Glass", "a6d9ff",
        "translucent blue-tinted glass with faint reflective highlights"
    ),
    MaterialInfo(
        "obsidian", "Obsidian", "34284a",
        "dark purple-black volcanic glass with glossy streaks"
    ),
    MaterialInfo(
        "sand", "Sand", "cdb36c",
        "tan granular sand surface with fine grain texture"
    ),
    MaterialInfo(
        "ice", "Ice", "bfe5ff",
        "light blue crystalline ice with subtle frost cracks"
    ),
    MaterialInfo(
        "armorplate", "Armor Plate", "58636f",
        "heavy dark grey armor plating with industrial panel lines"
    ),
    MaterialInfo(
        "foundation", "Foundation", "6b7080",
        "dark grey structural foundation stone with carved block pattern"
    ),
    MaterialInfo(
        "leaves", "Leaves", "3a7d2e",
        "lush green leaf clusters with visible individual leaf shapes and light veins"
    ),
    MaterialInfo(
        "bark", "Bark / Tree Trunk", "5c3a1e",
        "rough dark brown tree bark with deep vertical ridges and crevices"
    ),
]

# ---------------------------------------------------------------------------
# Prompt builder
# ---------------------------------------------------------------------------

def build_prompt(mat: MaterialInfo) -> str:
    return (
        f"Generate a seamless tileable {mat.display} texture. "
        f"Description: {mat.prompt_hint}. "
        f"The base color tone should be around #{mat.color_hex}. "
        "Style: low-poly voxel game, pixel art inspired, chunky and stylized, "
        "NOT photorealistic. Top-down flat lighting, no shadows, no perspective. "
        "The texture must tile seamlessly on all edges. "
        "Output a single square 512x512 image."
    )

# ---------------------------------------------------------------------------
# API call with retry
# ---------------------------------------------------------------------------

MAX_RETRIES = 3
RETRY_DELAY_SECONDS = 5


def generate_image(client, prompt: str, retries: int = MAX_RETRIES) -> bytes:
    """Call Gemini and return raw PNG/JPEG bytes of the generated image."""
    for attempt in range(1, retries + 1):
        try:
            response = client.models.generate_content(
                model="gemini-3.1-flash-image-preview",
                contents=prompt,
                config=types.GenerateContentConfig(
                    response_modalities=["IMAGE", "TEXT"],
                ),
            )

            # Walk through response parts looking for image data
            for part in response.candidates[0].content.parts:
                if hasattr(part, "inline_data") and part.inline_data is not None:
                    return part.inline_data.data

            raise RuntimeError(
                "No image data in response. Text parts: "
                + " | ".join(
                    p.text for p in response.candidates[0].content.parts
                    if hasattr(p, "text") and p.text
                )
            )

        except Exception as exc:
            print(f"  [attempt {attempt}/{retries}] Error: {exc}")
            if attempt < retries:
                wait = RETRY_DELAY_SECONDS * attempt
                print(f"  Retrying in {wait}s...")
                time.sleep(wait)
            else:
                raise

    # Should not reach here, but satisfy type checkers
    raise RuntimeError("Exhausted retries")

# ---------------------------------------------------------------------------
# Image processing
# ---------------------------------------------------------------------------

def downscale(img: Image.Image, size: int) -> Image.Image:
    """Downscale to (size x size) using nearest-neighbor for a pixelized look."""
    return img.resize((size, size), Image.Resampling.NEAREST)

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

def main() -> None:
    parser = argparse.ArgumentParser(
        description="Generate voxel textures using Gemini AI"
    )
    parser.add_argument(
        "--api-key",
        default=os.environ.get("GEMINI_API_KEY", ""),
        help="Gemini API key (or set GEMINI_API_KEY env var)",
    )
    parser.add_argument(
        "--force",
        action="store_true",
        help="Regenerate textures even if files already exist",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Print prompts without calling the API",
    )
    parser.add_argument(
        "--materials",
        default="",
        help="Comma-separated list of material names to generate (default: all)",
    )
    parser.add_argument(
        "--output-dir",
        default="",
        help="Output directory (default: assets/textures/voxels/ next to this script)",
    )
    parser.add_argument(
        "--sizes",
        default="32,64",
        help="Comma-separated downscale sizes in pixels (default: 32,64)",
    )

    args = parser.parse_args()

    # Resolve output directory
    if args.output_dir:
        out_dir = Path(args.output_dir)
    else:
        script_dir = Path(__file__).resolve().parent
        out_dir = script_dir.parent / "assets" / "textures" / "voxels"
    out_dir.mkdir(parents=True, exist_ok=True)

    # Also save originals for reference
    originals_dir = out_dir / "originals"
    originals_dir.mkdir(parents=True, exist_ok=True)

    sizes = [int(s.strip()) for s in args.sizes.split(",") if s.strip()]

    # Filter materials
    if args.materials:
        requested = {m.strip().lower() for m in args.materials.split(",")}
        materials = [m for m in MATERIALS if m.name in requested]
        unknown = requested - {m.name for m in materials}
        if unknown:
            print(f"WARNING: Unknown materials ignored: {', '.join(sorted(unknown))}")
            print(f"  Valid names: {', '.join(m.name for m in MATERIALS)}")
    else:
        materials = MATERIALS

    if not materials:
        print("No materials to generate.")
        return

    # Validate API key (unless dry-run)
    if not args.dry_run:
        if not args.api_key:
            sys.exit(
                "ERROR: No API key provided.\n"
                "  Use --api-key YOUR_KEY or set GEMINI_API_KEY env var."
            )
        client = genai.Client(api_key=args.api_key)

    print(f"Output directory : {out_dir}")
    print(f"Downscale sizes  : {sizes}")
    print(f"Materials        : {len(materials)}")
    print(f"Force regenerate : {args.force}")
    print()

    generated = 0
    skipped = 0
    failed = 0

    for idx, mat in enumerate(materials, 1):
        # Check if all target sizes already exist
        target_files = [out_dir / f"{mat.name}_{s}.png" for s in sizes]
        if not args.force and all(f.exists() for f in target_files):
            print(f"[{idx}/{len(materials)}] {mat.display} -- SKIPPED (files exist)")
            skipped += 1
            continue

        prompt = build_prompt(mat)

        if args.dry_run:
            print(f"[{idx}/{len(materials)}] {mat.display}")
            print(f"  Prompt: {prompt}")
            print(f"  Would save: {', '.join(str(f) for f in target_files)}")
            print()
            continue

        print(f"[{idx}/{len(materials)}] Generating {mat.display}...")
        print(f"  Prompt: {prompt[:100]}...")

        try:
            raw_bytes = generate_image(client, prompt)

            # Parse returned image
            img = Image.open(io.BytesIO(raw_bytes)).convert("RGBA")
            print(f"  Received image: {img.size[0]}x{img.size[1]}")

            # Save original
            original_path = originals_dir / f"{mat.name}_original.png"
            img.save(original_path)
            print(f"  Saved original: {original_path.name}")

            # Downscale and save
            for size in sizes:
                small = downscale(img, size)
                out_path = out_dir / f"{mat.name}_{size}.png"
                small.save(out_path)
                print(f"  Saved {size}x{size}: {out_path.name}")

            generated += 1

        except Exception as exc:
            print(f"  FAILED: {exc}")
            failed += 1

        # Rate-limit between requests to avoid API throttling
        if idx < len(materials):
            time.sleep(2)

    print()
    print("=" * 50)
    print(f"Done! Generated: {generated}, Skipped: {skipped}, Failed: {failed}")
    if generated > 0:
        print(f"Textures saved to: {out_dir}")
    if failed > 0:
        print("Re-run with --force to retry failed materials.")


if __name__ == "__main__":
    main()
