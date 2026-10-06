#!/usr/bin/env python3
"""
WarriorRun — turn-corner props ("Temple Run" junction dressing).

Generates two stylized low-poly models alongside the existing bl_* set:
  bl_turnsign  — weathered trail post with a big carved arrow plank
                 (arrow points local +X; object "ArrowTip" marks the tip so
                 the Unity builder can detect the facing axis regardless of
                 exporter handedness)
  bl_gate      — chunky sandstone gate: two stepped pillars + lintel +
                 keystone, walk-through axis along local +Z

Run:  Blender --background --python blender_turn_assets.py
"""
import bpy, bmesh, os, math, random
from mathutils import Vector

random.seed(20261006)

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                    "Assets", "WarriorRun", "Art", "Realistic")
MODELS = os.path.join(ROOT, "Models")

PAL = {
    "wood":      "#C58A4A", "woodDark":  "#9C6B38", "woodCut":   "#D9A86C",
    "rope":      "#8A6B45", "lantern":   "#FFD76E",
    "sandstone": "#E0B56A", "sandDark":  "#C29551", "sandLight": "#F0CD8A",
    "rock":      "#A99E92", "rockDark":  "#857B70",
    "paint":     "#E4572E",
    "gold":      "#F0B93E",
}

def C(hexstr):
    h = hexstr.lstrip('#')
    r, g, b = (int(h[i:i+2], 16) / 255.0 for i in (0, 2, 4))
    return (r ** 2.2, g ** 2.2, b ** 2.2, 1.0)

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    col = bpy.data.collections.new("Asset")
    bpy.context.scene.collection.children.link(col)
    return col

def link(obj, col):
    for c in list(obj.users_collection):
        c.objects.unlink(obj)
    col.objects.link(obj)

def active(obj):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj

def mat(name, key, rough=0.85, metal=0.0, emit=0.0):
    m = bpy.data.materials.get("M_" + name)
    if m: return m
    m = bpy.data.materials.new("M_" + name)
    m.use_nodes = True
    bs = m.node_tree.nodes["Principled BSDF"]
    bs.inputs["Base Color"].default_value = C(PAL.get(key, key))
    bs.inputs["Roughness"].default_value = rough
    bs.inputs["Metallic"].default_value = metal
    if emit > 0:
        if "Emission Color" in bs.inputs:
            bs.inputs["Emission Color"].default_value = C(PAL.get(key, key))
            bs.inputs["Emission Strength"].default_value = emit
        elif "Emission" in bs.inputs:
            bs.inputs["Emission"].default_value = C(PAL.get(key, key))
            bs.inputs["Emission Strength"].default_value = emit
    return m

def assign(obj, m):
    if obj.data and hasattr(obj.data, "materials"):
        obj.data.materials.append(m)

def flat(obj):
    if obj.type == 'MESH':
        for p in obj.data.polygons: p.use_smooth = False

def bevel(obj, width=0.04, segments=1, angle=math.radians(30)):
    m = obj.modifiers.new("Bev", 'BEVEL')
    m.width = width; m.segments = segments
    m.limit_method = 'ANGLE'; m.angle_limit = angle
    active(obj)
    try: bpy.ops.object.modifier_apply(modifier=m.name)
    except Exception as e: print("  mod fail", m.name, e)

def xapply(obj):
    active(obj)
    bpy.ops.object.mode_set(mode='OBJECT')
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

def recalc(obj):
    me = obj.data
    bm = bmesh.new(); bm.from_mesh(me)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me); bm.free()
    me.update()

def prim(kind, name, loc=(0,0,0), rot=(0,0,0), scale=(1,1,1), col=None, m=None, **kw):
    fn = {
        'cube':   bpy.ops.mesh.primitive_cube_add,
        'cyl':    bpy.ops.mesh.primitive_cylinder_add,
        'cone':   bpy.ops.mesh.primitive_cone_add,
        'ico':    bpy.ops.mesh.primitive_ico_sphere_add,
        'sphere': bpy.ops.mesh.primitive_uv_sphere_add,
        'torus':  bpy.ops.mesh.primitive_torus_add,
    }[kind]
    fn(location=loc, rotation=rot, **kw)
    o = bpy.context.active_object
    o.name = name
    o.scale = scale
    bpy.ops.object.transform_apply(scale=True)
    if col is not None: link(o, col)
    if m is not None: assign(o, m)
    return o

def mesh_obj(name, verts, faces, col, m=None):
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces); me.update()
    o = bpy.data.objects.new(name, me)
    col.objects.link(o)
    recalc(o)
    if m is not None: assign(o, m)
    return o

def export(col, asset_id):
    out_dir = os.path.join(MODELS, asset_id)
    os.makedirs(out_dir, exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT')
    for o in list(col.objects):
        if col in o.users_collection:
            col.objects.unlink(o)
            bpy.context.scene.collection.objects.link(o)
        o.select_set(True)
    path = os.path.join(out_dir, asset_id + ".gltf")
    kw = dict(filepath=path, export_format='GLTF_SEPARATE',
              use_selection=True, export_yup=True, export_apply=True,
              export_animations=False, export_skins=False,
              export_morph=False, export_cameras=False, export_lights=False)
    try:
        bpy.ops.export_scene.gltf(**kw)
    except TypeError:
        kw.pop("export_animations", None)
        bpy.ops.export_scene.gltf(**kw)
    n = sum(len(o.data.polygons) for o in bpy.data.objects if o.type == 'MESH')
    print(f"[turn_assets] {asset_id}: ~{n} polys")

# ---------------------------------------------------------------- turnsign
def turnsign():
    """Trail post. NOTE: Blender +X exports to glTF as +X, but glTFast flips
    to Unity -X — the builder reads the ArrowTip child's sign, so the visual
    direction is auto-detected at build time."""
    col = reset()
    wood = mat("SignWood", "wood", 0.8)
    dark = mat("SignDark", "woodDark", 0.85)
    stone = mat("SignStone", "rockDark", 0.9)
    rope = mat("SignRope", "rope", 0.9)
    paint = mat("SignPaint", "paint", 0.7)
    glow = mat("SignGlow", "lantern", 0.6, emit=2.0)

    # stone cairn base — three stacked slabs, slightly rotated
    b1 = prim('cube', "Base1", (0, 0, 0.10), scale=(0.62, 0.52, 0.20), col=col, m=stone)
    b1.rotation_euler[2] = math.radians(4); xapply(b1); bevel(b1, 0.05)
    b2 = prim('cube', "Base2", (0.03, -0.02, 0.28), scale=(0.50, 0.44, 0.18), col=col, m=stone)
    b2.rotation_euler[2] = math.radians(-7); xapply(b2); bevel(b2, 0.04)

    # post — gently tapered box
    post = prim('cube', "Post", (0, 0, 1.05), scale=(0.17, 0.17, 1.6), col=col, m=wood)
    post.rotation_euler[2] = math.radians(1.5); xapply(post); bevel(post, 0.025)
    # cap ball
    cap = prim('ico', "Cap", (0, 0, 1.92), scale=(0.13, 0.13, 0.13), subdivisions=1, col=col, m=dark)

    # arrow plank pointing +X at shoulder height: shaft + triangular head
    plank = prim('cube', "ArrowPlank", (0.18, 0, 1.62), scale=(1.05, 0.09, 0.30), col=col, m=wood)
    bevel(plank, 0.02)
    # arrowhead prism at +X tip (wedge): flat triangle extruded along Y
    tip = mesh_obj("ArrowTip",
        [(0.70, -0.055, 1.47), (0.70, 0.055, 1.47), (0.70, -0.055, 1.77), (0.70, 0.055, 1.77),
         (1.08, -0.055, 1.62), (1.08, 0.055, 1.62)],
        [(0, 1, 3), (0, 3, 2), (0, 4, 5), (0, 5, 1), (2, 3, 5), (2, 5, 4),
         (0, 2, 4), (1, 5, 3)],
        col, m=wood)
    # painted chevron on the plank faces (both sides so it reads either way)
    for sgn in (-1, 1):
        for i in range(2):
            cx = 0.10 + i * 0.26
            bar = prim('cube', f"Chev{sgn}_{i}", (cx, sgn * 0.052, 1.62),
                       rot=(0, math.radians(-35 * (1 if i == 0 else -1)), 0) if False else (0,0,0),
                       scale=(0.10, 0.012, 0.26), col=col, m=paint)
            # chevron look: squash each bar into a slanted dash
            bar.rotation_euler[1] = math.radians(-32)
            xapply(bar)

    # rope wrap where the plank meets the post
    for i, z in enumerate((1.50, 1.74)):
        t = prim('torus', f"Rope{i}", (0, 0, z), rot=(math.pi/2, 0, 0),
                 major_radius=0.11, minor_radius=0.028, col=col, m=rope)

    # tiny lantern dangling off the back end of the plank
    arm = prim('cube', "LanternArm", (-0.34, 0, 1.70), scale=(0.30, 0.05, 0.05), col=col, m=dark)
    lamp = prim('cube', "Lantern", (-0.44, 0, 1.50), scale=(0.14, 0.14, 0.20), col=col, m=glow)
    bevel(lamp, 0.02)
    frame = prim('cube', "LanternCap", (-0.44, 0, 1.63), scale=(0.18, 0.18, 0.05), col=col, m=dark)

    export(col, "bl_turnsign")

# ---------------------------------------------------------------- gate
def gate():
    """Chunky sandstone gate. Walk-through axis along local +Z (Blender -Y
    maps to Unity +Z, so build the opening along Blender Y)."""
    col = reset()
    sand = mat("GateSand", "sandstone", 0.85)
    dark = mat("GateDark", "sandDark", 0.9)
    light = mat("GateLight", "sandLight", 0.8)
    gold = mat("GateGold", "gold", 0.6, metal=0.3, emit=0.6)

    # two pillars at x=±4.1 (opening ~7m for the 9.4m-wide track arm)
    for sgn in (-1, 1):
        x = sgn * 4.1
        # plinth + three stacked blocks + cap — slightly jittered for age
        p0 = prim('cube', f"P{sgn}_Plinth", (x, 0, 0.25), scale=(1.5, 1.3, 0.5), col=col, m=dark)
        bevel(p0, 0.05)
        y = 0.9
        for i in range(3):
            w = 1.25 - i * 0.12
            blk = prim('cube', f"P{sgn}_B{i}", (x + random.uniform(-0.05, 0.05), 0, y + 0.6),
                       scale=(w, 1.15, 1.25), col=col, m=sand if i % 2 == 0 else light)
            blk.rotation_euler[2] = math.radians(random.uniform(-2.5, 2.5) * sgn)
            xapply(blk); bevel(blk, 0.06)
            y += 1.25
        capm = prim('cube', f"P{sgn}_Cap", (x, 0, y + 0.18), scale=(1.55, 1.35, 0.36), col=col, m=dark)
        capm.rotation_euler[2] = math.radians(random.uniform(-2, 2) * sgn)
        xapply(capm); bevel(capm, 0.05)
        # gold band inset on each pillar face
        band = prim('cube', f"P{sgn}_Band", (x, -0.60, 2.6), scale=(0.9, 0.06, 0.22), col=col, m=gold)
        bevel(band, 0.01)

    # lintel spanning the pillars at ~4.9m
    lin = prim('cube', "Lintel", (0, 0, 4.85), scale=(9.6, 1.05, 0.7), col=col, m=sand)
    bevel(lin, 0.06)
    lin2 = prim('cube', "LintelTop", (0, 0, 5.35), scale=(8.6, 0.8, 0.35), col=col, m=dark)
    bevel(lin2, 0.05)
    # keystone wedge + gold boss
    key = prim('cube', "Keystone", (0, 0, 4.35), scale=(0.7, 1.0, 0.9), col=col, m=light)
    bevel(key, 0.04)
    boss = prim('ico', "Boss", (0, -0.55, 4.85), scale=(0.24, 0.16, 0.24), subdivisions=1, col=col, m=gold)

    export(col, "bl_gate")

if __name__ == "__main__":
    turnsign()
    gate()
    print("[turn_assets] done")
