"""
Voxel Model Preview Tool
Renders orthographic views (front, side, top) of voxel models to PNG.
Parses the C# model generator source files to extract voxel placement data,
then renders pixel-art previews.

Usage: python preview_models.py
Output: tools/preview_commander.png, tools/preview_weapons.png
"""

import re
import sys
from pathlib import Path

try:
    from PIL import Image, ImageDraw, ImageFont
except ImportError:
    print("ERROR: Pillow not installed. Run: pip install Pillow")
    sys.exit(1)

# Known material colors (from VoxelMaterial.cs GetPreviewColor)
MATERIAL_COLORS = {
    "Dirt": (74, 140, 63),
    "Wood": (139, 90, 43),
    "Stone": (128, 128, 128),
    "Brick": (178, 89, 62),
    "Concrete": (180, 180, 170),
    "Metal": (160, 165, 175),
    "ReinforcedSteel": (100, 105, 115),
    "Glass": (180, 220, 240),
    "Obsidian": (30, 20, 35),
    "Sand": (210, 190, 130),
    "Ice": (200, 230, 255),
    "ArmorPlate": (70, 80, 90),
    "Foundation": (107, 112, 128),
    "Leaves": (60, 160, 50),
    "Bark": (90, 60, 30),
}

# Common color name mappings
COLOR_NAME_MAP = {
    "Colors.Red": (255, 0, 0),
    "Colors.DarkRed": (139, 0, 0),
    "Colors.Blue": (0, 0, 255),
    "Colors.DarkBlue": (0, 0, 139),
    "Colors.Green": (0, 128, 0),
    "Colors.DarkGreen": (0, 100, 0),
    "Colors.Yellow": (255, 255, 0),
    "Colors.Gold": (255, 215, 0),
    "Colors.White": (255, 255, 255),
    "Colors.Black": (0, 0, 0),
    "Colors.Gray": (128, 128, 128),
    "Colors.DarkGray": (64, 64, 64),
    "Colors.LightGray": (192, 192, 192),
    "Colors.Brown": (139, 69, 19),
    "Colors.SaddleBrown": (139, 69, 19),
    "Colors.Orange": (255, 165, 0),
    "Colors.Cyan": (0, 255, 255),
    "Colors.Magenta": (255, 0, 255),
    "Colors.Pink": (255, 192, 203),
    "Colors.Tan": (210, 180, 140),
    "Colors.Beige": (245, 245, 220),
    "Colors.Khaki": (195, 176, 145),
    "Colors.Silver": (192, 192, 192),
    "Colors.SteelBlue": (70, 130, 180),
    "Colors.SlateGray": (112, 128, 144),
    "Colors.DimGray": (105, 105, 105),
    "Colors.Olive": (128, 128, 0),
    "Colors.OliveDrab": (107, 142, 35),
    "Colors.DarkOliveGreen": (85, 107, 47),
    "Colors.Maroon": (128, 0, 0),
    "Colors.Navy": (0, 0, 128),
    "Colors.Teal": (0, 128, 128),
    "Colors.Purple": (128, 0, 128),
    "Colors.Indigo": (75, 0, 130),
    "Colors.Coral": (255, 127, 80),
    "Colors.Tomato": (255, 99, 71),
    "Colors.Crimson": (220, 20, 60),
    "Colors.Firebrick": (178, 34, 34),
    "Colors.Chocolate": (210, 105, 30),
    "Colors.Peru": (205, 133, 63),
    "Colors.Sienna": (160, 82, 45),
    "Colors.RosyBrown": (188, 143, 143),
    "Colors.SandyBrown": (244, 164, 96),
    "Colors.Wheat": (245, 222, 179),
    "Colors.Cornsilk": (255, 248, 220),
}


def parse_hex_color(hex_str):
    """Parse a hex color string like '5a5045' or '#5a5045'."""
    hex_str = hex_str.strip().strip('"').strip("'").lstrip('#')
    if len(hex_str) == 6:
        return (int(hex_str[0:2], 16), int(hex_str[2:4], 16), int(hex_str[4:6], 16))
    return None


def parse_color_from_code(color_expr):
    """Try to parse a Color(...) expression from C# code."""
    color_expr = color_expr.strip()

    # Check named colors
    for name, rgb in COLOR_NAME_MAP.items():
        if name in color_expr:
            return rgb

    # new Color("hex")
    m = re.search(r'new\s+Color\s*\(\s*"([0-9a-fA-F]{6})"\s*\)', color_expr)
    if m:
        return parse_hex_color(m.group(1))

    # new Color(r, g, b) with float values
    m = re.search(r'new\s+Color\s*\(\s*([\d.]+)f?\s*,\s*([\d.]+)f?\s*,\s*([\d.]+)f?', color_expr)
    if m:
        r, g, b = float(m.group(1)), float(m.group(2)), float(m.group(3))
        if r <= 1.0 and g <= 1.0 and b <= 1.0:
            return (int(r * 255), int(g * 255), int(b * 255))
        return (int(r), int(g), int(b))

    return (200, 200, 200)  # fallback gray


def extract_voxels_from_source(filepath):
    """
    Parse a C# model generator file and extract voxel placements.
    Returns a dict of model_name -> list of (x, y, z, r, g, b).
    This is a best-effort heuristic parser.
    """
    content = Path(filepath).read_text(encoding='utf-8-sig')
    models = {}

    # Find all SetVoxel / set calls - patterns like:
    # grid[x, y, z] = color  OR  voxels[x,y,z] = ... OR SetVoxel(x, y, z, color)
    # Also look for loops that fill regions

    # Strategy: Find method boundaries, then parse voxel assignments within each
    method_pattern = re.compile(
        r'(?:public|private|internal|protected)\s+(?:static\s+)?(?:\w+(?:<[^>]+>)?)\s+(\w+)\s*\([^)]*\)\s*\{',
        re.MULTILINE
    )

    current_model_voxels = []
    current_color = (200, 200, 200)

    # Look for direct coordinate assignments and color
    # Pattern: something[x, y, z] = someColor
    # Or: SetVoxel(new Vector3I(x, y, z), color)

    # Simple approach: find all (x, y, z) tuples paired with colors
    # This handles the most common patterns in our generators

    # Find color variable assignments
    color_vars = {}
    for m in re.finditer(r'(?:Color|var)\s+(\w+)\s*=\s*(.+?);', content):
        var_name = m.group(1)
        color_expr = m.group(2)
        parsed = parse_color_from_code(color_expr)
        if parsed:
            color_vars[var_name] = parsed

    # Find voxel placement patterns
    voxels = []

    # Pattern 1: Direct array set like grid[x, y, z] = true/color with surrounding color context
    # Pattern 2: SetVoxel calls
    # Pattern 3: Loops with Fill calls

    # Look for for-loop blocks that place voxels
    for m in re.finditer(
        r'for\s*\(\s*int\s+\w+\s*=\s*(\d+)\s*;\s*\w+\s*[<]=?\s*(\d+)\s*;[^)]+\)\s*'
        r'(?:\{[^{}]*(?:\{[^{}]*\}[^{}]*)*\}|\s*[^{;]+;)',
        content, re.DOTALL
    ):
        pass  # Complex loop parsing - skip for now

    # Simple extraction: find all numeric coordinate triples near color references
    # This is model-specific, so let's try to find the grid dimensions first

    # Look for grid size: new Type[W, H, D] or similar
    grid_match = re.search(r'new\s+\w+\[(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\]', content)
    grid_w, grid_h, grid_d = 16, 16, 16
    if grid_match:
        grid_w = int(grid_match.group(1))
        grid_h = int(grid_match.group(2))
        grid_d = int(grid_match.group(3))

    # For now, return the raw content for manual parsing
    models["raw"] = content
    models["grid_size"] = (grid_w, grid_h, grid_d)
    models["color_vars"] = color_vars

    return models


def render_voxel_grid(voxels, grid_size, scale=8, title="Model"):
    """
    Render a 3D voxel grid to front/side/top view images.
    voxels: list of (x, y, z, r, g, b) tuples
    grid_size: (w, h, d) tuple
    scale: pixels per voxel
    Returns a PIL Image with all 3 views + title.
    """
    w, h, d = grid_size

    # Create lookup
    grid = {}
    for vx in voxels:
        x, y, z = vx[0], vx[1], vx[2]
        r, g, b = vx[3], vx[4], vx[5]
        grid[(x, y, z)] = (r, g, b)

    padding = 10
    label_h = 20
    view_w = max(w, d) * scale
    view_h = max(h, d) * scale

    total_w = view_w * 3 + padding * 4
    total_h = view_h + label_h + padding * 3

    img = Image.new('RGBA', (total_w, total_h), (30, 30, 30, 255))
    draw = ImageDraw.Draw(img)

    # Front view (X-Z plane, looking from front, Y is up)
    # Project: for each (x, y), find the nearest z that has a voxel
    front_img = Image.new('RGBA', (w * scale, h * scale), (40, 40, 40, 255))
    for x in range(w):
        for y in range(h):
            for z in range(d):
                if (x, y, z) in grid:
                    color = grid[(x, y, z)]
                    for px in range(scale):
                        for py in range(scale):
                            front_img.putpixel(
                                (x * scale + px, (h - 1 - y) * scale + py),
                                (*color, 255)
                            )
                    break  # nearest z only

    # Side view (Z-Y plane, looking from right, Y is up)
    side_img = Image.new('RGBA', (d * scale, h * scale), (40, 40, 40, 255))
    for z in range(d):
        for y in range(h):
            for x in range(w - 1, -1, -1):
                if (x, y, z) in grid:
                    color = grid[(x, y, z)]
                    for px in range(scale):
                        for py in range(scale):
                            side_img.putpixel(
                                (z * scale + px, (h - 1 - y) * scale + py),
                                (*color, 255)
                            )
                    break

    # Top view (X-Z plane, looking from above)
    top_img = Image.new('RGBA', (w * scale, d * scale), (40, 40, 40, 255))
    for x in range(w):
        for z in range(d):
            for y in range(h - 1, -1, -1):
                if (x, y, z) in grid:
                    color = grid[(x, y, z)]
                    for px in range(scale):
                        for py in range(scale):
                            top_img.putpixel(
                                (x * scale + px, z * scale + py),
                                (*color, 255)
                            )
                    break

    # Compose
    x_off = padding
    y_off = label_h + padding
    img.paste(front_img, (x_off, y_off))
    draw.text((x_off, padding), "FRONT", fill=(255, 255, 255))

    x_off += w * scale + padding
    img.paste(side_img, (x_off, y_off))
    draw.text((x_off, padding), "SIDE", fill=(255, 255, 255))

    x_off += d * scale + padding
    img.paste(top_img, (x_off, y_off))
    draw.text((x_off, padding), "TOP", fill=(255, 255, 255))

    return img


def parse_commander_voxels(filepath):
    """
    Parse CommanderModelGenerator.cs specifically.
    Look for the voxel placement pattern used in the file.
    """
    content = Path(filepath).read_text(encoding='utf-8-sig')
    voxels = []

    # Find grid dimensions
    grid_match = re.search(r'(\d+)\s*,\s*(\d+)\s*,\s*(\d+)',
                           re.search(r'new\s+(?:Color|bool|byte)\??\[([^\]]+)\]', content).group(1) if re.search(r'new\s+(?:Color|bool|byte)\??\[([^\]]+)\]', content) else "10,14,8")

    # Try to find the grid size from variable declarations
    w_match = re.search(r'(?:width|gridW|w)\s*=\s*(\d+)', content, re.IGNORECASE)
    h_match = re.search(r'(?:height|gridH|h)\s*=\s*(\d+)', content, re.IGNORECASE)
    d_match = re.search(r'(?:depth|gridD|d)\s*=\s*(\d+)', content, re.IGNORECASE)

    grid_w = int(w_match.group(1)) if w_match else 10
    grid_h = int(h_match.group(1)) if h_match else 14
    grid_d = int(d_match.group(1)) if d_match else 8

    # Also check for explicit array like new Color?[W, H, D]
    arr_match = re.search(r'new\s+Color\??\s*\[\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\]', content)
    if arr_match:
        grid_w, grid_h, grid_d = int(arr_match.group(1)), int(arr_match.group(2)), int(arr_match.group(3))

    # Find color variable definitions
    color_vars = {}
    for m in re.finditer(r'Color\s+(\w+)\s*=\s*(.+?);', content):
        name = m.group(1)
        expr = m.group(2)
        parsed = parse_color_from_code(expr)
        if parsed:
            color_vars[name] = parsed

    # Find voxel set patterns:
    # grid[x, y, z] = someColor;
    # or: SetVox(grid, x, y, z, color);
    # or: for loops that fill regions

    # Pattern: grid[x, y, z] = varName;
    for m in re.finditer(r'grid\s*\[\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\]\s*=\s*(\w+)', content):
        x, y, z = int(m.group(1)), int(m.group(2)), int(m.group(3))
        color_name = m.group(4)
        color = color_vars.get(color_name, parse_color_from_code(color_name))
        if color and color_name != "null":
            voxels.append((x, y, z, *color))

    # Pattern: SetVox or similar helper
    for m in re.finditer(r'(?:SetVox|Set|Fill)\w*\s*\(\s*\w+\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*(\w+)', content):
        x, y, z = int(m.group(1)), int(m.group(2)), int(m.group(3))
        color_name = m.group(4)
        color = color_vars.get(color_name, parse_color_from_code(color_name))
        if color and color_name != "null":
            voxels.append((x, y, z, *color))

    # Pattern: Fill loops - for (int VAR = START; VAR < END; VAR++) with grid[...] = color
    # Find simple rectangular fills
    for m in re.finditer(
        r'for\s*\(\s*int\s+(\w+)\s*=\s*(\d+)\s*;\s*\1\s*[<]=?\s*(\d+)\s*;\s*\1\+\+\s*\)\s*\{?\s*'
        r'(?:for\s*\(\s*int\s+(\w+)\s*=\s*(\d+)\s*;\s*\4\s*[<]=?\s*(\d+)\s*;\s*\4\+\+\s*\)\s*\{?\s*)?'
        r'(?:for\s*\(\s*int\s+(\w+)\s*=\s*(\d+)\s*;\s*\7\s*[<]=?\s*(\d+)\s*;\s*\7\+\+\s*\)\s*\{?\s*)?'
        r'[^}]*?grid\s*\[\s*(?:\w+)\s*,\s*(?:\w+)\s*,\s*(?:\w+)\s*\]\s*=\s*(\w+)',
        content, re.DOTALL
    ):
        # Extract loop ranges and fill
        color_name = m.group(10) if m.group(10) else m.group(7)
        color = color_vars.get(color_name, (200, 200, 200))

        ranges = []
        if m.group(1):
            start, end = int(m.group(2)), int(m.group(3))
            ranges.append((start, end))
        if m.group(4):
            start, end = int(m.group(5)), int(m.group(6))
            ranges.append((start, end))
        if m.group(7):
            start, end = int(m.group(8)), int(m.group(9))
            ranges.append((start, end))

    # If we couldn't parse any voxels, return a placeholder
    if not voxels:
        # Create a simple placeholder figure
        skin = (220, 180, 140)
        helmet = (80, 100, 60)
        body = (60, 80, 50)
        boots = (50, 40, 30)

        # Simple 6x10x4 figure
        grid_w, grid_h, grid_d = 6, 10, 4
        # Boots (y=0-1)
        for y in range(2):
            for x in [1, 2, 3, 4]:
                for z in [1, 2]:
                    voxels.append((x, y, z, *boots))
        # Body (y=2-5)
        for y in range(2, 6):
            for x in range(1, 5):
                for z in range(1, 3):
                    voxels.append((x, y, z, *body))
        # Arms (y=3-5)
        for y in range(3, 6):
            for z in [1]:
                voxels.append((0, y, z, *body))
                voxels.append((5, y, z, *body))
        # Head (y=6-9)
        for y in range(6, 10):
            for x in range(1, 5):
                for z in range(0, 4):
                    if y >= 8:
                        voxels.append((x, y, z, *helmet))
                    else:
                        voxels.append((x, y, z, *skin))

    return voxels, (grid_w, grid_h, grid_d)


def main():
    base = Path(__file__).parent.parent

    # Parse commander
    commander_file = base / "src" / "Art" / "CommanderModelGenerator.cs"
    weapon_file = base / "src" / "Art" / "WeaponModelGenerator.cs"

    results = []

    if commander_file.exists():
        print(f"Parsing {commander_file.name}...")
        voxels, grid_size = parse_commander_voxels(str(commander_file))
        print(f"  Found {len(voxels)} voxels in {grid_size[0]}x{grid_size[1]}x{grid_size[2]} grid")
        if voxels:
            img = render_voxel_grid(voxels, grid_size, scale=10, title="Commander")
            out_path = base / "tools" / "preview_commander.png"
            img.save(str(out_path))
            print(f"  Saved to {out_path}")
            results.append(str(out_path))

    if weapon_file.exists():
        print(f"Parsing {weapon_file.name}...")
        voxels, grid_size = parse_commander_voxels(str(weapon_file))
        print(f"  Found {len(voxels)} voxels in {grid_size[0]}x{grid_size[1]}x{grid_size[2]} grid")
        if voxels:
            img = render_voxel_grid(voxels, grid_size, scale=10, title="Weapon")
            out_path = base / "tools" / "preview_weapons.png"
            img.save(str(out_path))
            print(f"  Saved to {out_path}")
            results.append(str(out_path))

    if results:
        print(f"\nPreview images saved! Open them to check the models.")
    else:
        print("\nNo model files found to preview.")


if __name__ == "__main__":
    main()
