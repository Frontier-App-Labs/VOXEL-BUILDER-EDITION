"""
Reprocess AI-generated voxel textures into proper pixel art.

The key insight: real pixel art has FLAT areas of solid color, not smooth gradients.
By posterizing (k-means color quantization) the high-res source BEFORE downscaling
with nearest-neighbor, we get crisp, distinct pixels with uniform color regions --
exactly what hand-drawn pixel art looks like.

Pipeline per texture:
  1. Load 512px original (RGBA)
  2. K-means quantize to 10 colors (creates flat color regions)
  3. Boost contrast slightly (1.2x)
  4. Downscale to 32x32 and 64x64 with NEAREST neighbor
  5. Save as RGBA PNG
"""

import os
import sys
import numpy as np
from PIL import Image, ImageEnhance
from sklearn.cluster import MiniBatchKMeans

# ---------------------------------------------------------------------------
# Config
# ---------------------------------------------------------------------------
BASE_DIR = r"C:\Users\jeffd\Desktop\Voxel Siege\assets\textures\voxels"
ORIGINALS_DIR = os.path.join(BASE_DIR, "originals")
OUTPUT_DIR = BASE_DIR

TEXTURES = [
    "dirt",
    "wood",
    "stone",
    "brick",
    "concrete",
    "metal",
    "reinforcedsteel",
    "glass",
    "obsidian",
    "sand",
    "ice",
    "armorplate",
    "foundation",
    "leaves",
    "bark",
]

N_COLORS = 10          # number of palette colours (8-12 range, 10 is a good sweet spot)
CONTRAST_BOOST = 1.2   # mild contrast bump
OUTPUT_SIZES = [32, 64]

# ---------------------------------------------------------------------------
# Processing
# ---------------------------------------------------------------------------

def quantize_colors(img_rgba: Image.Image, n_colors: int) -> Image.Image:
    """
    Reduce an RGBA image to n_colors using k-means on the RGB channels.
    Alpha is preserved as-is.
    """
    arr = np.array(img_rgba)
    rgb = arr[:, :, :3]
    alpha = arr[:, :, 3]

    h, w, _ = rgb.shape
    pixels = rgb.reshape(-1, 3).astype(np.float64)

    # Only cluster non-transparent pixels to avoid wasting palette entries
    # on invisible regions
    opaque_mask = alpha.reshape(-1) > 0
    if opaque_mask.sum() == 0:
        return img_rgba  # fully transparent -- nothing to do

    opaque_pixels = pixels[opaque_mask]

    kmeans = MiniBatchKMeans(
        n_clusters=n_colors,
        random_state=42,
        batch_size=1024,
        n_init=3,
    )
    kmeans.fit(opaque_pixels)

    # Map every opaque pixel to its nearest cluster centre
    labels = kmeans.predict(opaque_pixels)
    centres = kmeans.cluster_centers_.astype(np.uint8)

    quantized = pixels.copy().astype(np.uint8)
    quantized[opaque_mask] = centres[labels]

    out = np.dstack([quantized.reshape(h, w, 3), alpha])
    return Image.fromarray(out, "RGBA")


def process_texture(name: str) -> None:
    src_path = os.path.join(ORIGINALS_DIR, f"{name}_original.png")
    if not os.path.isfile(src_path):
        print(f"  SKIP  {name}: source not found at {src_path}")
        return

    img = Image.open(src_path).convert("RGBA")
    print(f"  Loaded {name} ({img.size[0]}x{img.size[1]})")

    # 1. Posterize via k-means quantization
    img = quantize_colors(img, N_COLORS)
    print(f"    Quantized to {N_COLORS} colors")

    # 2. Boost contrast (operates on RGB; we split/rejoin alpha)
    rgb = img.convert("RGB")
    rgb = ImageEnhance.Contrast(rgb).enhance(CONTRAST_BOOST)
    r, g, b = rgb.split()
    a = img.split()[3]
    img = Image.merge("RGBA", (r, g, b, a))
    print(f"    Contrast boosted x{CONTRAST_BOOST}")

    # 3. Downscale with nearest-neighbor and save
    for size in OUTPUT_SIZES:
        out = img.resize((size, size), Image.Resampling.NEAREST)
        out_path = os.path.join(OUTPUT_DIR, f"{name}_{size}.png")
        out.save(out_path, "PNG")
        print(f"    Saved {name}_{size}.png")


def main() -> None:
    print("=" * 60)
    print("Voxel Texture Reprocessor -- pixel-art posterization pipeline")
    print("=" * 60)
    print(f"Source : {ORIGINALS_DIR}")
    print(f"Output : {OUTPUT_DIR}")
    print(f"Palette: {N_COLORS} colors | Contrast: {CONTRAST_BOOST}x")
    print(f"Sizes  : {OUTPUT_SIZES}")
    print("-" * 60)

    for name in TEXTURES:
        process_texture(name)

    print("-" * 60)
    print("Done. All textures reprocessed.")


if __name__ == "__main__":
    main()
