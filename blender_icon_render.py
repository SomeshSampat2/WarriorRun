#!/usr/bin/env python3
"""
WarriorRun — full app-icon pipeline.
Run:  /Applications/Blender.app/Contents/MacOS/Blender --background --python blender_icon_render.py -- <pose>
Poses the Knight hero, renders a lit bust at 2048 (transparent), builds a
sunburst backdrop procedurally, composites icon variants and writes every
Android density into Assets/WarriorRun/Sprites/AppIcon/.

Outputs (all PNG):
  AppIcon.png            1024 square   (overwrite existing — GUID stays valid)
  StoreIcon.png          512  square   (Play Console listing)
  SplashIcon.png         512  circular badge (Android 12+ system splash)
  IconFG_<n>.png         432/324/216/162/108/81  adaptive foreground
  IconBG_<n>.png         same sizes               adaptive background
  Icon_<n>.png           192/144/96/72/48/36     legacy square
  IconRound_<n>.png      same sizes               legacy round
"""
import bpy, math, os, sys
from mathutils import Vector
import numpy as np

ROOT = os.path.dirname(os.path.abspath(__file__))
GLB = os.path.join(ROOT, "Assets", "WarriorRun", "Art", "CharNew", "Knight.glb")
WORK = os.path.join(ROOT, "icon_work")
DST = os.path.join(ROOT, "Assets", "WarriorRun", "Sprites", "AppIcon")
os.makedirs(WORK, exist_ok=True)
os.makedirs(DST, exist_ok=True)

POSE = "2H_Melee_Idle"
POSE_FRAME = 30
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
if len(argv) >= 1 and argv[0]:
    POSE = argv[0]
if len(argv) >= 2:
    POSE_FRAME = int(argv[1])
REUSE = len(argv) >= 3 and argv[2] == "reuse"

HIDE = {"2H_Sword", "1H_Sword_Offhand", "Badge_Shield", "Rectangle_Shield", "Spike_Shield"}

# ------------------------------------------------------------------ palette
# matches WarriorRunBuilder.cs
def hx(s):
    s = s.lstrip('#')
    return tuple(int(s[i:i + 2], 16) / 255 for i in (0, 2, 4)) + (1.0,)

AMBER_HI   = np.array(hx("#FFD36B")[:3])
AMBER_MID  = np.array(hx("#F79E33")[:3])
AMBER_DEEP = np.array(hx("#E4572E")[:3])
RAY_DARK   = np.array(hx("#DE7C22")[:3])
NAVY       = np.array(hx("#18242F")[:3])
NAVY_HI    = np.array(hx("#243646")[:3])

# ================================================================== blender
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=GLB)
scene = bpy.context.scene

arm, meshes = None, []
for o in scene.objects:
    if o.type == 'ARMATURE':
        arm = o
    elif o.type == 'MESH':
        meshes.append(o)
for o in meshes:
    if o.name in HIDE or o.name.split('.')[0] in HIDE:
        o.hide_render = True
        o.hide_viewport = True
bpy.context.view_layer.update()

# pose
actions = {a.name: a for a in bpy.data.actions}
if arm and POSE in actions:
    if arm.animation_data is None:
        arm.animation_data_create()
    act = actions[POSE]
    arm.animation_data.action = act
    try:
        if len(act.slots):
            arm.animation_data.action_slot = act.slots[0]
    except Exception:
        pass
    scene.frame_set(POSE_FRAME)
    bpy.context.view_layer.update()
    print("pose:", POSE, "@", POSE_FRAME)

def world_bbox():
    mins = Vector((1e9,) * 3)
    maxs = Vector((-1e9,) * 3)
    for o in meshes:
        if o.hide_render:
            continue
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c)
            mins = Vector(map(min, mins, w))
            maxs = Vector(map(max, maxs, w))
    return mins, maxs

# lights
def add_light(name, ltype, loc, energy, color, size=1.0):
    d = bpy.data.lights.new(name, ltype)
    d.energy = energy
    d.color = color
    if hasattr(d, "size"):
        d.size = size
    o = bpy.data.objects.new(name, d)
    o.location = loc
    scene.collection.objects.link(o)
    return o

def point_at(obj, target):
    d = Vector(target) - obj.location
    obj.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()

world = bpy.data.worlds.new("IconWorld")
scene.world = world
world.use_nodes = True
bg_node = world.node_tree.nodes["Background"]
bg_node.inputs[0].default_value = (0.05, 0.055, 0.07, 1.0)
bg_node.inputs[1].default_value = 0.55

key  = add_light("Key",  'AREA', (3.0, -4.0, 4.5),  950,  (1.0, 0.87, 0.70), 3.0)
rim  = add_light("Rim",  'AREA', (-2.5, 1.8, 4.2),  1250, (0.45, 0.75, 1.0), 2.0)
fill = add_light("Fill", 'AREA', (-1.6, -3.0, 0.8), 430,  (0.70, 0.78, 0.95), 2.5)
kick = add_light("Kick", 'AREA', (1.8,  1.2, 0.6),  500,  (1.0, 0.55, 0.30), 1.5)

mins, maxs = world_bbox()
cx = (mins.x + maxs.x) / 2
head_z = mins.z + (maxs.z - mins.z) * 0.78
for l, t in ((key, (cx, 0, head_z + 0.1)), (rim, (cx, 0, head_z + 0.2)),
             (fill, (cx, 0, head_z - 0.2)), (kick, (cx, 0, head_z - 0.5))):
    point_at(l, t)

cam_d = bpy.data.cameras.new("IconCam")
cam = bpy.data.objects.new("IconCam", cam_d)
scene.collection.objects.link(cam)
scene.camera = cam
cam.location = (cx + 0.62, -4.6, head_z + 0.20)
point_at(cam, (cx, 0.0, head_z + 0.02))
cam_d.lens = 95

scene.render.engine = 'BLENDER_EEVEE'
scene.render.resolution_x = 2048
scene.render.resolution_y = 2048
scene.render.resolution_percentage = 100
scene.render.film_transparent = True
scene.render.image_settings.file_format = 'PNG'
scene.render.image_settings.color_mode = 'RGBA'
scene.render.image_settings.color_depth = '8'

KNIGHT_PNG = os.path.join(WORK, "knight_2048.png")
if not (REUSE and os.path.exists(KNIGHT_PNG)):
    scene.render.filepath = KNIGHT_PNG
    bpy.ops.render.render(write_still=True)
    print("knight render ->", KNIGHT_PNG)

# ================================================================== numpy io
def img_rgba(img):
    w, h = img.size
    return np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)

def save_rgba(arr, path, size=None):
    h, w = arr.shape[:2]
    img = bpy.data.images.new("tmp_" + str(w), width=w, height=h, alpha=True, float_buffer=False)
    img.pixels.foreach_set(arr.reshape(-1).astype(np.float32))
    if size and (size != w):
        img.scale(size, size)
    img.filepath_raw = path
    img.file_format = 'PNG'
    img.save()
    bpy.data.images.remove(img)

def load_rgba(path):
    img = bpy.data.images.load(path)
    a = img_rgba(img).copy()
    bpy.data.images.remove(img)
    return a

def resize(a, s):
    return resize_aspect(a, s, s)

def premul(a):
    out = a.copy()
    out[..., :3] *= a[..., 3:4]
    return out

def over(fg, bg):
    """fg over bg, both non-premultiplied RGBA float."""
    fa = fg[..., 3:4]
    ba = bg[..., 3:4]
    oa = fa + ba * (1 - fa)
    rgb = (fg[..., :3] * fa + bg[..., :3] * ba * (1 - fa)) / np.clip(oa, 1e-6, None)
    return np.concatenate([rgb, oa], axis=-1)

# ================================================================== artwork
S = 2048
yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
u = xx / (S - 1)                    # 0..1
v = 1 - yy / (S - 1)                # 0..1 bottom-up (image row 0 = bottom)

# ---- sunburst background ----------------------------------------------------
cx_b, cy_b = 0.50, 0.42             # burst center slightly below middle
dx = u - cx_b
dy = v - cy_b
r = np.sqrt(dx * dx + dy * dy)
ang = np.arctan2(dy, dx)

# radial base gradient: warm center -> deep orange edge
grad = np.clip(r / 0.72, 0, 1)
base = (AMBER_HI[None, None] * (1 - grad[..., None]) ** 1.4
        + AMBER_MID[None, None] * (1 - (1 - grad[..., None]) ** 1.4))
deep = np.clip((r - 0.45) / 0.45, 0, 1) ** 1.6
base = base * (1 - deep[..., None]) + AMBER_DEEP[None, None] * deep[..., None]

# faint rays only near the rim — converge-zone reads as a dirty halo at icon size
N_RAYS = 14
rayf = np.abs(np.sin(ang * N_RAYS / 2))
rays = np.clip((rayf - 0.55) / 0.28, 0, 1)
rays *= np.clip((r - 0.52) / 0.30, 0, 1)             # exist only past mid-radius
rays *= np.clip(1 - r / 1.15, 0, 1)
bg_rgb = base * (1 - rays[..., None] * 0.16) + RAY_DARK[None, None] * rays[..., None] * 0.16

# warm glow pool behind the head — silhouette pops
glow = np.clip(1 - r / 0.50, 0, 1) ** 2.0 * 0.42
bg_rgb += glow[..., None] * np.array([1.0, 0.9, 0.6])[None, None]

# gentle top-light sheen + corner vignette
sheen = np.clip(1 - v, 0, 1) ** 2 * 0.10
bg_rgb += sheen[..., None]
vig = np.clip((r - 0.62) / 0.55, 0, 1) ** 2 * 0.22
bg_rgb *= (1 - vig[..., None])

BG = np.concatenate([bg_rgb, np.ones((S, S, 1), np.float32)], axis=-1).astype(np.float32)

# soft contact shadow under the shoulders — lives in BG so adaptive icons match
# (1 - v) is the height fraction from the BOTTOM: 0 = bottom edge
cy_T = 0.16
ex = (u - 0.5) / 0.31
ey = ((1 - v) - cy_T) / 0.08
sh = np.clip(1 - (ex * ex + ey * ey), 0, 1) ** 1.8 * 0.40
bg_rgb2 = BG[..., :3] * (1 - sh[..., None])
BG[..., :3] = bg_rgb2

# ---- load knight, trim transparent margin ----------------------------------
k = load_rgba(KNIGHT_PNG)
alpha = k[..., 3]
cols = np.where(alpha.max(axis=0) > 0.02)[0]
rows = np.where(alpha.max(axis=1) > 0.02)[0]
k = k[rows.min():rows.max() + 1, cols.min():cols.max() + 1]
kh, kw = k.shape[:2]
print("knight trim:", kw, "x", kh)

def place(fg, canvas, scale_w, cx_frac, cy_frac):
    """Scale fg so its width = scale_w*canvas, center at (cx_frac, cy_frac bottom-up)."""
    out = np.zeros((canvas, canvas, 4), np.float32)
    nw = int(scale_w * canvas)
    nh = max(1, int(kh * nw / kw))
    fgs = resize_aspect(fg, nw, nh)
    x0 = int(cx_frac * canvas - nw / 2)
    y0 = int((1 - cy_frac) * canvas - nh / 2)   # cy_frac measured bottom-up
    x1, y1 = x0 + nw, y0 + nh
    sx0, sy0 = max(0, -x0), max(0, -y0)
    dx0, dy0 = max(0, x0), max(0, y0)
    dx1, dy1 = min(canvas, x1), min(canvas, y1)
    if dx1 > dx0 and dy1 > dy0:
        out[dy0:dy1, dx0:dx1] = fgs[sy0:sy0 + (dy1 - dy0), sx0:sx0 + (dx1 - dx0)]
    return out

def resize_aspect(a, nw, nh):
    h, w = a.shape[:2]
    yf = np.linspace(0, h - 1, nh); xf = np.linspace(0, w - 1, nw)
    y0 = np.floor(yf).astype(int); y1 = np.clip(y0 + 1, 0, h - 1)
    x0 = np.floor(xf).astype(int); x1 = np.clip(x0 + 1, 0, w - 1)
    wy = (yf - y0)[:, None]; wx = (xf - x0)[None, :]
    c00 = a[np.ix_(y0, x0)]; c10 = a[np.ix_(y1, x0)]
    c01 = a[np.ix_(y0, x1)]; c11 = a[np.ix_(y1, x1)]
    top = c00 * (1 - wy)[..., None] + c10 * wy[..., None]
    bot = c01 * (1 - wy)[..., None] + c11 * wy[..., None]
    return top * (1 - wx)[..., None] + bot * wx[..., None]

# ---- variants ---------------------------------------------------------------
ICON_SQ = 1024
sq_bg = resize(BG, ICON_SQ)
square = over(place(k, ICON_SQ, 0.80, 0.5, 0.47), sq_bg)

# round icon — circular badge clip
r2 = np.sqrt((np.mgrid[0:ICON_SQ, 0:ICON_SQ][1] / ICON_SQ - 0.5) ** 2
             + (0.5 - np.mgrid[0:ICON_SQ, 0:ICON_SQ][0] / ICON_SQ) ** 2)
edge = 0.5
circ = np.clip((edge - r2) * ICON_SQ * 0.5, 0, 1)[..., None]
round_icon = over(place(k, ICON_SQ, 0.72, 0.5, 0.46), sq_bg)
round_icon[..., :3] *= circ
round_icon[..., 3] *= circ[..., 0]

# adaptive foreground — whole bust inside 61% safe circle, slightly above center
fg1024 = place(k, ICON_SQ, 0.60, 0.5, 0.46)

# splash badge — navy disc + amber ring + knight head inside, transparent corners
disc_r = 0.38
outer = np.clip((disc_r + 0.045 - r2) * ICON_SQ * 0.5, 0, 1)[..., None]  # badge edge
inner = np.clip((disc_r - r2) * ICON_SQ * 0.5, 0, 1)[..., None]         # disc fill
band = np.clip(outer - inner, 0, 1)
disc_rgb = (NAVY[None, None] * (1 - np.clip(1 - r2 / disc_r, 0, 1)[..., None] * 0.55)
            + NAVY_HI[None, None] * np.clip(1 - r2 / disc_r, 0, 1)[..., None] * 0.55)
sp_rgb = AMBER_MID[None, None] * band + disc_rgb * inner
sp_bg = np.concatenate([sp_rgb, outer], axis=-1).astype(np.float32)
splash = over(place(k, ICON_SQ, 0.50, 0.5, 0.44), sp_bg)

# ================================================================== writes
save_rgba(square, os.path.join(DST, "AppIcon.png"))                 # 1024
save_rgba(square, os.path.join(DST, "StoreIcon.png"), 512)
save_rgba(splash, os.path.join(DST, "SplashIcon.png"), 512)
save_rgba(square, os.path.join(WORK, "preview_square.png"))
save_rgba(round_icon, os.path.join(WORK, "preview_round.png"))
save_rgba(over(fg1024, sq_bg), os.path.join(WORK, "preview_adaptive.png"))

for n in (432, 324, 216, 162, 108, 81):
    save_rgba(fg1024, os.path.join(DST, "IconFG_%d.png" % n), n)
    save_rgba(BG,     os.path.join(DST, "IconBG_%d.png" % n), n)
for n in (432, 324, 216, 192, 162, 144, 108, 96, 81, 72, 48, 36):
    save_rgba(square,     os.path.join(DST, "Icon_%d.png" % n), n)
    save_rgba(round_icon, os.path.join(DST, "IconRound_%d.png" % n), n)
save_rgba(square, os.path.join(DST, "Icon_128.png"), 128)   # default-target slot

print("ICONS WRITTEN to", DST)
