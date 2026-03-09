"""Direct Python port of WeaponModelGenerator to preview all weapon models."""
from PIL import Image, ImageDraw

def darken(rgb, amt): return tuple(max(0, int(v*(1-amt))) for v in rgb)
def lighten(rgb, amt): return tuple(min(255, int(v + (255-v)*amt)) for v in rgb)

team = (200, 50, 50)  # Red team
teamDk = darken(team, 0.15)
teamDkk = darken(team, 0.35)

def fill(grid, w, h, d, x0, y0, z0, x1, y1, z1, color):
    for x in range(x0, x1):
        for y in range(y0, y1):
            for z in range(z0, z1):
                if 0 <= x < w and 0 <= y < h and 0 <= z < d:
                    grid[(x,y,z)] = color

def make_cannon():
    w, h, d = 8, 6, 10
    g = {}
    metal = (115, 115, 122)
    metalDk = (71, 71, 82)
    metalLt = (148, 148, 158)
    brass = (199, 166, 56)

    # Base platform
    fill(g, w,h,d, 1,0,1, 7,1,9, teamDk)
    for x in range(1,7):
        g[(x,0,1)] = teamDkk; g[(x,0,8)] = teamDkk
    for z in range(1,9):
        g[(1,0,z)] = teamDkk; g[(6,0,z)] = teamDkk

    # Carriage sides
    fill(g, w,h,d, 1,1,4, 3,3,8, metalDk)
    fill(g, w,h,d, 5,1,4, 7,3,8, metalDk)

    # Barrel core
    fill(g, w,h,d, 3,2,0, 5,4,8, metal)
    # Barrel wider cross
    fill(g, w,h,d, 2,3,1, 6,3,7, metal)
    fill(g, w,h,d, 3,1,1, 5,5,7, metal)

    # Muzzle flare
    fill(g, w,h,d, 2,2,0, 6,4,1, metalDk)
    g[(3,4,0)] = metalLt; g[(4,4,0)] = metalLt

    # Brass bands
    for x in range(2,6):
        g[(x,4,2)] = brass; g[(x,4,5)] = brass

    # Brass breech cap
    fill(g, w,h,d, 3,2,8, 5,4,9, brass)

    # Trunnion pins
    g[(2,2,5)] = brass; g[(5,2,5)] = brass

    return g, w, h, d, "CANNON"

def make_mortar():
    w, h, d = 6, 8, 6
    g = {}
    olive = (97, 107, 64)
    oliveDk = (66, 77, 41)
    metalDk = (56, 56, 64)

    # Wide base plate
    fill(g, w,h,d, 0,0,0, 6,1,6, metalDk)
    for x in range(6):
        g[(x,0,0)] = teamDkk; g[(x,0,5)] = teamDkk
    for z in range(6):
        g[(0,0,z)] = teamDkk; g[(5,0,z)] = teamDkk

    # Support bipod legs
    fill(g, w,h,d, 1,1,1, 2,3,2, teamDk)
    fill(g, w,h,d, 4,1,1, 5,3,2, teamDk)
    fill(g, w,h,d, 1,1,4, 2,3,5, teamDk)
    fill(g, w,h,d, 4,1,4, 5,3,5, teamDk)

    # Mortar tube
    fill(g, w,h,d, 2,1,2, 4,3,4, olive)
    fill(g, w,h,d, 2,2,1, 4,4,3, olive)
    fill(g, w,h,d, 2,3,0, 4,5,2, olive)
    fill(g, w,h,d, 2,4,0, 4,7,1, olive)

    # Muzzle opening
    g[(2,7,0)] = oliveDk; g[(3,7,0)] = oliveDk

    # Muzzle flare
    g[(1,6,0)] = olive; g[(4,6,0)] = olive

    # Sight post
    g[(1,4,1)] = metalDk; g[(1,5,1)] = metalDk

    return g, w, h, d, "MORTAR"

def make_railgun():
    w, h, d = 8, 5, 12
    g = {}
    steel = (89, 94, 102)
    steelDk = (46, 51, 61)
    steelLt = (128, 133, 140)
    cyan = (51, 217, 242)
    cyanGlow = (128, 255, 255)

    # Base platform
    fill(g, w,h,d, 1,0,2, 7,1,10, teamDk)
    for x in range(1,7):
        g[(x,0,2)] = teamDkk; g[(x,0,9)] = teamDkk

    # Left rail
    fill(g, w,h,d, 1,1,0, 3,3,11, steel)
    fill(g, w,h,d, 1,1,0, 3,3,1, steelDk)
    for z in range(1,10):
        g[(1,2,z)] = steelLt; g[(2,2,z)] = steelLt

    # Right rail
    fill(g, w,h,d, 5,1,0, 7,3,11, steel)
    fill(g, w,h,d, 5,1,0, 7,3,1, steelDk)
    for z in range(1,10):
        g[(5,2,z)] = steelLt; g[(6,2,z)] = steelLt

    # Energy channel
    fill(g, w,h,d, 3,1,2, 5,2,9, cyan)
    for z in [3, 5, 7]:
        g[(3,1,z)] = cyanGlow; g[(4,1,z)] = cyanGlow

    # Top housing
    fill(g, w,h,d, 2,3,3, 6,4,8, steelDk)
    g[(3,3,4)] = cyan; g[(4,3,4)] = cyan
    g[(3,3,6)] = cyan; g[(4,3,6)] = cyan

    # Rear power unit
    fill(g, w,h,d, 2,1,9, 6,4,12, steelDk)
    g[(3,2,10)] = cyan; g[(4,2,10)] = cyan
    g[(3,2,11)] = cyanGlow; g[(4,2,11)] = cyanGlow
    g[(2,3,11)] = steelLt; g[(5,3,11)] = steelLt

    return g, w, h, d, "RAILGUN"

def make_missile():
    w, h, d = 8, 7, 8
    g = {}
    army = (71, 92, 56)
    armyDk = (46, 61, 36)
    metalDk = (56, 56, 64)
    red = (217, 46, 31)
    yellow = (242, 204, 38)

    # Base
    fill(g, w,h,d, 1,0,1, 7,1,7, teamDk)
    for x in range(1,7):
        g[(x,0,1)] = teamDkk; g[(x,0,6)] = teamDkk

    # Main housing
    fill(g, w,h,d, 1,1,1, 7,6,7, army)
    for y in range(1,6):
        g[(1,y,1)] = armyDk; g[(6,y,1)] = armyDk
        g[(1,y,6)] = armyDk; g[(6,y,6)] = armyDk

    # 4 launch tubes (front face z=1)
    for bx, by in [(2,4),(4,4),(2,2),(4,2)]:
        g[(bx,by,1)] = metalDk; g[(bx+1,by,1)] = metalDk
        g[(bx,by+1,1)] = metalDk; g[(bx+1,by+1,1)] = metalDk

    # Divider cross
    g[(3,3,1)] = armyDk; g[(4,3,1)] = armyDk
    g[(3,4,1)] = armyDk; g[(4,4,1)] = armyDk

    # Warning stripes
    for side_x in [1, 6]:
        g[(side_x,4,3)] = yellow; g[(side_x,4,4)] = yellow
        g[(side_x,2,3)] = yellow; g[(side_x,2,4)] = yellow

    # Red danger on top
    g[(3,6,3)] = red; g[(4,6,3)] = red
    g[(3,6,4)] = red; g[(4,6,4)] = red

    # Top cap
    fill(g, w,h,d, 1,6,1, 7,7,7, metalDk)
    g[(2,6,2)] = teamDkk; g[(5,6,2)] = teamDkk
    g[(2,6,5)] = teamDkk; g[(5,6,5)] = teamDkk

    return g, w, h, d, "MISSILE LAUNCHER"

def make_drill():
    w, h, d = 6, 7, 10
    g = {}
    orange = (235, 148, 31)
    orangeDk = (184, 107, 20)
    yellow = (242, 209, 46)
    metal = (128, 128, 138)
    metalDk = (77, 77, 87)
    metalLt = (158, 158, 168)

    # Base housing
    fill(g, w,h,d, 0,0,5, 6,1,10, teamDk)

    # Motor housing
    fill(g, w,h,d, 1,1,5, 5,5,9, orange)
    fill(g, w,h,d, 1,5,6, 5,6,8, orangeDk)
    fill(g, w,h,d, 1,4,5, 5,5,6, yellow)

    # Exhaust vents
    g[(2,3,9)] = metalDk; g[(3,3,9)] = metalDk
    g[(2,2,9)] = metalDk; g[(3,2,9)] = metalDk

    # Drive shaft
    fill(g, w,h,d, 2,2,3, 4,4,5, metalDk)

    # Drill chuck
    fill(g, w,h,d, 1,1,2, 5,5,4, metal)

    # Drill bit wide base
    fill(g, w,h,d, 1,1,2, 5,5,3, metal)

    # Narrowing
    fill(g, w,h,d, 1,2,1, 5,4,2, metalLt)

    # Tip
    g[(2,2,0)] = metalLt; g[(3,2,0)] = metalLt
    g[(2,3,0)] = metalLt; g[(3,3,0)] = metalLt

    # Spiral fluting
    g[(1,3,2)] = metalDk; g[(4,2,2)] = metalDk
    g[(2,1,1)] = metalDk; g[(3,4,1)] = metalDk
    g[(1,2,1)] = metalDk; g[(4,3,1)] = metalDk

    # Tip highlight
    g[(2,3,0)] = yellow; g[(3,2,0)] = yellow

    return g, w, h, d, "DRILL"


# === RENDER ===
scale = 10
pad = 12
label_h = 18

def render_view(grid, vw, vh, get_pixel):
    img = Image.new('RGB', (vw*scale, vh*scale), (40,40,40))
    for px in range(vw):
        for py in range(vh):
            color = get_pixel(grid, px, py)
            if color:
                for sx in range(scale):
                    for sy in range(scale):
                        img.putpixel((px*scale+sx, py*scale+sy), color)
                for sx in range(scale):
                    img.putpixel((px*scale+sx, py*scale), darken(color, 0.15))
                for sy in range(scale):
                    img.putpixel((px*scale, py*scale+sy), darken(color, 0.15))
    return img

def front_pixel(grid, w, h, d, px, py):
    for z in range(d):
        if (px, h-1-py, z) in grid:
            return grid[(px, h-1-py, z)]
    return None

def side_pixel(grid, w, h, d, px, py):
    for x in range(w-1, -1, -1):
        if (x, h-1-py, px) in grid:
            return grid[(x, h-1-py, px)]
    return None

def top_pixel(grid, w, h, d, px, py):
    for y in range(h-1, -1, -1):
        if (px, y, py) in grid:
            return grid[(px, y, py)]
    return None

weapons = [make_cannon(), make_mortar(), make_railgun(), make_missile(), make_drill()]

# Calculate total canvas size
row_height = 0
total_width = 0
for g, w, h, d, name in weapons:
    fw = w * scale
    fh = h * scale
    sw = d * scale
    tw = w * scale
    th = d * scale
    row_w = fw + sw + tw + pad * 4
    row_h = max(fh, fh, th) + label_h + pad
    total_width = max(total_width, row_w)
    row_height = max(row_height, row_h)

total_h = row_height * len(weapons) + pad
canvas = Image.new('RGB', (total_width + pad*2, total_h + pad), (30,30,30))
draw = ImageDraw.Draw(canvas)

y_offset = pad
for g, w, h, d, name in weapons:
    front = render_view(g, w, h, lambda gr, px, py: front_pixel(gr, w, h, d, px, py))
    side = render_view(g, d, h, lambda gr, px, py: side_pixel(gr, w, h, d, px, py))
    top = render_view(g, w, d, lambda gr, px, py: top_pixel(gr, w, h, d, px, py))

    x = pad
    draw.text((x, y_offset), f"{name} - FRONT", fill=(255,255,255))
    canvas.paste(front, (x, y_offset + label_h))
    x += front.width + pad
    draw.text((x, y_offset), "SIDE", fill=(255,255,255))
    canvas.paste(side, (x, y_offset + label_h))
    x += side.width + pad
    draw.text((x, y_offset), "TOP", fill=(255,255,255))
    canvas.paste(top, (x, y_offset + label_h))

    y_offset += max(front.height, side.height, top.height) + label_h + pad

canvas.save("tools/preview_weapons.png")
print(f"Weapon previews saved for {len(weapons)} weapons")
for g, w, h, d, name in weapons:
    print(f"  {name}: {len(g)} voxels, {w}x{h}x{d} grid")
