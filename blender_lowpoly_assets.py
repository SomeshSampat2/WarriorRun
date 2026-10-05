#!/usr/bin/env python3
"""
WarriorRun — stylized low-poly asset regeneration ("Subway Surfers" pass).

Re-creates every model in Art/Realistic/Models/<id>/ as a chunky, colorful,
flat-shaded low-poly prop at roughly the same bounding box as the scanned
original, then re-exports in place (<id>.gltf + <id>.bin) so all Unity
.prefab references (.meta guids) keep working untouched.

Run:  Blender --background --python blender_lowpoly_assets.py
"""
import bpy, bmesh, os, math, json, random, shutil, sys
from mathutils import Vector

random.seed(20261005)

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                    "Assets", "WarriorRun", "Art", "Realistic")
MODELS = os.path.join(ROOT, "Models")

# ---------------------------------------------------------------- palette
# srgb hex -> linear tuple for Principled base color
def C(hexstr):
    h = hexstr.lstrip('#')
    r, g, b = (int(h[i:i+2], 16) / 255.0 for i in (0, 2, 4))
    return (r ** 2.2, g ** 2.2, b ** 2.2, 1.0)

PAL = {
    # foliage greens — saturated Subway-Surfers green ramp
    "leafA":     "#57B845", "leafB": "#79D356", "leafDark": "#3E9142",
    "leafWarm":  "#A8D94E", "leafBlue": "#3FA98F",
    "trunk":     "#8A5A32", "trunkDark": "#6B4226", "woodCut": "#D9A86C",
    "aloe":      "#4FAE7E", "aloeDark": "#3C8C66", "aloeTip": "#E8703A",
    "cactus":    "#4E9E5B", "cactusFlower": "#F2789F",
    "deadwood":  "#9A7B56", "deadDark": "#7A6043",
    # rocks / stone
    "rock":      "#A99E92", "rockDark": "#857B70", "rockLight": "#C4B9AB",
    "moonrock":  "#8E8FA0", "moss": "#7BA05B",
    "sandstone": "#E0B56A", "sandDark": "#C29551", "sandLight": "#F0CD8A",
    "cliff":     "#C98A5A", "cliffDark": "#A86B42",
    # city
    "hydrant":   "#E8402F", "hydrantCap": "#F5E9D0", "steel": "#9AA5B1",
    "trashcan":  "#5E6B75", "trashcanDark": "#49555E",
    "trashbag":  "#3E4A54", "bagKnot": "#2E3941",
    "lampPost":  "#37424E", "lampGlow": "#FFE9A8",
    "benchWood": "#F0A035", "benchLeg": "#3D4854",
    "concrete":  "#C9C4BB", "concreteDark": "#A39E94",
    "stripe":    "#F0682C", "wornWhite": "#F2EFE6",
    "crateWood": "#C58A4A", "crateEdge": "#9C6B38",
    "milCrate":  "#7A8354", "milEdge": "#5A6140",
    "carBody":   "#E85B3A", "carGlass": "#274156", "tarp": "#5FA8D3",
    "tire":      "#23272E", "rim": "#D9DEE4",
    "planter":   "#B4553B", "soil": "#5C4030",
    # temple / vault
    "barrel":    "#9C6B3C", "barrelHoop": "#4A4340",
    "lantern":   "#8A6B45", "lanternGlow": "#FFD76E",
    "statue":    "#C7BCA8", "statueDark": "#A09480", "gold": "#F0B93E",
    # obstacles
    "spike":     "#C9CDD4", "plate": "#4A4F58",
    "dumpster":  "#3E8E6E", "dumpsterLid": "#2F7057",
    "pillar":    "#E4C688", "pillarShade": "#C6A468",
}

# ---------------------------------------------------------------- helpers

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

def smooth(obj):
    if obj.type == 'MESH':
        for p in obj.data.polygons: p.use_smooth = True

def flat(obj):
    if obj.type == 'MESH':
        for p in obj.data.polygons: p.use_smooth = False

def bevel(obj, width=0.04, segments=1, angle=math.radians(30)):
    m = obj.modifiers.new("Bev", 'BEVEL')
    m.width = width; m.segments = segments
    m.limit_method = 'ANGLE'; m.angle_limit = angle
    apply_mod(obj, m)

def apply_mod(obj, mod):
    active(obj)
    try: bpy.ops.object.modifier_apply(modifier=mod.name)
    except Exception as e: print("  mod fail", mod.name, e)

def xapply(obj):
    """Apply location/rotation/scale on a data-API or prim object."""
    active(obj)
    bpy.ops.object.mode_set(mode='OBJECT')
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

def recalc(obj):
    """Force outward-facing normals on hand-built meshes."""
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

def loft(name, profile, length, col, m=None):
    """Extrude a Y-Z cross-section profile along X (±length/2)."""
    n = len(profile)
    verts = [(-length/2, y, z) for y, z in profile] + \
            [( length/2, y, z) for y, z in profile]
    faces = [tuple(range(n-1, -1, -1)), tuple(range(n, 2*n))]
    for i in range(n):
        j = (i+1) % n
        faces.append((i, j, n+j, n+i))
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces); me.update()
    o = bpy.data.objects.new(name, me)
    col.objects.link(o)
    recalc(o)
    if m is not None: assign(o, m)
    return o

def jitter(obj, amt=0.08):
    """Random vertex jitter for organic silhouettes (flat-shaded facets)."""
    for v in obj.data.vertices:
        v.co += Vector((random.uniform(-amt, amt),
                        random.uniform(-amt, amt),
                        random.uniform(-amt, amt)))

def blob(col, name, loc, scale, m, subdiv=2, jit=0.06):
    o = prim('ico', name, loc, scale=scale, subdivisions=subdiv, col=col, m=m)
    if jit: jitter(o, jit)
    smooth(o)
    return o

class Box:
    """Original model's world bbox in Blender space (Z up)."""
    def __init__(self, mn, mx):
        self.mn, self.mx = Vector(mn), Vector(mx)
        self.cx = (mn[0]+mx[0])/2; self.cy = (mn[1]+mx[1])/2
        self.w = mx[0]-mn[0]; self.d = mx[1]-mn[1]; self.h = mx[2]-mn[2]
        self.z0 = mn[2]

def measure(path):
    """Import existing gltf, record combined world bbox, wipe the scene."""
    try:
        bpy.ops.import_scene.gltf(filepath=path)
    except Exception as e:
        print("  import fail:", e); return None
    deps = bpy.context.evaluated_depsgraph_get()
    mn = Vector(( 1e9,)*3); mx = Vector((-1e9,)*3); found = False
    for o in list(bpy.data.objects):
        if o.type != 'MESH': continue
        oe = o.evaluated_get(deps)
        for c in oe.bound_box:
            w = oe.matrix_world @ Vector(c)
            mn = Vector(map(min, mn, w)); mx = Vector(map(max, mx, w))
            found = True
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    for me in list(bpy.data.meshes): bpy.data.meshes.remove(me)
    for im in list(bpy.data.images): bpy.data.images.remove(im)
    if not found: return None
    return Box(mn, mx)

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
    # drop stale texture payloads — flat-color materials need none
    tex_dir = os.path.join(out_dir, "textures")
    if os.path.isdir(tex_dir): shutil.rmtree(tex_dir, ignore_errors=True)
    n = sum(len(o.data.polygons) for o in bpy.data.objects if o.type=='MESH')
    print(f"[lowpoly] {asset_id}: ~{n} polys")

# ================================================================ builders
# Every builder receives the ORIGINAL model bbox (Box) and fills it.

def leaf_tree(col, b, hue=0):
    """Round-canopy stylized tree — the Subway Surfers silhouette."""
    trunkH = b.h * 0.42
    tr = prim('cone', "trunk", (b.cx, b.cy, b.z0 + trunkH/2),
              radius1=b.w*0.09, radius2=b.w*0.055, depth=trunkH,
              vertices=9, col=col, m=mat("trunk","trunk"))
    smooth(tr)
    can_lo = b.z0 + trunkH*0.85
    can_h = b.h*0.62
    greens = ["leafDark","leafA","leafB"]
    for i,(dx,dy,fz,fs) in enumerate([(0,0,0.45,1.0),(-0.22,0.1,0.28,0.62),
                                     (0.24,-0.12,0.32,0.55),(0.02,0.2,0.62,0.6)]):
        bb = blob(col, f"canopy{i}",
                  (b.cx+dx*b.w, b.cy+dy*b.d, can_lo+can_h*fz),
                  (b.w*0.42*fs, b.d*0.42*fs, can_h*0.5*fs),
                  mat(f"leaf{i}", greens[(i+hue)%3]), subdiv=2, jit=0.05)
    # tiny top crown
    blob(col, "crown", (b.cx, b.cy, b.z0+b.h*0.97),
         (b.w*0.2, b.d*0.2, b.h*0.12), mat("crown","leafWarm"), subdiv=1, jit=0.05)

def aloe_tree(col, b, var=0):
    """Quiver/aloe succulent — rosette spikes on a forked trunk."""
    trunkH = b.h*0.5
    tr = prim('cone', "trunk", (b.cx, b.cy, b.z0+trunkH/2),
              radius1=b.w*0.10, radius2=b.w*0.05, depth=trunkH,
              vertices=8, col=col, m=mat("aloeTrunk","deadwood"))
    smooth(tr)
    for i, dx in enumerate((-0.16, 0.16)):
        br = prim('cone', f"branch{i}", (b.cx+dx*b.w, b.cy, b.z0+trunkH*0.86),
                  rot=(0, math.radians(28)*(-1 if dx<0 else 1), 0),
                  radius1=b.w*0.05, radius2=b.w*0.025, depth=trunkH*0.55,
                  vertices=7, col=col, m=mat("aloeTrunk2","deadwood"))
        smooth(br)
    # rosettes: ring of tapered leaf cones
    tops = [(b.cx, b.cy, b.z0+b.h*0.62, 1.0),
            (b.cx-0.30*b.w, b.cy, b.z0+b.h*0.82, 0.66),
            (b.cx+0.30*b.w, b.cy, b.z0+b.h*0.82, 0.66)]
    for ri,(x,y,z,s) in enumerate(tops):
        n = 7
        for i in range(n):
            a = i*2*math.pi/n + var
            r = b.w*0.13*s
            lf = prim('cone', f"leaf{ri}_{i}", (x+math.cos(a)*r*0.55, y+math.sin(a)*r*0.55, z),
                      rot=(math.radians(38)*math.sin(a), -math.radians(38)*math.cos(a), 0),
                      radius1=r*0.30, radius2=0.008, depth=b.h*0.24*s,
                      vertices=5, col=col, m=mat("aloe","aloe" if i%2 else "aloeDark"))
            smooth(lf)
        tip = prim('cone', f"tip{ri}", (x,y,z+b.h*0.02),
                   radius1=r*0.5, radius2=0.01, depth=b.h*0.2*s,
                   vertices=6, col=col, m=mat("aloeTip","aloeTip"))
        smooth(tip)

def dead_trunk(col, b, lean=0):
    """Bare jagged trunk with a couple of branch stubs."""
    tr = prim('cone', "trunk", (b.cx, b.cy, b.z0+b.h*0.46),
              radius1=b.w*0.14, radius2=b.w*0.045, depth=b.h*0.92,
              vertices=8, col=col, m=mat("dead","deadwood"))
    # jag the top verts
    for v in tr.data.vertices:
        if v.co.z > b.h*0.35:
            v.co.z -= random.uniform(0, b.h*0.10)
            v.co.x += random.uniform(-0.03, 0.03)*b.w
    smooth(tr)
    for i,(dz, ang) in enumerate([(0.62, 42),(0.78,-55),(0.5,-30)]):
        br = prim('cone', f"stub{i}",
                  (b.cx+(-1)**i*b.w*0.10, b.cy+0.02*b.d, b.z0+b.h*dz),
                  rot=(math.radians(ang), math.radians(15*(-1)**i), 0),
                  radius1=b.w*0.045, radius2=0.01, depth=b.h*0.28,
                  vertices=6, col=col, m=mat("dead2","deadDark"))
        smooth(br)

def stump(col, b):
    st = prim('cone', "stump", (b.cx, b.cy, b.z0+b.h*0.45),
              radius1=b.w*0.30, radius2=b.w*0.24, depth=b.h*0.9,
              vertices=10, col=col, m=mat("bark","trunk"))
    smooth(st)
    top = prim('cyl', "top", (b.cx, b.cy, b.z0+b.h*0.91),
               radius=b.w*0.245, depth=b.h*0.04, vertices=10,
               col=col, m=mat("cut","woodCut"))
    for rr in (0.08, 0.16):
        ring = prim('torus', "ring", (b.cx, b.cy, b.z0+b.h*0.93),
                    major_radius=b.w*rr, minor_radius=b.w*0.012,
                    major_segments=12, minor_segments=4,
                    col=col, m=mat("ring","trunkDark"))
    for a in range(4):
        ang = a*math.pi/2 + 0.5
        rt = prim('cone', f"root{a}",
                  (b.cx+math.cos(ang)*b.w*0.28, b.cy+math.sin(ang)*b.d*0.28, b.z0+b.h*0.10),
                  rot=(math.radians(64)*math.sin(ang), -math.radians(64)*math.cos(ang), 0),
                  radius1=b.w*0.09, radius2=0.02, depth=b.h*0.35,
                  vertices=6, col=col, m=mat("bark2","trunk"))
        smooth(rt)

def roots(col, b):
    """Pine-root flare: shallow dome + radiating root toes."""
    blob(col, "mound", (b.cx, b.cy, b.z0+b.h*0.12),
         (b.w*0.34, b.d*0.34, b.h*0.14), mat("mound","trunk"), subdiv=1, jit=0.05)
    n = 7
    for i in range(n):
        a = i*2*math.pi/n
        rt = prim('cone', f"root{i}",
                  (b.cx+math.cos(a)*b.w*0.30, b.cy+math.sin(a)*b.d*0.30, b.z0+b.h*0.10),
                  rot=(math.radians(72)*math.sin(a), -math.radians(72)*math.cos(a), 0),
                  radius1=b.w*0.06, radius2=0.015, depth=b.w*0.42,
                  vertices=5, col=col, m=mat("root","trunkDark"))
        smooth(rt)

def boulder(col, b, key="rock", flat_shade=True):
    o = prim('ico', "boulder", (b.cx, b.cy, b.z0+b.h*0.45),
             subdivisions=2, radius=1.0, col=col, m=mat("r",key))
    o.scale = (b.w*0.5, b.d*0.5, b.h*0.5)
    bpy.ops.object.transform_apply(scale=True)
    jitter(o, min(b.w,b.d)*0.06)
    if flat_shade: flat(o)
    else: smooth(o)

def rock_cluster(col, b, mossy=True):
    """3-5 piled stones."""
    rocks = [(0,0,0.5,1.0),(-0.28,0.12,0.38,0.6),(0.26,-0.16,0.34,0.55),
             (0.1,0.3,0.3,0.45),(-0.12,-0.3,0.26,0.4)]
    for i,(dx,dy,fz,fs) in enumerate(rocks):
        o = prim('ico', f"r{i}", (b.cx+dx*b.w, b.cy+dy*b.d, b.z0+b.h*fz*fs + b.h*0.08),
                 subdivisions=1, radius=1.0, col=col,
                 m=mat(f"rc{i}","moss" if (mossy and i==3) else ("rock" if i%2==0 else "rockDark")))
        o.scale = (b.w*0.34*fs, b.d*0.34*fs, b.h*0.5*fs)
        bpy.ops.object.transform_apply(scale=True)
        jitter(o, 0.05*min(b.w,b.d)); flat(o)

def cliff_formation(col, b):
    """Layered strata stack for canyon rims (namaqualand_rocks / cliff tops)."""
    n = 4
    yoff = 0
    for i in range(n):
        fz0 = i/n
        h = b.h/n * 1.15
        w = b.w * (0.55 + 0.35*random.random())
        d = b.d * (0.55 + 0.3*random.random())
        ox = random.uniform(-0.12,0.12)*b.w
        oy = random.uniform(-0.12,0.12)*b.d
        key = ["cliff","cliffDark","sandstone","cliff"][i%4]
        o = prim('cube', f"strata{i}",
                 (b.cx+ox, b.cy+oy, b.z0 + fz0*b.h + h/2),
                 scale=(w/2, d/2, h/2), col=col, m=mat(f"st{i}",key))
        bevel(o, min(w,d)*0.12, 1)
        flat(o)

def moon_rock(col, b):
    boulder(col, b, "moonrock")
    # a couple of crater dimples
    for i,(a,fz) in enumerate([(0.3,0.6),(2.4,0.45),(4.2,0.7)]):
        c = prim('ico', f"crater{i}",
                 (b.cx+math.cos(a)*b.w*0.28, b.cy+math.sin(a)*b.d*0.28, b.z0+b.h*fz),
                 subdivisions=1, radius=min(b.w,b.d)*0.09, col=col,
                 m=mat("cd","rockDark"))
        flat(c)

def bush(col, b, kind="round"):
    if kind == "fern":
        # ring of arcing leaf blades
        n = 9
        for i in range(n):
            a = i*2*math.pi/n
            r = min(b.w,b.d)*0.20
            lf = prim('cone', f"f{i}",
                      (b.cx+math.cos(a)*r*0.6, b.cy+math.sin(a)*r*0.6, b.z0+b.h*0.4),
                      rot=(math.radians(35)*math.sin(a), -math.radians(35)*math.cos(a), 0),
                      radius1=r*0.42, radius2=0.01, depth=b.h*0.85,
                      vertices=5, col=col,
                      m=mat(f"fm{i}", "leafA" if i%2 else "leafDark"))
            smooth(lf)
        blob(col,"core",(b.cx,b.cy,b.z0+b.h*0.5),(b.w*0.18,b.d*0.18,b.h*0.2),
             mat("fc","leafB"), subdiv=1, jit=0.04)
    else:
        # puffy shrub — 3 blobs
        for i,(dx,dy,fz,fs,k) in enumerate([(0,0,0.5,1.0,"leafA"),(-0.2,0.1,0.4,0.7,"leafDark"),
                                            (0.22,-0.08,0.42,0.65,"leafB")]):
            blob(col, f"s{i}", (b.cx+dx*b.w, b.cy+dy*b.d, b.z0+b.h*fz),
                 (b.w*0.4*fs, b.d*0.4*fs, b.h*0.5*fs), mat(f"sh{i}",k), subdiv=2, jit=0.07)

def grass_tuft(col, b):
    n = 7
    for i in range(n):
        a = i*2*math.pi/n + 0.3
        r = min(b.w,b.d)*0.16
        h = b.h*random.uniform(0.55, 0.95)
        lf = prim('cone', f"g{i}",
                  (b.cx+math.cos(a)*r, b.cy+math.sin(a)*r, b.z0+h/2),
                  rot=(math.radians(random.uniform(6,20))*math.sin(a),
                       -math.radians(random.uniform(6,20))*math.cos(a), 0),
                  radius1=b.w*0.055, radius2=0.008, depth=h,
                  vertices=4, col=col,
                  m=mat(f"gr{i}", ["leafWarm","leafA","leafB"][i%3]))
        smooth(lf)

def hydrant(col, b):
    body = prim('cyl', "body", (b.cx, b.cy, b.z0+b.h*0.42),
                radius=b.w*0.20, depth=b.h*0.78, vertices=10,
                col=col, m=mat("hred","hydrant"))
    smooth(body); bevel(body, b.w*0.02, 1)
    dome = prim('sphere', "dome", (b.cx, b.cy, b.z0+b.h*0.83),
                radius=b.w*0.20, scale=(1,1,0.7), segments=10, ring_count=6,
                col=col, m=mat("hred2","hydrant"))
    smooth(dome)
    prim('cyl', "topnut", (b.cx, b.cy, b.z0+b.h*0.97), radius=b.w*0.06,
         depth=b.h*0.08, vertices=6, col=col, m=mat("hcap","hydrantCap"))
    for a in (0, math.pi):
        cap = prim('cyl', "sidecap", (b.cx+math.cos(a)*b.w*0.24, b.cy+math.sin(a)*b.w*0.24, b.z0+b.h*0.55),
                   rot=(math.pi/2,0,a), radius=b.w*0.09, depth=b.w*0.12,
                   vertices=8, col=col, m=mat("hcap2","hydrantCap"))
        smooth(cap)
    # front cap
    fc = prim('cyl', "front", (b.cx, b.cy-b.d*0.24, b.z0+b.h*0.45),
              rot=(math.pi/2,0,0), radius=b.w*0.10, depth=b.d*0.12,
              vertices=8, col=col, m=mat("hcap3","hydrantCap"))
    smooth(fc)
    prim('cyl', "base", (b.cx, b.cy, b.z0+b.h*0.05), radius=b.w*0.26,
         depth=b.h*0.10, vertices=10, col=col, m=mat("hred3","hydrant"))

def trash_can(col, b):
    body = prim('cyl', "can", (b.cx, b.cy, b.z0+b.h*0.45),
                radius=b.w*0.30, depth=b.h*0.82, vertices=12,
                col=col, m=mat("tc","trashcan"))
    smooth(body)
    for fz in (0.18, 0.45, 0.72):
        rib = prim('torus', "rib", (b.cx, b.cy, b.z0+b.h*fz),
                   major_radius=b.w*0.305, minor_radius=b.w*0.025,
                   major_segments=12, minor_segments=4,
                   col=col, m=mat("tcd","trashcanDark"))
    lid = prim('cyl', "lid", (b.cx, b.cy, b.z0+b.h*0.92),
               radius=b.w*0.33, depth=b.h*0.07, vertices=12,
               col=col, m=mat("tcl","trashcanDark"))
    smooth(lid)
    prim('cyl', "knob", (b.cx, b.cy, b.z0+b.h*0.98), radius=b.w*0.08,
         depth=b.h*0.06, vertices=8, col=col, m=mat("tck","trashcanDark"))

def trash_bag(col, b):
    bag = prim('ico', "bag", (b.cx, b.cy, b.z0+b.h*0.40),
               subdivisions=2, radius=1.0, col=col, m=mat("tb","trashbag",rough=0.6))
    bag.scale = (b.w*0.42, b.d*0.42, b.h*0.42)
    bpy.ops.object.transform_apply(scale=True)
    jitter(bag, 0.05); smooth(bag)
    knot = prim('cone', "knot", (b.cx, b.cy, b.z0+b.h*0.82),
                radius1=b.w*0.09, radius2=0.02, depth=b.h*0.2,
                vertices=6, col=col, m=mat("tk","bagKnot"))
    smooth(knot)

def street_lamp(col, b, variant=0):
    pole = prim('cyl', "pole", (b.cx, b.cy, b.z0+b.h*0.45),
                radius=b.w*0.045, depth=b.h*0.9, vertices=8,
                col=col, m=mat("lp","lampPost",metal=0.3))
    smooth(pole)
    prim('cyl', "base", (b.cx, b.cy, b.z0+b.h*0.04), radius=b.w*0.10,
         depth=b.h*0.08, vertices=8, col=col, m=mat("lpb","lampPost",metal=0.3))
    # arm + head
    arm = prim('cyl', "arm", (b.cx+b.w*0.16, b.cy, b.z0+b.h*0.90),
               rot=(0,math.pi/2,0), radius=b.w*0.035, depth=b.w*0.4,
               vertices=7, col=col, m=mat("lpa","lampPost",metal=0.3))
    smooth(arm)
    head = prim('cone', "head", (b.cx+b.w*0.34, b.cy, b.z0+b.h*0.86),
                radius1=b.w*0.12, radius2=b.w*0.16, depth=b.h*0.10,
                vertices=8, col=col, m=mat("lh","lampPost",metal=0.3))
    smooth(head)
    bulb = prim('sphere', "bulb", (b.cx+b.w*0.34, b.cy, b.z0+b.h*0.82),
                radius=b.w*0.09, scale=(1,1,0.6), segments=8, ring_count=5,
                col=col, m=mat("lb","lampGlow",emit=2.0))
    smooth(bulb)

def bench(col, b):
    # seat slats
    for i in range(3):
        s = prim('cube', f"slat{i}", (b.cx, b.cy-b.d*0.30+i*b.d*0.30, b.z0+b.h*0.48),
                 scale=(b.w*0.46, b.d*0.11, b.h*0.05), col=col,
                 m=mat("bslat","benchWood"))
        bevel(s, min(b.d*0.04,0.02), 1)
    # back slats
    for i in range(2):
        s = prim('cube', f"back{i}", (b.cx, b.cy+b.d*0.40, b.z0+b.h*(0.66+i*0.2)),
                 rot=(math.radians(-12),0,0),
                 scale=(b.w*0.46, b.d*0.06, b.h*0.09), col=col,
                 m=mat("bback","benchWood"))
        bevel(s, 0.015, 1)
    # legs (cast-iron sides)
    for sx in (-1, 1):
        leg = prim('cube', "leg", (b.cx+sx*b.w*0.40, b.cy, b.z0+b.h*0.24),
                   scale=(b.w*0.05, b.d*0.42, b.h*0.48), col=col,
                   m=mat("bleg","benchLeg",metal=0.3))
        bevel(leg, 0.02, 1)
        bk = prim('cube', "legback", (b.cx+sx*b.w*0.40, b.cy+b.d*0.36, b.z0+b.h*0.72),
                  rot=(math.radians(-12),0,0),
                  scale=(b.w*0.05, b.d*0.06, b.h*0.5), col=col,
                  m=mat("bleg2","benchLeg",metal=0.3))
        bevel(bk, 0.015, 1)

def street_seating(col, b):
    """Modular plaza seat — chunky stone blocks."""
    base = prim('cube', "base", (b.cx, b.cy, b.z0+b.h*0.35),
                scale=(b.w*0.44, b.d*0.40, b.h*0.5), col=col,
                m=mat("ss","concrete"))
    bevel(base, 0.04, 1)
    top = prim('cube', "top", (b.cx, b.cy, b.z0+b.h*0.63),
               scale=(b.w*0.48, b.d*0.44, b.h*0.07), col=col,
               m=mat("sst","concreteDark"))
    bevel(top, 0.03, 1)

def planter(col, b, style=0):
    box = prim('cube', "box", (b.cx, b.cy, b.z0+b.h*0.30),
               scale=(b.w*0.45, b.d*0.42, b.h*0.32), col=col,
               m=mat("pb","planter"))
    bevel(box, 0.03, 1)
    rim = prim('cube', "rim", (b.cx, b.cy, b.z0+b.h*0.60),
               scale=(b.w*0.48, b.d*0.45, b.h*0.05), col=col,
               m=mat("pbr","planter"))
    bevel(rim, 0.02, 1)
    soil = prim('cube', "soil", (b.cx, b.cy, b.z0+b.h*0.585),
                scale=(b.w*0.40, b.d*0.37, b.h*0.04), col=col,
                m=mat("soil","soil"))
    # little green tuft
    for i,(dx,dy) in enumerate([(-0.15,0.05),(0.12,-0.1),(0.02,0.15)]):
        blob(col, f"tp{i}", (b.cx+dx*b.w, b.cy+dy*b.d, b.z0+b.h*0.72),
             (b.w*0.16, b.d*0.16, b.h*0.18), mat(f"pt{i}","leafA" if i%2 else "leafB"),
             subdiv=1, jit=0.05)

def covered_car(col, b):
    """Car under a tarp — soft rounded silhouette, tarp-blue."""
    body = prim('cube', "body", (b.cx, b.cy, b.z0+b.h*0.45),
                scale=(b.w*0.44, b.d*0.44, b.h*0.42), col=col,
                m=mat("tarp","tarp"))
    bevel(body, min(b.w,b.d)*0.16, 2)
    smooth(body)
    top = prim('cube', "cabin", (b.cx-b.w*0.04, b.cy, b.z0+b.h*0.72),
               scale=(b.w*0.26, b.d*0.38, b.h*0.22), col=col,
               m=mat("tarp2","tarp"))
    bevel(top, min(b.w,b.d)*0.14, 2)
    smooth(top)
    # wheel bulges under tarp
    for dx in (-0.26, 0.26):
        for dy in (-0.34, 0.34):
            w = prim('cyl', "wheelb", (b.cx+dx*b.w, b.cy+dy*b.d, b.z0+b.h*0.14),
                     rot=(math.pi/2,0,0), radius=b.h*0.14, depth=b.d*0.16,
                     vertices=10, col=col, m=mat("tw","tarp"))
            smooth(w)

def crate(col, b, mil=False):
    body_key, edge_key = ("milCrate","milEdge") if mil else ("crateWood","crateEdge")
    body = prim('cube', "crate", (b.cx, b.cy, b.z0+b.h*0.5),
                scale=(b.w*0.46, b.d*0.46, b.h*0.48), col=col,
                m=mat("cr",body_key))
    bevel(body, 0.03, 1)
    # edge beams
    for sx in (-1,1):
        for sy in (-1,1):
            e = prim('cube', "edge", (b.cx+sx*b.w*0.44, b.cy+sy*b.d*0.44, b.z0+b.h*0.5),
                     scale=(b.w*0.05, b.d*0.05, b.h*0.48), col=col,
                     m=mat("cre",edge_key))
    for sz in (0.06, 0.94):
        for sx in (-1,1):
            e = prim('cube', "bedge", (b.cx+sx*b.w*0.44, b.cy, b.z0+b.h*sz),
                     scale=(b.w*0.05, b.d*0.44, b.h*0.055), col=col,
                     m=mat("cre2",edge_key))
        for sy in (-1,1):
            e = prim('cube', "bedge2", (b.cx, b.cy+sy*b.d*0.44, b.z0+b.h*sz),
                     scale=(b.w*0.44, b.d*0.05, b.h*0.055), col=col,
                     m=mat("cre3",edge_key))
    # diagonal plank on two sides for style
    for sy in (-1,1):
        d = prim('cube', "diag", (b.cx, b.cy+sy*b.d*0.465, b.z0+b.h*0.5),
                 rot=(math.radians(40),0,0),
                 scale=(b.w*0.40, b.d*0.02, b.h*0.09), col=col,
                 m=mat("crd",edge_key))

def road_barrier(col, b):
    """Concrete jersey barrier — bright concrete + orange stripe."""
    prof = [(-.30,0),(.30,0),(.30,.10),(.22,.22),(.14,.62),(.12,1.0),
            (-.12,1.0),(-.14,.62),(-.22,.22),(-.30,.10)]
    o = loft("barrier", prof, 1.0, col, m=mat("cb","concrete"))
    o.location = (b.cx, b.cy, b.z0)
    o.scale = (b.w, b.d, b.h)
    xapply(o)
    bevel(o, 0.02, 1); flat(o)
    # warning stripe on each sloped face (just proud of the concrete)
    for s in (-1, 1):
        st = prim('cube', "stripe", (b.cx, b.cy+s*b.d*0.155, b.z0+b.h*0.60),
                  rot=(s*math.radians(10),0,0),
                  scale=(b.w*0.40, b.d*0.02, b.h*0.09), col=col,
                  m=mat("cbs","stripe"))
        bevel(st, 0.01, 1)

def barrel(col, b):
    # lathed barrel profile
    prof = []
    seg = 9
    for i in range(seg+1):
        t = i/seg
        z = t*2-1   # -1..1
        r = 0.42 + 0.10*(1-z*z)          # bulge
        prof.append((z, r))
    # spin it
    vs = 10
    verts=[]; faces=[]
    import math as _m
    for i in range(vs):
        a = i*2*_m.pi/vs
        for z,r in prof:
            verts.append((_m.cos(a)*r, _m.sin(a)*r, z))
    npr = len(prof)
    for i in range(vs):
        ni=(i+1)%vs
        for j in range(npr-1):
            a=i*npr+j; b2=ni*npr+j
            faces.append((a,b2,b2+1,a+1))
    # caps
    verts.append((0,0,-1)); ci=len(verts)-1
    verts.append((0,0,1)); ti=len(verts)-1
    for i in range(vs):
        ni=(i+1)%vs
        faces.append((i*npr, ni*npr, ci))
        faces.append((i*npr+npr-1, ti, ni*npr+npr-1))
    me=bpy.data.meshes.new("barrel"); me.from_pydata(verts,[],faces); me.update()
    o=bpy.data.objects.new("barrel",me); col.objects.link(o)
    recalc(o)
    o.location=(b.cx,b.cy,b.z0+b.h*0.5)
    o.scale=(b.w*0.5,b.d*0.5,b.h*0.5); xapply(o)
    assign(o, mat("brl","barrel")); smooth(o)
    for fz in (0.22,0.5,0.78):
        hoop = prim('torus',"hoop",(b.cx,b.cy,b.z0+b.h*fz),
                    major_radius=b.w*(0.42+0.10*(1-(fz*2-1)**2))*0.98,
                    minor_radius=b.w*0.02, major_segments=12, minor_segments=4,
                    col=col, m=mat("bhp","barrelHoop",metal=0.4))

def lantern(col, b):
    # wooden frame box + glowing core
    fr = prim('cube', "frame", (b.cx, b.cy, b.z0+b.h*0.52),
              scale=(b.w*0.30, b.d*0.30, b.h*0.42), col=col,
              m=mat("lf","lantern"))
    bevel(fr, 0.025, 1)
    core = prim('cube', "glow", (b.cx, b.cy, b.z0+b.h*0.52),
                scale=(b.w*0.22, b.d*0.22, b.h*0.34), col=col,
                m=mat("lg","lanternGlow",emit=3.0))
    cap = prim('cone', "cap", (b.cx, b.cy, b.z0+b.h*0.92),
               radius1=b.w*0.30, radius2=b.w*0.10, depth=b.h*0.16,
               vertices=4, col=col, m=mat("lc","lantern"))
    flat(cap)
    prim('torus', "ring", (b.cx, b.cy, b.z0+b.h*0.99),
         major_radius=b.w*0.07, minor_radius=b.w*0.02,
         major_segments=8, minor_segments=4, col=col, m=mat("lr","lantern"))

def statue(col, b, horse=False):
    plinth = prim('cube', "plinth", (b.cx, b.cy, b.z0+b.h*0.10),
                  scale=(b.w*0.42, b.d*0.42, b.h*0.10), col=col,
                  m=mat("spl","statueDark"))
    bevel(plinth, 0.03, 1)
    if horse:
        # stylized rearing horse: body blob, neck, head, legs, gold trim
        body = blob(col, "hbody", (b.cx, b.cy, b.z0+b.h*0.62),
                    (b.w*0.20, b.d*0.34, b.h*0.20), mat("hs","statue"), subdiv=2, jit=0.03)
        neck = prim('cone', "neck", (b.cx, b.cy-b.d*0.24, b.z0+b.h*0.78),
                    rot=(math.radians(-30),0,0),
                    radius1=b.w*0.10, radius2=b.w*0.06, depth=b.h*0.30,
                    vertices=8, col=col, m=mat("hn","statue"))
        smooth(neck)
        head = prim('cube', "head", (b.cx, b.cy-b.d*0.33, b.z0+b.h*0.92),
                    rot=(math.radians(-20),0,0),
                    scale=(b.w*0.09, b.d*0.16, b.h*0.08), col=col,
                    m=mat("hh","statue"))
        bevel(head, 0.02, 1)
        for dx in (-0.10, 0.10):
            for i,dy in enumerate((-0.20, 0.18)):
                leg = prim('cyl', f"leg{dx}{i}",
                           (b.cx+dx*b.w, b.cy+dy*b.d, b.z0+b.h*0.36),
                           radius=b.w*0.05, depth=b.h*0.36,
                           vertices=7, col=col, m=mat("hl","statue"))
                smooth(leg)
        tail = prim('cone', "tail", (b.cx, b.cy+b.d*0.32, b.z0+b.h*0.55),
                    rot=(math.radians(140),0,0),
                    radius1=b.w*0.07, radius2=0.02, depth=b.h*0.30,
                    vertices=7, col=col, m=mat("ht","statueDark"))
        smooth(tail)
        cres = prim('cube', "crest", (b.cx, b.cy, b.z0+b.h*0.16),
                    scale=(b.w*0.30, b.d*0.30, b.h*0.05), col=col,
                    m=mat("hc","gold",metal=0.5,rough=0.4))
        bevel(cres, 0.015, 1)
    else:
        # gothic robed figure: tapered body, hood, arms
        robe = prim('cone', "robe", (b.cx, b.cy, b.z0+b.h*0.52),
                    radius1=b.w*0.30, radius2=b.w*0.16, depth=b.h*0.66,
                    vertices=9, col=col, m=mat("gr","statue"))
        smooth(robe)
        hood = prim('cone', "hood", (b.cx, b.cy, b.z0+b.h*0.88),
                    radius1=b.w*0.16, radius2=b.w*0.05, depth=b.h*0.22,
                    vertices=8, col=col, m=mat("gh","statueDark"))
        smooth(hood)
        face = prim('sphere', "face", (b.cx, b.cy-b.d*0.09, b.z0+b.h*0.83),
                    radius=b.w*0.09, scale=(1,0.7,1.1), segments=8, ring_count=6,
                    col=col, m=mat("gf","statueDark"))
        smooth(face)
        for sx in (-1,1):
            arm = prim('cyl', f"arm{sx}", (b.cx+sx*b.w*0.20, b.cy-b.d*0.05, b.z0+b.h*0.60),
                       rot=(math.radians(70), sx*math.radians(20), 0),
                       radius=b.w*0.055, depth=b.h*0.26,
                       vertices=7, col=col, m=mat("ga","statue"))
            smooth(arm)

def jersey(col, b):
    prof = [(-.28,0),(.28,0),(.28,.10),(.20,.24),(.13,.66),(.12,1.0),
            (-.12,1.0),(-.13,.66),(-.20,.24),(-.28,.10)]
    o = loft("jersey", prof, 1.0, col, m=mat("jb","wornWhite"))
    o.location = (b.cx, b.cy, b.z0)
    o.scale = (b.w, b.d, b.h)
    xapply(o)
    bevel(o, 0.02, 1); flat(o)
    # orange chevron stripe blocks, proud of both faces
    for i,dx in enumerate((-0.3, 0.0, 0.3)):
        st = prim('cube', f"chev{i}", (b.cx+dx*b.w, b.cy, b.z0+b.h*0.55),
                  rot=(0,math.radians(28),0),
                  scale=(b.w*0.10, b.d*0.20, b.h*0.20), col=col,
                  m=mat(f"jc{i}","stripe",emit=0.4))
        bevel(st, 0.012, 1)

def police(col, b):
    """Crowd-control barrier: orange/white board + steel tube frame."""
    L, H = b.w, b.h
    steelm = mat("steel","steel",metal=0.5,rough=0.4)
    for x in (-L*0.44, L*0.44):
        t = prim('cyl', "upright", (b.cx+x, b.cy, b.z0+H*0.5),
                 radius=min(b.d*0.06,0.03), depth=H*0.94, vertices=8,
                 col=col, m=steelm); smooth(t)
    for z in (H*0.92, H*0.60):
        t = prim('cyl', "rail", (b.cx, b.cy, b.z0+z), rot=(0,math.pi/2,0),
                 radius=min(b.d*0.05,0.026), depth=L*0.92, vertices=8,
                 col=col, m=steelm); smooth(t)
    bd = prim('cube', "board", (b.cx, b.cy, b.z0+H*0.76),
              scale=(L*0.44, b.d*0.16, H*0.16), col=col, m=mat("bw","wornWhite"))
    bevel(bd, 0.015, 1)
    for i,dx in enumerate((-0.28,0,0.28)):
        for s in (-1, 1):
            st = prim('cube', f"stripe{i}{s}", (b.cx+dx*L, b.cy+s*b.d*0.165, b.z0+H*0.76),
                      rot=(0,math.radians(-30),0),
                      scale=(L*0.09, b.d*0.02, H*0.17), col=col,
                      m=mat(f"ps{i}s{s}","stripe",emit=0.4))
    for x in (-L*0.44, L*0.44):
        f = prim('cube', "foot", (b.cx+x, b.cy, b.z0+0.03),
                 scale=(L*0.05, b.d*0.30, 0.035), col=col, m=steelm)
        bevel(f, 0.01, 1)

def fallen_log(col, b):
    L = max(b.w, b.d)   # log lies along its long axis
    axis_y = b.d >= b.w
    rot = (math.pi/2,0,0) if axis_y else (0,math.pi/2,0)
    R = min(b.w,b.d)*0.5*0.9
    log = prim('cone', "log",
               (b.cx, b.cy, b.z0+R+0.02),
               rot=rot, radius1=R, radius2=R*0.82, depth=L*0.96,
               vertices=12, col=col, m=mat("lg","trunk"))
    smooth(log)
    # cut faces + ring accents
    off = L*0.48
    for s,r in ((-off, R), (off, R*0.82)):
        c = prim('cyl', "cut",
                 (b.cx, b.cy+s, b.z0+R+0.02) if axis_y else (b.cx+s, b.cy, b.z0+R+0.02),
                 rot=rot, radius=r*0.95, depth=0.02, vertices=12,
                 col=col, m=mat("lc","woodCut"))
    # branch stubs
    for i,(t, ang) in enumerate([(-0.2, 40), (0.15, -50)]):
        p = t*L
        st = prim('cone', f"stub{i}",
                  (b.cx, b.cy+p, b.z0+R*1.6) if axis_y else (b.cx+p, b.cy, b.z0+R*1.6),
                  rot=(math.radians(ang),0,0),
                  radius1=R*0.16, radius2=0.02, depth=R*0.8,
                  vertices=6, col=col, m=mat("lst","trunkDark"))
        smooth(st)

def sandstone_block(col, b):
    o = prim('cube', "block", (b.cx, b.cy, b.z0+b.h*0.5),
             scale=(b.w*0.48, b.d*0.48, b.h*0.48), col=col,
             m=mat("sb","sandstone"))
    bevel(o, 0.05, 2); flat(o)
    # carved seam + cap lip
    g = prim('cube', "groove", (b.cx, b.cy, b.z0+b.h*0.80),
             scale=(b.w*0.49, b.d*0.49, b.h*0.03), col=col,
             m=mat("sg","sandDark"))
    cap = prim('cube', "cap", (b.cx, b.cy, b.z0+b.h*0.95),
               scale=(b.w*0.44, b.d*0.44, b.h*0.05), col=col,
               m=mat("sc","sandLight"))
    bevel(cap, 0.02, 1)

def spikes(col, b):
    base = prim('cube', "base", (b.cx, b.cy, b.z0+b.h*0.06),
                scale=(b.w*0.48, b.d*0.46, b.h*0.06), col=col,
                m=mat("sp","plate",metal=0.4,rough=0.5))
    bevel(base, 0.015, 1)
    rows = [(-0.25, (-0.33,0,0.33)), (0.25, (-0.2,0.2))]
    for ry, xs in rows:
        for x in xs:
            h = b.h*random.uniform(0.6,0.88)
            s = prim('cone', "spike",
                     (b.cx+x*b.w, b.cy+ry*b.d, b.z0+b.h*0.12+h/2),
                     rot=(math.radians(random.uniform(-7,7)),
                          math.radians(random.uniform(-7,7)), 0),
                     radius1=b.w*0.07, radius2=0.008, depth=h,
                     vertices=6, col=col, m=mat("ss2","spike",metal=0.6,rough=0.35))
            smooth(s)

def traffic_cone(col, b):
    base = prim('cube', "base", (b.cx, b.cy, b.z0+b.h*0.03),
                scale=(b.w*0.42, b.d*0.42, b.h*0.04), col=col,
                m=mat("cb2","tire"))
    bevel(base, 0.015, 1)
    body = prim('cone', "body", (b.cx, b.cy, b.z0+b.h*0.45),
                radius1=b.w*0.30, radius2=b.w*0.08, depth=b.h*0.82,
                vertices=10, col=col, m=mat("co","stripe",emit=0.3))
    smooth(body)
    band = prim('cone', "band", (b.cx, b.cy, b.z0+b.h*0.55),
                radius1=b.w*0.185, radius2=b.w*0.145, depth=b.h*0.18,
                vertices=10, col=col, m=mat("cw","wornWhite",emit=0.5))
    smooth(band)
    prim('cyl', "tip", (b.cx, b.cy, b.z0+b.h*0.92), radius=b.w*0.075,
         depth=b.h*0.08, vertices=8, col=col, m=mat("ct","stripe"))

def dumpster(col, b):
    prof = [(-0.42,0.12),(0.42,0.12),(0.5,1.0),(-0.5,1.0)]
    body = loft("body", prof, 1.0, col, m=mat("db","dumpster"))
    body.scale = (b.w, b.d, b.h*0.78)
    body.location = (b.cx, b.cy, b.z0 + b.h*0.06)
    xapply(body)
    bevel(body, 0.04, 1)
    # lids
    for s in (-1,1):
        lid = prim('cube', "lid", (b.cx, b.cy+s*b.d*0.21, b.z0+b.h*0.86),
                   rot=(math.radians(4*s),0,0),
                   scale=(b.w*0.46, b.d*0.20, b.h*0.05), col=col,
                   m=mat("dl","dumpsterLid"))
        bevel(lid, 0.02, 1)
    # side ribs
    for x in (-0.3, 0.0, 0.3):
        for s in (-1,1):
            r = prim('cube', "rib", (b.cx+x*b.w, b.cy+s*b.d*0.44, b.z0+b.h*0.48),
                     scale=(b.w*0.04, b.d*0.02, b.h*0.32), col=col,
                     m=mat("dr","dumpsterLid"))
    # wheels
    for x in (-0.36, 0.36):
        for y in (-0.3, 0.3):
            w = prim('cyl', "wheel", (b.cx+x*b.w, b.cy+y*b.d, b.z0+b.h*0.07),
                     rot=(math.pi/2,0,0), radius=b.h*0.07, depth=b.d*0.05,
                     vertices=8, col=col, m=mat("dw","tire"))
            smooth(w)

def pillar_broken(col, b):
    plinth = prim('cube', "plinth", (b.cx, b.cy, b.z0+b.h*0.07),
                  scale=(b.w*0.46, b.d*0.46, b.h*0.07), col=col,
                  m=mat("pp","pillar"))
    bevel(plinth, 0.03, 1)
    shaft = prim('cyl', "shaft", (b.cx, b.cy, b.z0+b.h*0.52),
                 radius=b.w*0.30, depth=b.h*0.78, vertices=12,
                 col=col, m=mat("ps","pillar"))
    # jagged fracture
    for v in shaft.data.vertices:
        if v.co.z > 0.2:
            v.co.z -= random.uniform(0, 0.35)*(v.co.z-0.2)
            v.co.x += random.uniform(-0.03,0.03)*b.w
            v.co.y += random.uniform(-0.03,0.03)*b.d
    smooth(shaft); bevel(shaft, 0.02, 1)
    cap = prim('torus', "capring", (b.cx, b.cy, b.z0+b.h*0.34),
               major_radius=b.w*0.31, minor_radius=b.w*0.04,
               major_segments=12, minor_segments=5, col=col,
               m=mat("pc","pillarShade"))

# ---------------------------------------------------------------- registry

BUILDERS = {
    # trees & foliage
    "island_tree_02":      lambda c,b: leaf_tree(c,b),
    "quiver_tree_01":      lambda c,b: aloe_tree(c,b,0.0),
    "quiver_tree_02":      lambda c,b: aloe_tree(c,b,1.7),
    "dead_tree_trunk":     lambda c,b: dead_trunk(c,b),
    "dead_tree_trunk_02":  lambda c,b: dead_trunk(c,b,1),
    "tree_stump_01":       stump,
    "tree_stump_02":       stump,
    "bl_stump":            stump,
    "pine_roots":          roots,
    "fern_02":             lambda c,b: bush(c,b,"fern"),
    "wild_rooibos_bush":   lambda c,b: bush(c,b,"shrub"),
    "grass_medium_01":     grass_tuft,
    "grass_medium_02":     grass_tuft,
    # rocks
    "boulder_01":          lambda c,b: boulder(c,b,"rock"),
    "namaqualand_boulder_02": lambda c,b: boulder(c,b,"sandstone"),
    "namaqualand_boulder_04": cliff_formation,
    "namaqualand_rocks_01":   lambda c,b: rock_cluster(c,b,False),
    "rock_moss_set_01":       lambda c,b: rock_cluster(c,b,True),
    "moon_rock_01":        moon_rock,
    "bl_boulder":          lambda c,b: boulder(c,b,"rock"),
    # city
    "fire_hydrant":        hydrant,
    "metal_trash_can":     trash_can,
    "trashbag":            trash_bag,
    "street_lamp_01":      lambda c,b: street_lamp(c,b,0),
    "street_lamp_02":      lambda c,b: street_lamp(c,b,1),
    "painted_wooden_bench": bench,
    "modular_street_seating": street_seating,
    "planter_box_01":      lambda c,b: planter(c,b,0),
    "planter_box_02":      lambda c,b: planter(c,b,1),
    "planter_box_03":      lambda c,b: planter(c,b,2),
    "covered_car":         covered_car,
    "wooden_crate_01":     lambda c,b: crate(c,b,False),
    "wooden_crate_02":     lambda c,b: crate(c,b,False),
    "old_military_crate":  lambda c,b: crate(c,b,True),
    "concrete_road_barrier":    road_barrier,
    "concrete_road_barrier_02": road_barrier,
    # temple / vault
    "wine_barrel_01":      barrel,
    "wooden_lantern_01":   lantern,
    "gothic_statue":       lambda c,b: statue(c,b,False),
    "horse_statue_01":     lambda c,b: statue(c,b,True),
    # bl_* obstacles
    "bl_jersey_barrier":   jersey,
    "bl_police_barrier":   police,
    "bl_fallen_log":       fallen_log,
    "bl_sandstone_block":  sandstone_block,
    "bl_spikes":           spikes,
    "bl_traffic_cone":     traffic_cone,
    "bl_dumpster":         dumpster,
    "bl_pillar_broken":    pillar_broken,
}

# ---------------------------------------------------------------- main

only = sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else []
for aid, fn in BUILDERS.items():
    if only and aid not in only: continue
    gltf = os.path.join(MODELS, aid, aid + ".gltf")
    try:
        col = reset()
        box = measure(gltf) if os.path.exists(gltf) else None
        if box is None:
            box = Box((-0.5,-0.5,0),(0.5,0.5,1.0))
            print(f"[lowpoly] {aid}: no source, unit box")
        fn(col, box)
        export(col, aid)
    except Exception as e:
        print(f"[lowpoly] FAILED {aid}: {e}")
        import traceback; traceback.print_exc()

print("[lowpoly] ALL DONE")
