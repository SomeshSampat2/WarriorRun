#!/usr/bin/env python3
"""
WarriorRun — loading-screen backdrop renders.
Run:  Blender --background --python blender_loading_shots.py
Renders four portrait biome shots (forest / lava / city / snow) using the
game's own prop library + the Knight hero, to
Assets/WarriorRun/Resources/Loading/. Also stamps runner.png (knight face
token for the progress bar) and emblem.png (splash badge) next to them.
"""
import bpy, math, os, sys
from mathutils import Vector
import numpy as np

ROOT = os.path.dirname(os.path.abspath(__file__))
ART = os.path.join(ROOT, "Assets", "WarriorRun", "Art")
MODELS = os.path.join(ART, "Realistic", "Models")
KNIGHT_GLB = os.path.join(ART, "CharNew", "Knight.glb")
OUT = os.path.join(ROOT, "Assets", "WarriorRun", "Resources", "Loading")
WORK = os.path.join(ROOT, "icon_work")
os.makedirs(OUT, exist_ok=True)

W, H = 720, 1280
HIDE = {"2H_Sword", "1H_Sword_Offhand", "Badge_Shield", "Rectangle_Shield", "Spike_Shield"}

# ---------------------------------------------------------------- utils
def wipe():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.view_settings.view_transform = 'Standard'   # saturated flat low-poly look
    sc.view_settings.look = 'Medium High Contrast'
    sc.view_settings.exposure = 0.0

def import_gltf(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    return [o for o in bpy.data.objects if o not in before]

def bbox_of(objs):
    mins = Vector((1e9,) * 3); maxs = Vector((-1e9,) * 3)
    for o in objs:
        if o.type != 'MESH':
            continue
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c)
            mins = Vector(map(min, mins, w))
            maxs = Vector(map(max, maxs, w))
    return mins, maxs

def add_prop(name, loc, target_h, rot_z=0.0):
    gltf = os.path.join(MODELS, name, name + ".gltf")
    if not os.path.exists(gltf):
        print("MISSING PROP", name)
        return []
    objs = import_gltf(gltf)
    mins, maxs = bbox_of(objs)
    size = max(maxs.z - mins.z, 0.01)
    s = target_h / size
    root = bpy.data.objects.new("P_" + name, None)
    bpy.context.scene.collection.objects.link(root)
    for o in objs:
        if o.parent is None:
            o.parent = root
    root.scale = (s,) * 3
    root.location = (loc[0], loc[1], -mins.z * s + loc[2])
    root.rotation_euler = (0, 0, rot_z)
    bpy.context.view_layer.update()
    return [root] + objs

def flat_mat(name, rgb, rough=0.95, emit=None):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*rgb, 1)
    b.inputs["Roughness"].default_value = rough
    if emit is not None:
        b.inputs["Emission Color"].default_value = (emit[0], emit[1], emit[2], 1)
        b.inputs["Emission Strength"].default_value = emit[3] if len(emit) > 3 else 2.0
    return m

def ground(name, size, rgb, z=0.0):
    m = flat_mat(name, rgb)
    bpy.ops.mesh.primitive_plane_add(size=size, location=(0, size * 0.35, z))
    p = bpy.context.object
    p.name = name
    p.data.materials.append(m)
    return p

def road(rgb, w=2.2, length=200.0, z=0.02):
    m = flat_mat("Road", rgb)
    bpy.ops.mesh.primitive_plane_add(size=1, location=(0, length / 2 - 10, z))
    p = bpy.context.object
    p.name = "Road"
    p.scale = (w, length, 1)
    p.data.materials.append(m)
    return p

def sky(top_rgb, horizon_rgb, z_top=-0.32, z_hor=-0.03):
    """View-direction gradient. World Normal Z is negative for up-rays, so the
    top color sits at the LOW ramp position and the horizon near zero."""
    w = bpy.data.worlds.new("Sky")
    w.use_nodes = True
    nt = w.node_tree
    bg = nt.nodes["Background"]
    tc = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].position = z_top
    ramp.color_ramp.elements[0].color = (*top_rgb, 1)
    ramp.color_ramp.elements[1].position = z_hor
    ramp.color_ramp.elements[1].color = (*horizon_rgb, 1)
    nt.links.new(tc.outputs["Normal"], sep.inputs[0])
    nt.links.new(sep.outputs["Z"], ramp.inputs[0])
    nt.links.new(ramp.outputs["Color"], bg.inputs[0])
    bg.inputs[1].default_value = 0.9
    bpy.context.scene.world = w

def sun(energy, color, rot=(0.6, 0.15, -0.5)):
    d = bpy.data.lights.new("Sun", 'SUN')
    d.energy = energy
    d.color = color
    o = bpy.data.objects.new("Sun", d)
    o.rotation_euler = rot
    bpy.context.scene.collection.objects.link(o)
    return o

def point(name, loc, energy, color, radius=1.0):
    d = bpy.data.lights.new(name, 'POINT')
    d.energy = energy
    d.color = color
    d.shadow_soft_size = radius
    o = bpy.data.objects.new(name, d)
    o.location = loc
    bpy.context.scene.collection.objects.link(o)
    return o

def fog_cube(density, color, size=(70, 70, 40), loc=(0, 30, 20)):
    m = bpy.data.materials.new("Fog")
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    vol = nt.nodes.new("ShaderNodeVolumePrincipled")
    vol.inputs["Density"].default_value = density
    vol.inputs["Color"].default_value = (*color, 1)
    nt.links.new(vol.outputs["Volume"], out.inputs["Volume"])
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    c = bpy.context.object
    c.scale = size
    c.data.materials.append(m)
    return c

def knight(loc=(0.55, 0.0, 0.0), pose="Running_A", frame=12, yaw=0.35, target_h=1.9):
    objs = import_gltf(KNIGHT_GLB)
    arm = next((o for o in objs if o.type == 'ARMATURE'), None)
    for o in objs:
        if o.type == 'MESH' and (o.name in HIDE or o.name.split('.')[0] in HIDE):
            o.hide_render = True
            o.hide_viewport = True
    actions = {a.name: a for a in bpy.data.actions}
    if arm and pose in actions:
        if arm.animation_data is None:
            arm.animation_data_create()
        act = actions[pose]
        arm.animation_data.action = act
        try:
            if len(act.slots):
                arm.animation_data.action_slot = act.slots[0]
        except Exception:
            pass
        bpy.context.scene.frame_set(frame)
    root = bpy.data.objects.new("KnightRoot", None)
    bpy.context.scene.collection.objects.link(root)
    for o in objs:
        if o.parent is None:
            o.parent = root
    bpy.context.view_layer.update()
    mins, maxs = bbox_of(objs)
    s = target_h / max(maxs.z - mins.z, 0.01)
    root.scale = (s,) * 3
    root.location = (loc[0] - (mins.x + maxs.x) / 2 * s, loc[1] - (mins.y + maxs.y) / 2 * s,
                     loc[2] - mins.z * s)
    root.rotation_euler = (0, 0, yaw)
    return objs

def coin(loc, r=0.22):
    m = flat_mat("Coin", (1.0, 0.78, 0.24), rough=0.35, emit=(1.0, 0.6, 0.1, 0.6))
    bpy.ops.mesh.primitive_cylinder_add(radius=r, depth=0.06, location=loc,
                                        rotation=(math.pi / 2, 0, 0))
    bpy.context.object.data.materials.append(m)

def camera(loc, target, lens=50):
    d = bpy.data.cameras.new("Cam")
    o = bpy.data.objects.new("Cam", d)
    bpy.context.scene.collection.objects.link(o)
    bpy.context.scene.camera = o
    o.location = loc
    dirv = Vector(target) - Vector(loc)
    o.rotation_euler = dirv.to_track_quat('-Z', 'Y').to_euler()
    d.lens = lens
    return o

def render(name):
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_EEVEE'
    sc.render.resolution_x = W
    sc.render.resolution_y = H
    sc.render.resolution_percentage = 100
    sc.render.image_settings.file_format = 'PNG'
    sc.render.image_settings.color_mode = 'RGB'
    sc.render.image_settings.color_depth = '8'
    sc.render.film_transparent = False
    sc.render.filepath = os.path.join(OUT, name + ".png")
    bpy.ops.render.render(write_still=True)
    print("wrote", name)

# ---------------------------------------------------------------- scenes
def scene_forest():
    wipe()
    sky((0.28, 0.52, 0.85), (0.98, 0.72, 0.40))
    sun(2.6, (1.0, 0.88, 0.70), rot=(0.7, 0.1, -0.7))
    ground("Grass", 160, (0.22, 0.44, 0.14))
    road((0.44, 0.35, 0.24))
    add_prop("hd_pine_a", (-2.4, 3.2, 0), 3.4)
    add_prop("hd_pine_b", (2.7, 4.0, 0), 2.7)
    add_prop("hd_pine_a", (-3.6, 6.5, 0), 4.4)
    add_prop("hd_oak",   (3.8, 7.0, 0), 3.6)
    add_prop("hd_birch", (-3.0, 9.5, 0), 3.2)
    add_prop("hd_pine_b", (3.4, 11, 0), 4.0)
    add_prop("hd_maple", (-4.6, 12, 0), 3.6)
    add_prop("boulder_01", (2.2, 1.8, 0), 0.8)
    add_prop("hd_boulder", (-2.2, 4.5, 0), 1.1)
    add_prop("grass_medium_01", (-1.5, 1.3, 0), 0.5)
    add_prop("grass_medium_02", (1.6, 2.3, 0), 0.55)
    add_prop("hd_flowers", (-2.0, 2.2, 0), 0.4)
    add_prop("hd_bush", (2.5, 5.5, 0), 0.8)
    coin((-0.55, 2.4, 0.9))
    coin((0.0, 3.4, 1.15))
    coin((0.55, 4.4, 0.9))
    knight(loc=(0.45, 0.4, 0), pose="Running_A", frame=12, yaw=0.30)
    camera((0.0, -5.0, 1.30), (0.3, 2.0, 1.15), lens=50)
    render("bg0")

def scene_lava():
    wipe()
    sky((0.07, 0.02, 0.06), (0.85, 0.26, 0.04))
    sun(1.4, (1.0, 0.45, 0.2), rot=(0.9, 0.0, -0.4))
    ground("Basalt", 160, (0.030, 0.022, 0.030))
    road((0.06, 0.04, 0.05))
    lm = flat_mat("LavaGlow", (0.4, 0.05, 0.0), emit=(1.0, 0.22, 0.02, 4.5))
    for x in (-2.6, 2.6):
        bpy.ops.mesh.primitive_plane_add(size=10, location=(x, 8, 0.04))
        p = bpy.context.object; p.scale.y = 7
        p.data.materials.append(lm)
    point("LavaL", (-2.6, 3, 0.5), 350, (1.0, 0.26, 0.05), 1.2)
    point("LavaR", (2.6, 6, 0.5), 420, (1.0, 0.30, 0.06), 1.2)
    point("EmberTop", (0, 8, 5), 250, (1.0, 0.40, 0.15), 3.0)
    add_prop("bl_lavacrust_a", (-2.3, 2.2, 0), 0.75)
    add_prop("bl_lavacrust_b", (2.5, 3.2, 0), 0.95)
    add_prop("hd_basalt_hex", (-3.0, 5.0, 0), 2.8)
    add_prop("hd_basalt_hex", (3.6, 6.5, 0), 3.6)
    add_prop("hd_basalt_hex", (-4.4, 9.0, 0), 3.2)
    add_prop("hd_obsidian", (4.0, 2.6, 0), 1.7)
    add_prop("hd_lavabomb", (-3.2, 8.0, 0), 1.2)
    add_prop("hd_rockspire", (-4.6, 11, 0), 4.6)
    add_prop("hd_charred", (2.9, 9.0, 0), 2.4)
    fog_cube(0.006, (0.20, 0.05, 0.03))
    knight(loc=(0.45, 0.5, 0), pose="2H_Melee_Idle", frame=30, yaw=0.15)
    camera((0.0, -5.2, 1.45), (0.3, 2.5, 1.25), lens=50)
    render("bg1")

def scene_city():
    wipe()
    sky((0.02, 0.04, 0.16), (0.38, 0.12, 0.45))
    sun(1.5, (0.40, 0.50, 1.0), rot=(0.8, 0.0, -0.2))
    ground("Asphalt", 160, (0.030, 0.036, 0.058))
    road((0.05, 0.052, 0.08))
    add_prop("hd_bldg_tower", (-5.4, 8.5, 0), 9.0)
    add_prop("hd_bldg_shop",  (-4.6, 7.0, 0), 4.0)
    add_prop("hd_bldg_brick", (4.8, 8.0, 0), 6.0)
    add_prop("hd_bldg_modern",(6.4, 10, 0), 8.0)
    add_prop("hd_neon_pylon", (-2.5, 3.6, 0), 2.3)
    add_prop("hd_neon_sign",  (3.0, 4.8, 0), 2.4)
    add_prop("hd_sedan",      (3.4, 1.6, 0), 1.5, rot_z=0.12)
    add_prop("hd_streetlamp", (-2.2, 2.6, 0), 3.4)
    add_prop("hd_trafficlight", (-2.8, 6.5, 0), 3.0)
    add_prop("hd_neon_shard", (2.2, 8.0, 0), 1.9)
    point("NeonL", (-2.4, 3.6, 2.0), 700, (1.0, 0.20, 0.9), 1.4)
    point("NeonR", (3.0, 4.8, 2.2), 700, (0.15, 0.85, 1.0), 1.4)
    fog_cube(0.005, (0.08, 0.06, 0.16))
    knight(loc=(0.45, 0.5, 0), pose="Running_A", frame=12, yaw=0.28)
    camera((0.0, -5.4, 1.40), (0.2, 2.5, 1.35), lens=52)
    render("bg2")

def scene_snow():
    wipe()
    sky((0.30, 0.52, 0.85), (0.78, 0.86, 0.96))
    sun(2.8, (1.0, 0.92, 0.80), rot=(0.85, 0.15, -0.7))
    ground("Snow", 160, (0.60, 0.68, 0.84))
    road((0.42, 0.50, 0.66))
    add_prop("hd_pine_snow",   (-2.6, 3.4, 0), 3.6)
    add_prop("hd_pine_snow_b", (3.0, 4.4, 0), 3.0)
    add_prop("hd_pine_snow",   (-4.0, 7.0, 0), 4.6)
    add_prop("hd_pine_snow_b", (4.2, 8.5, 0), 4.0)
    add_prop("hd_snowdrift",   (-2.0, 2.0, 0), 0.7)
    add_prop("hd_frozenrock",  (2.3, 2.6, 0), 1.1)
    add_prop("hd_icecrystal",  (2.9, 6.0, 0), 1.6)
    add_prop("hd_snowman",     (-2.6, 5.2, 0), 1.5)
    fog_cube(0.004, (0.60, 0.72, 0.88))
    knight(loc=(0.45, 0.5, 0), pose="Jump_Idle", frame=10, yaw=0.25)
    camera((0.0, -5.2, 1.45), (0.3, 2.2, 1.35), lens=50)
    render("bg3")

scene_forest()
scene_lava()
scene_city()
scene_snow()

# ---------------------------------------------------------------- tokens
def load_rgba(path):
    img = bpy.data.images.load(path)
    a = np.array(img.pixels[:], dtype=np.float32).reshape(img.size[1], img.size[0], 4).copy()
    bpy.data.images.remove(img)
    return a

def save_rgba(arr, path):
    h, w = arr.shape[:2]
    img = bpy.data.images.new("t", width=w, height=h, alpha=True)
    img.pixels.foreach_set(arr.reshape(-1).astype(np.float32))
    img.filepath_raw = path
    img.file_format = 'PNG'
    img.save()
    bpy.data.images.remove(img)

src = load_rgba(os.path.join(WORK, "knight_2048.png"))
alpha = src[..., 3]
cols = np.where(alpha.max(axis=0) > 0.02)[0]
rows = np.where(alpha.max(axis=1) > 0.02)[0]
src = src[rows.min():rows.max() + 1, cols.min():cols.max() + 1]
sh, sw = src.shape[:2]
cw = min(sw, sh)
cy0 = int(sh * 0.30)
crop = src[cy0:cy0 + cw, 0:cw]
N = 256
yi = np.linspace(0, crop.shape[0] - 1, N).astype(int)
xi = np.linspace(0, crop.shape[1] - 1, N).astype(int)
save_rgba(crop[np.ix_(yi, xi)], os.path.join(OUT, "runner.png"))

import shutil
splash_src = os.path.join(ROOT, "Assets", "WarriorRun", "Sprites", "AppIcon", "SplashIcon.png")
if os.path.exists(splash_src):
    shutil.copyfile(splash_src, os.path.join(OUT, "emblem.png"))
    print("emblem copied")
print("ALL DONE")
