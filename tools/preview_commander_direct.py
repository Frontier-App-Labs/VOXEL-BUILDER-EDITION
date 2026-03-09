"""Direct Python port of CommanderModelGenerator to preview the model."""
from PIL import Image, ImageDraw

W, H, D = 8, 16, 8
grid = {}

def fill(x0, y0, z0, x1, y1, z1, color):
    for x in range(x0, x1):
        for y in range(y0, y1):
            for z in range(z0, z1):
                if 0 <= x < W and 0 <= y < H and 0 <= z < D:
                    grid[(x,y,z)] = color

def c(r,g,b): return (int(r*255), int(g*255), int(b*255))
def darken(rgb, amt): return tuple(max(0, int(v*(1-amt))) for v in rgb)
def lighten(rgb, amt): return tuple(min(255, int(v + (255-v)*amt)) for v in rgb)

team = (200, 50, 50)  # Red team
uniformDark = darken(team, 0.25)
uniformLight = lighten(team, 0.15)
skin = c(0.92, 0.76, 0.60)
skinShadow = c(0.80, 0.64, 0.48)
boots = c(0.20, 0.14, 0.10)
belt = c(0.35, 0.25, 0.12)
buckle = c(0.95, 0.80, 0.18)
helmet = darken(team, 0.20)
helmetRim = darken(team, 0.35)
helmetBadge = c(0.95, 0.82, 0.20)
epaulette = c(0.95, 0.82, 0.18)
eyeWhite = c(0.96, 0.96, 0.98)
eyePupil = c(0.06, 0.06, 0.10)
mouth = c(0.75, 0.50, 0.40)
blush = c(0.95, 0.70, 0.62)

# BOOTS
fill(1,0,2, 4,2,6, boots)
fill(4,0,2, 7,2,6, boots)
fill(1,0,1, 4,1,6, boots)
fill(4,0,1, 7,1,6, boots)

# LEGS
fill(1,2,2, 4,4,6, uniformDark)
fill(4,2,2, 7,4,6, uniformDark)

# BELT
fill(1,4,2, 7,5,6, belt)
grid[(3,4,2)] = buckle
grid[(4,4,2)] = buckle

# TORSO
fill(1,5,2, 7,8,6, team)
for y in range(5,8):
    grid[(3,y,2)] = uniformLight
    grid[(4,y,2)] = uniformLight
grid[(2,7,2)] = uniformLight
grid[(5,7,2)] = uniformLight

# ARMS
fill(0,4,2, 1,8,6, team)
for z in range(2,6): grid[(0,7,z)] = epaulette
for z in range(2,6): grid[(0,4,z)] = skin
fill(7,4,2, 8,8,6, team)
for z in range(2,6): grid[(7,7,z)] = epaulette
for z in range(2,6): grid[(7,4,z)] = skin

# HEAD
fill(1,8,1, 7,14,7, skin)
for y in range(8,14):
    for z in range(1,7):
        grid[(1,y,z)] = skinShadow
        grid[(6,y,z)] = skinShadow
for y in range(8,14):
    for x in range(2,6):
        grid[(x,y,6)] = skinShadow

# Eyes
grid[(2,11,1)] = eyeWhite
grid[(3,11,1)] = eyePupil
grid[(4,11,1)] = eyePupil
grid[(5,11,1)] = eyeWhite
# Eyebrows
for x in range(2,6): grid[(x,12,1)] = skinShadow
# Mouth
grid[(3,9,1)] = mouth; grid[(4,9,1)] = mouth
# Blush
grid[(2,10,1)] = blush; grid[(5,10,1)] = blush

# HELMET
fill(1,13,1, 7,14,7, helmet)
fill(1,14,1, 7,16,7, helmet)
for x in range(1,7):
    grid[(x,13,1)] = helmetRim
    grid[(x,13,6)] = helmetRim
for z in range(1,7):
    grid[(1,13,z)] = helmetRim
    grid[(6,13,z)] = helmetRim
fill(2,13,0, 6,14,1, helmetRim)
grid[(3,14,1)] = helmetBadge
grid[(4,14,1)] = helmetBadge
fill(2,15,2, 6,16,6, lighten(team, 0.05))

# === RENDER ===
scale = 12
pad = 15
label_h = 20

def render_view(name, get_pixel):
    """Render one orthographic view."""
    vw, vh = get_pixel("size")
    img = Image.new('RGB', (vw*scale, vh*scale), (40,40,40))
    for px in range(vw):
        for py in range(vh):
            color = get_pixel(px, py)
            if color:
                for sx in range(scale):
                    for sy in range(scale):
                        img.putpixel((px*scale+sx, py*scale+sy), color)
                # Grid lines (subtle)
                for sx in range(scale):
                    img.putpixel((px*scale+sx, py*scale), darken(color, 0.15))
                for sy in range(scale):
                    img.putpixel((px*scale, py*scale+sy), darken(color, 0.15))
    return img

def front_pixel(px, py=None):
    if py is None: return (W, H)
    for z in range(D):
        if (px, H-1-py, z) in grid:
            return grid[(px, H-1-py, z)]
    return None

def side_pixel(px, py=None):
    if py is None: return (D, H)
    for x in range(W-1, -1, -1):
        if (x, H-1-py, px) in grid:
            return grid[(x, H-1-py, px)]
    return None

def top_pixel(px, py=None):
    if py is None: return (W, D)
    for y in range(H-1, -1, -1):
        if (px, y, py) in grid:
            return grid[(px, y, py)]
    return None

front = render_view("FRONT", front_pixel)
side = render_view("SIDE", side_pixel)
top = render_view("TOP", top_pixel)

total_w = front.width + side.width + top.width + pad*4
total_h = max(front.height, side.height, top.height) + label_h + pad*2
canvas = Image.new('RGB', (total_w, total_h), (30,30,30))
draw = ImageDraw.Draw(canvas)

x = pad
draw.text((x, 5), "FRONT", fill=(255,255,255))
canvas.paste(front, (x, label_h + pad))
x += front.width + pad
draw.text((x, 5), "SIDE", fill=(255,255,255))
canvas.paste(side, (x, label_h + pad))
x += side.width + pad
draw.text((x, 5), "TOP", fill=(255,255,255))
canvas.paste(top, (x, label_h + pad))

canvas.save("tools/preview_commander.png")
print(f"Commander preview saved ({len(grid)} voxels, {W}x{H}x{D} grid)")
