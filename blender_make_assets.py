#!/usr/bin/env python3
"""
WarriorRun — Blender procedural-realistic obstacle generator.
Run:  Blender --background --python blender_make_assets.py
Builds game-ready props with real geometry (bevels, displacement, lathed
profiles) + PolyHaven PBR texture sets already downloaded in Art/Realistic,
then exports each as glTF_SEPARATE (<id>.gltf + .bin + textures/) into
Art/Realistic/Models/<id>/ — the same layout the PolyHaven downloads use,
so RealisticUpgrade.LoadModel() picks them up unchanged.
"""
import bpy, bmesh, os, math, json, random, shutil, sys

random.seed(20261002)

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                    "Assets", "WarriorRun", "Art", "Realistic")
MODELS = os.path.join(ROOT, "Models")
TEX = os.path.join(ROOT, "Textures")

# ---------------------------------------------------------------- helpers

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    # keep a single "Assets" collection to parent everything under
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

def smart_uv(obj, angle=66, margin=0.03):
    active(obj)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(angle),
                             island_margin=margin, area_weight=True)
    bpy.ops.object.mode_set(mode='OBJECT')

def bevel(obj, width=0.03, segments=2, angle=math.radians(30)):
    m = obj.modifiers.new("EdgeBevel", 'BEVEL')
    m.width = width; m.segments = segments
    m.limit_method = 'ANGLE'; m.angle_limit = angle
    apply_mod(obj, m)

def apply_mod(obj, mod):
    active(obj)
    bpy.ops.object.modifier_apply(modifier=mod.name)

def displace(obj, scale, strength, mid=0.5):
    tex = bpy.data.textures.new(obj.name + "_nz", type='CLOUDS')
    tex.noise_scale = scale; tex.noise_depth = 2
    m = obj.modifiers.new("Erode", 'DISPLACE')
    m.texture = tex; m.strength = strength; m.mid_level = mid
    apply_mod(obj, m)

def subdiv(obj, lvl=2, simple=True):
    m = obj.modifiers.new("Sub", 'SUBSURF')
    m.subdivision_type = 'SIMPLE' if simple else 'CATMULL_CLARK'
    m.levels = lvl; m.render_levels = lvl
    apply_mod(obj, m)

def smooth(obj):
    for p in obj.data.polygons: p.use_smooth = True

def prim(kind, name, loc=(0,0,0), rot=(0,0,0), scale=(1,1,1), col=None, **kw):
    fn = {
        'cube':    bpy.ops.mesh.primitive_cube_add,
        'cyl':     bpy.ops.mesh.primitive_cylinder_add,
        'cone':    bpy.ops.mesh.primitive_cone_add,
        'ico':     bpy.ops.mesh.primitive_ico_sphere_add,
        'sphere':  bpy.ops.mesh.primitive_uv_sphere_add,
        'torus':   bpy.ops.mesh.primitive_torus_add,
    }[kind]
    fn(location=loc, rotation=rot, **kw)
    o = bpy.context.active_object
    o.name = name
    o.scale = scale
    bpy.ops.object.transform_apply(scale=True)
    if col is not None: link(o, col)
    return o

# ---- materials ----------------------------------------------------------

def mat_flat(name, rgb, rough=0.6, metal=0.0, coat=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bs = m.node_tree.nodes["Principled BSDF"]
    bs.inputs["Base Color"].default_value = (*rgb, 1.0)
    bs.inputs["Roughness"].default_value = rough
    bs.inputs["Metallic"].default_value = metal
    if "Coat Weight" in bs.inputs:
        bs.inputs["Coat Weight"].default_value = coat
    return m

def mat_tex(name, tex_id, rough_fallback=0.9):
    """PolyHaven set: <id>_Diffuse_1k.jpg + <id>_nor_gl_1k.jpg."""
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree; bs = nt.nodes["Principled BSDF"]
    d = os.path.join(TEX, tex_id)
    def img(suffix, noncolor=False):
        p = os.path.join(d, f"{tex_id}_{suffix}_1k.jpg")
        if not os.path.exists(p): return None
        i = bpy.data.images.load(p, check_existing=True)
        if noncolor: i.colorspace_settings.name = 'Non-Color'
        return i
    diff = img("Diffuse")
    if diff:
        t = nt.nodes.new("ShaderNodeTexImage"); t.image = diff
        nt.links.new(t.outputs["Color"], bs.inputs["Base Color"])
    nor = img("nor_gl", noncolor=True)
    if nor:
        t = nt.nodes.new("ShaderNodeTexImage"); t.image = nor
        nm = nt.nodes.new("ShaderNodeNormalMap")
        nt.links.new(t.outputs["Color"], nm.inputs["Color"])
        nt.links.new(nm.outputs["Normal"], bs.inputs["Normal"])
    bs.inputs["Roughness"].default_value = rough_fallback
    return m

def assign(obj, mat):
    if obj.data and hasattr(obj.data, "materials"):
        obj.data.materials.append(mat)

# ---- export -------------------------------------------------------------

def export(col, asset_id):
    out_dir = os.path.join(MODELS, asset_id)
    os.makedirs(out_dir, exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT')
    for o in col.objects:
        o.select_set(True)
        if o.users_collection and col in o.users_collection:
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
    except TypeError:                       # older exporter arg names
        kw.pop("export_animations", None)
        bpy.ops.export_scene.gltf(**kw)
    tidy_textures(out_dir, path)
    print(f"[assets] exported {asset_id}")

def tidy_textures(out_dir, gltf_path):
    """Move loose image files into textures/ and rewrite glTF URIs."""
    tex_dir = os.path.join(out_dir, "textures")
    with open(gltf_path) as f: g = json.load(f)
    moved = {}
    for fn in os.listdir(out_dir):
        if fn.lower().endswith((".jpg", ".jpeg", ".png", ".webp")):
            os.makedirs(tex_dir, exist_ok=True)
            shutil.move(os.path.join(out_dir, fn), os.path.join(tex_dir, fn))
            moved[fn] = "textures/" + fn
    dirty = False
    for im in g.get("images", []):
        u = im.get("uri", "")
        base = os.path.basename(u)
        if base in moved and u != moved[base]:
            im["uri"] = moved[base]; dirty = True
    if dirty or moved:
        with open(gltf_path, "w") as f: json.dump(g, f)

# ---------------------------------------------------------------- assets

def loft(name, profile, length, col):
    """Extrude a Y-Z cross-section profile along X (±length/2)."""
    n = len(profile)
    verts = [(-length/2, y, z) for y, z in profile] + \
            [( length/2, y, z) for y, z in profile]
    faces = []
    faces.append(tuple(range(n-1, -1, -1)))                    # back cap
    faces.append(tuple(range(n, 2*n)))                          # front cap
    for i in range(n):
        j = (i+1) % n
        faces.append((i, j, n+j, n+i))
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces); me.update()
    o = bpy.data.objects.new(name, me)
    col.objects.link(o)
    return o

def build_jersey_barrier(col):
    # classic jersey profile, 2.2m long, ~1.05m tall
    prof = [(-.28,0),(.28,0),(.28,.06),(.21,.17),(.135,.55),(.125,1.02),
            (-.125,1.02),(-.135,.55),(-.21,.17),(-.28,.06)]
    o = loft("jersey_barrier", prof, 2.2, col)
    bevel(o, 0.025, 2)
    subdiv(o, 2)
    displace(o, 0.35, 0.012)
    smart_uv(o)
    assign(o, mat_tex("concrete_worn", "concrete_floor_worn_02"))

def build_police_barrier(col):
    steel = mat_flat("galvanized_steel", (0.58,0.60,0.62), 0.38, 0.85)
    white = mat_flat("board_white", (0.92,0.92,0.90), 0.5)
    orange = mat_flat("board_orange", (0.85,0.28,0.04), 0.45)
    dark  = mat_flat("foot_rubber", (0.08,0.08,0.09), 0.8)
    L = 2.2; H = 1.08
    # frame tubes
    for x in (-L/2+0.06, L/2-0.06):
        t = prim('cyl', "upright", (x,0,H/2), radius=0.022, depth=H, col=col)
        assign(t, steel); smooth(t)
    for z in (H-0.04, 0.62):
        t = prim('cyl', "rail", (0,0,z), rot=(0,math.pi/2,0), radius=0.02,
                 depth=L-0.06, col=col)
        assign(t, steel); smooth(t)
    # welded joint balls
    for x in (-L/2+0.06, L/2-0.06):
        for z in (H-0.04, 0.62):
            s = prim('sphere', "joint", (x,0,z), radius=0.026,
                     segments=12, ring_count=8, col=col)
            assign(s, steel); smooth(s)
    # striped board (white plate + 3 orange bands)
    bd = prim('cube', "board", (0,-0.015,0.82), scale=(1.9,0.05,0.30), col=col)
    bevel(bd, 0.012, 2); assign(bd, white); smart_uv(bd)
    for i, x in enumerate((-0.62, 0.0, 0.62)):
        st = prim('cube', f"stripe{i}", (x,-0.045,0.82), scale=(0.42,0.012,0.30), col=col)
        assign(st, orange)
    # feet — flat plates splayed
    for x in (-L/2+0.06, L/2-0.06):
        f = prim('cube', "foot", (x,0,0.03), scale=(0.09,0.55,0.06), col=col)
        bevel(f, 0.015, 2); assign(f, steel)

def build_fallen_log(col):
    bark = mat_tex("bark", "bark_brown_01", 0.95)
    cut  = mat_flat("cut_wood", (0.62,0.44,0.27), 0.85)
    L, R = 2.2, 0.40
    log = prim('cone', "log", (0,0,R+0.02), rot=(0,math.pi/2,0),
               radius1=R, radius2=R*0.82, depth=L, vertices=20, col=col)
    subdiv(log, 2)
    displace(log, 0.5, 0.03)
    smart_uv(log); assign(log, bark); smooth(log)
    # cut end caps
    for x, r in ((-L/2-0.005, R), (L/2+0.005, R*0.82)):
        c = prim('cyl', "cut", (x,0,R+0.02), rot=(0,math.pi/2,0),
                 radius=r*0.97, depth=0.02, vertices=20, col=col)
        assign(c, cut)
        # growth rings
        for rr in (r*0.35, r*0.65):
            ring = prim('torus', "ring", (x-(0.012 if x<0 else -0.012),0,R+0.02),
                        rot=(0,math.pi/2,0), major_radius=rr, minor_radius=0.006,
                        major_segments=16, minor_segments=6, col=col)
            assign(ring, mat_flat("ring", (0.5,0.34,0.2), 0.9))
    # branch stubs
    for x, ang in ((-0.6, 35), (0.45, -50)):
        b = prim('cone', "stub", (x, 0.12, R+0.1),
                 rot=(math.radians(ang),0,0),
                 radius1=0.07, radius2=0.05, depth=0.3, vertices=10, col=col)
        assign(b, bark); smooth(b)

def build_boulder(col):
    rock = mat_tex("granite", "rock_ground", 0.95)
    o = prim('ico', "boulder", (0,0,0.95), subdivisions=4, radius=1.0, col=col)
    o.scale = (1.0, 0.86, 0.92)
    bpy.ops.object.transform_apply(scale=True)
    displace(o, 0.55, 0.30)
    displace(o, 0.16, 0.07)
    smart_uv(o); assign(o, rock); smooth(o)

def build_sandstone_block(col):
    stone = mat_tex("sandstone", "sandstone_blocks_04", 0.92)
    o = prim('cube', "block", (0,0,1.05), scale=(1.9,1.75,2.1), col=col)
    bevel(o, 0.05, 3)
    subdiv(o, 2)
    displace(o, 0.45, 0.018)
    smart_uv(o); assign(o, stone)
    # carved seam band near top (real groove)
    g = prim('cube', "groove", (0,0,1.72), scale=(1.905,1.755,0.06), col=col)
    assign(g, mat_flat("groove_shade", (0.45,0.33,0.2), 0.95))

def build_spikes(col):
    plate = mat_flat("plate_steel", (0.10,0.10,0.11), 0.55, 0.6)
    spike = mat_flat("spike_steel", (0.16,0.15,0.14), 0.42, 0.8)
    base = prim('cube', "base", (0,0,0.05), scale=(2.25,1.3,0.10), col=col)
    bevel(base, 0.02, 2); assign(base, plate); smart_uv(base)
    rows = [(-0.32, (-0.85,-0.28,0.28,0.85)), (0.32, (-0.6,0.0,0.6))]
    for ry, xs in rows:
        for x in xs:
            h = random.uniform(0.5, 0.85)
            s = prim('cone', "spike",
                     (x+random.uniform(-.06,.06), ry+random.uniform(-.05,.05),
                      0.1+h/2),
                     rot=(math.radians(random.uniform(-8,8)),
                          math.radians(random.uniform(-8,8)), 0),
                     radius1=random.uniform(0.13,0.17), radius2=0.012,
                     depth=h, vertices=7, col=col)
            assign(s, spike); smooth(s)

def build_traffic_cone(col):
    orange = mat_flat("cone_orange", (0.93,0.30,0.04), 0.42, 0.0, coat=0.25)
    white  = mat_flat("cone_reflect", (0.95,0.95,0.93), 0.28, 0.0, coat=0.5)
    rubber = mat_flat("cone_rubber", (0.07,0.07,0.07), 0.85)
    base = prim('cube', "base", (0,0,0.025), scale=(0.52,0.52,0.05), col=col)
    bevel(base, 0.02, 2); assign(base, rubber)
    body = prim('cone', "body", (0,0,0.42), radius1=0.23, radius2=0.045,
                depth=0.72, vertices=24, col=col)
    assign(body, orange); smooth(body)
    band = prim('cone', "band", (0,0,0.47), radius1=0.155, radius2=0.115,
                depth=0.17, vertices=24, col=col)
    assign(band, white); smooth(band)
    tip = prim('cyl', "tip", (0,0,0.80), radius=0.05, depth=0.06,
               vertices=16, col=col)
    assign(tip, orange); smooth(tip)

def build_dumpster(col):
    green = mat_flat("dumpster_paint", (0.06,0.22,0.10), 0.5, 0.55)
    darkg = mat_flat("dumpster_lid", (0.05,0.10,0.06), 0.55, 0.5)
    rust  = mat_flat("dumpster_rust", (0.30,0.14,0.06), 0.8, 0.1)
    dark  = mat_flat("wheel_rubber", (0.07,0.07,0.08), 0.85)
    # trapezoid body via loft (wider at top)
    prof = [(-0.62,0.18),(0.62,0.18),(0.70,1.28),(-0.70,1.28)]
    body = loft("body", prof, 2.05, col)
    bevel(body, 0.04, 2); assign(body, green); smart_uv(body)
    # side ribs
    for x in (-0.68, 0.0, 0.68):
        for s in (-1, 1):
            r = prim('cube', "rib", (x, s*0.665, 0.75),
                     scale=(0.10, 0.03, 1.0), col=col)
            bevel(r, 0.01, 2); assign(r, green)
    # two lids, slightly gapped
    for s in (-1, 1):
        lid = prim('cube', "lid", (0, s*0.33, 1.33),
                   rot=(math.radians(4*s), 0, 0),
                   scale=(1.98, 0.62, 0.07), col=col)
        bevel(lid, 0.025, 2); assign(lid, darkg)
    # hinge bar + side handles
    hb = prim('cyl', "hinge", (0,0,1.28), rot=(math.pi/2,0,0),
              radius=0.025, depth=1.95, col=col)
    assign(hb, rust); smooth(hb)
    for x in (-0.6, 0.6):
        h = prim('cyl', "handle", (x, 0.73, 1.05), rot=(math.pi/2,0,0),
                 radius=0.02, depth=0.22, col=col)
        assign(h, rust); smooth(h)
    # caster wheels
    for x in (-0.78, 0.78):
        for y in (-0.45, 0.45):
            w = prim('cyl', "wheel", (x,y,0.09), rot=(math.pi/2,0,0),
                     radius=0.09, depth=0.06, vertices=12, col=col)
            assign(w, dark); smooth(w)

def build_stump(col):
    bark = mat_tex("bark", "bark_brown_01", 0.95)
    cut  = mat_flat("stump_top", (0.66,0.50,0.33), 0.9)
    t = prim('cone', "stump", (0,0,0.5), radius1=0.62, radius2=0.46,
             depth=1.0, vertices=20, col=col)
    subdiv(t, 2); displace(t, 0.3, 0.035)
    smart_uv(t); assign(t, bark); smooth(t)
    top = prim('cyl', "top", (0,0,1.0), radius=0.44, depth=0.03,
               vertices=20, col=col)
    assign(top, cut)
    for rr in (0.14, 0.26, 0.36):
        ring = prim('torus', "ring", (0,0,1.02), major_radius=rr,
                    minor_radius=0.008, major_segments=20, minor_segments=6, col=col)
        assign(ring, mat_flat("ring_"+str(rr), (0.52,0.36,0.22), 0.95))
    for a in range(4):
        ang = math.radians(a*90 + 30)
        r = prim('cone', "root", (math.cos(ang)*0.55, math.sin(ang)*0.55, 0.12),
                 rot=(math.radians(62)*math.sin(ang), -math.radians(62)*math.cos(ang), 0),
                 radius1=0.15, radius2=0.05, depth=0.55, vertices=8, col=col)
        assign(r, bark); smooth(r)

def build_pillar(col):
    stone = mat_tex("sandstone", "sandstone_blocks_04", 0.92)
    plinth = prim('cube', "plinth", (0,0,0.14), scale=(1.15,1.15,0.28), col=col)
    bevel(plinth, 0.03, 2); assign(plinth, stone); smart_uv(plinth)
    shaft = prim('cyl', "shaft", (0,0,1.28), radius=0.40, depth=2.0,
                 vertices=20, col=col)
    # jagged fracture at the top — drag top verts down randomly
    me = shaft.data
    for v in me.vertices:
        if v.co.z > 0.55:   # upper part of the (local) shaft
            v.co.z -= random.uniform(0.0, 0.55) * (v.co.z - 0.55)
            v.co.x += random.uniform(-0.03, 0.03)
            v.co.y += random.uniform(-0.03, 0.03)
    bevel(shaft, 0.02, 2)
    smart_uv(shaft); assign(shaft, stone)
    cap = prim('torus', "cap_ring", (0,0,1.05), major_radius=0.42,
               minor_radius=0.05, major_segments=20, minor_segments=8, col=col)
    assign(cap, stone); smooth(cap)

# ---------------------------------------------------------------- main

ASSETS = {
    "bl_jersey_barrier":  build_jersey_barrier,
    "bl_police_barrier":  build_police_barrier,
    "bl_fallen_log":      build_fallen_log,
    "bl_boulder":         build_boulder,
    "bl_sandstone_block": build_sandstone_block,
    "bl_spikes":          build_spikes,
    "bl_traffic_cone":    build_traffic_cone,
    "bl_dumpster":        build_dumpster,
    "bl_stump":           build_stump,
    "bl_pillar_broken":   build_pillar,
}

for aid, fn in ASSETS.items():
    try:
        col = reset()
        fn(col)
        export(col, aid)
    except Exception as e:
        print(f"[assets] FAILED {aid}: {e}")
        import traceback; traceback.print_exc()

print("[assets] ALL DONE")
