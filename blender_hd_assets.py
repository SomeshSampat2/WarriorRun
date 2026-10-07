#!/usr/bin/env python3
"""
WarriorRun — HD stylized asset pack (biome overhaul).

Generates a full set of higher-fidelity stylized props into
Art/Realistic/Models/hd_*/ (gltf + bin). Same import convention as
blender_lowpoly_assets.py: GLTF_SEPARATE, +Y up, pivot at base centre,
flat-colour Principled materials (emission where a part glows).

Design language: chunky stylized — readable silhouettes first, then real
secondary structure (tiered canopies, strata bands, window grids, ribs,
rims, roots) and accent colours (flowers, glow cracks, snow blankets).
~0.5–3k tris per prop — mobile-safe at the tile decor counts the game uses.

Run:  Blender --background --python blender_hd_assets.py [-- hd_oak hd_pine]
"""
import bpy, bmesh, os, math, random, shutil, sys
from mathutils import Vector

random.seed(20261007)

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                    "Assets", "WarriorRun", "Art", "Realistic")
MODELS = os.path.join(ROOT, "Models")

# ---------------------------------------------------------------- palette
def C(hexstr):
    h = hexstr.lstrip('#')
    r, g, b = (int(h[i:i+2], 16) / 255.0 for i in (0, 2, 4))
    return (r ** 2.2, g ** 2.2, b ** 2.2, 1.0)

PAL = {
    # forest greens — warm sunny ramp
    "leafDeep":  "#2E6B34", "leafDark":  "#3E8948", "leafA":     "#57B845",
    "leafB":     "#79D356", "leafLight": "#9FE06A", "leafWarm":  "#BCD84E",
    "birchLeaf": "#C4DE5A", "maple":     "#E8873A", "mapleDeep": "#C95F2B",
    "blossom":   "#F29AC2", "blossomDeep":"#DE6FA5",
    "trunk":     "#8A5A32", "trunkDark": "#5E3C22", "trunkLight":"#A9743F",
    "birchBark": "#EAE4D4", "birchMark": "#4A4238", "woodCut":   "#E8C088",
    "moss":      "#7BA05B", "deadwood":  "#9A7B56", "deadDark":  "#6E5540",
    "flowerR":   "#F2607A", "flowerY":   "#FFC93C", "flowerW":   "#FFF4E0",
    "flowerP":   "#B47CFF", "shroomCap": "#E8402F", "shroomDot": "#FFF4E0",
    "shroomStem":"#F0E0C0",
    # rocks / stone
    "rock":      "#A89E92", "rockDark":  "#847B70", "rockLight": "#C8BDAE",
    "mossRock":  "#8B9664", "stoneSand": "#DCC9A8", "stoneShade":"#B7A077",
    "marble":    "#E8E0CC", "goldTrim":  "#F0B93E", "bronze":    "#8A6B45",
    "emberGlow": "#FF9A3C",
    # desert
    "sandA":     "#E0B56A", "sandB":     "#C98A5A", "sandC":     "#C07048",
    "sandDark":  "#A85A38", "sandLight": "#F0CD8A", "boneWhite": "#EFE6D0",
    "cactus":    "#4E9E5B", "cactusDark":"#3C7E4A", "cactusBloom":"#F2789F",
    "dryBrush":  "#B08B4F",
    # volcano
    "basalt":    "#3B3334", "basalt2":   "#524648", "obsidian":  "#2B2130",
    "charred":   "#241E1E", "lavaHot":   "#FFC23A", "lava":      "#FF7A26",
    # frost
    "snow":      "#F2F8FC", "snowShade": "#D6E4EE", "ice":       "#9FD4EC",
    "iceDeep":   "#5FB4DE", "pineCold":  "#3E7D4E", "pineDark":  "#2E5E3C",
    "frostRock": "#6B7684",
    # lagoon
    "palmTrunk": "#A9743F", "palmRings": "#8A5A32", "frond":     "#3FA060",
    "frondDark": "#2E7D4C", "frondLight":"#5FC278", "coconut":   "#6E4A2E",
    "sand":      "#F0DCB0", "beachRock": "#9A948C", "drift":     "#C8B89A",
    "coral":     "#F08C8C", "coralDeep": "#DE6E7A", "shell":     "#F5E0C8",
    "starfish":  "#F08A4B",
    # city
    "brick":     "#B55B44", "brickDark": "#9A4A38", "tanBldg":   "#D9B98A",
    "shopTeal":  "#3E8E8A", "awningR":   "#E0574A", "awningW":   "#F5EFE0",
    "glassBlue": "#7FB3C9", "glassLit":  "#B8E0EC", "concrete":  "#C9C4BB",
    "concreteD": "#A39E94", "steelDark": "#37424E", "asphalt":   "#4A4F5C",
    "lampGlow":  "#FFE9A8", "hydrant":   "#E8402F", "benchWood": "#D98A3C",
    "dumpGrn":   "#3E8E6E", "dumpDark":  "#2F7057", "crate":     "#C58A4A",
    "crateEdge": "#9C6B38", "taxi":      "#F2B83C", "sedan":     "#4A7FC9",
    "van":       "#E8E4DC", "tire":      "#23272E", "rim":       "#D9DEE4",
    "glassCar":  "#274156", "headlight": "#FFF2C8", "taillight": "#E8402F",
    "planter":   "#B4553B", "soil":      "#5C4030", "signDark":  "#2E3941",
    "neonCyan":  "#2FE8DE", "neonPink":  "#FF4FB0", "neonViolet":"#8A5CFF",
    "neonBase":  "#252847", "holoBlue":  "#63D8FF",
    # road surfaces
    "dirtPath":  "#B98A5A", "dirtDark":  "#9C7148", "grassEdge": "#57A83E",
    "lanePaint": "#EFE6C8", "manhole":   "#37424E", "tarLine":   "#3A3F4A",
    "sidewalk":  "#C9CDD4", "sidewalkD": "#A8AEB6", "curb":      "#9AA0A8",
    "grout":     "#8F7B5E", "neonFloor": "#1D1F38", "panelSeam": "#2A2D4E",
    "slideAqua": "#9FDCEC", "slideDeep": "#6FC4DE", "foam":      "#F2FBFF",
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

def mat(name, key=None, rough=0.85, metal=0.0, emit=0.0):
    key = key if (key and key in PAL) else (name if name in PAL else "rock")
    m = bpy.data.materials.get("M_" + name)
    if m: return m
    m = bpy.data.materials.new("M_" + name)
    m.use_nodes = True
    bs = m.node_tree.nodes["Principled BSDF"]
    bs.inputs["Base Color"].default_value = C(PAL[key])
    bs.inputs["Roughness"].default_value = rough
    bs.inputs["Metallic"].default_value = metal
    if emit > 0:
        inp = bs.inputs.get("Emission Color") or bs.inputs.get("Emission")
        if inp is not None:
            inp.default_value = C(PAL[key])
        st = bs.inputs.get("Emission Strength")
        if st is not None: st.default_value = emit
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

def bevel(obj, width=0.04, segments=2, angle=math.radians(28)):
    mod = obj.modifiers.new("Bev", 'BEVEL')
    mod.width = width; mod.segments = segments
    mod.limit_method = 'ANGLE'; mod.angle_limit = angle
    apply_mod(obj, mod)

def apply_mod(obj, mod):
    active(obj)
    try: bpy.ops.object.modifier_apply(modifier=mod.name)
    except Exception as e: print("  mod fail", mod.name, e)

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

def loft(name, profile, length, col, m=None, axis='x'):
    """Extrude a Y-Z cross-section along X (or X-Y along Z if axis='z')."""
    n = len(profile)
    if axis == 'x':
        verts = [(-length/2, y, z) for y, z in profile] + \
                [( length/2, y, z) for y, z in profile]
    else:
        verts = [(y, -length/2, z) for y, z in profile] + \
                [(y,  length/2, z) for y, z in profile]
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

def lathe(name, profile, col, m=None, segments=12, loc=(0,0,0)):
    """Revolve an (r, z) profile around Z — pottery, columns, bowls."""
    verts, faces = [], []
    nseg = segments
    for i in range(nseg):
        a = i * 2*math.pi / nseg
        ca, sa = math.cos(a), math.sin(a)
        for r, z in profile:
            verts.append((r*ca, r*sa, z))
    np_ = len(profile)
    for i in range(nseg):
        ni = (i+1) % nseg
        for j in range(np_-1):
            a = i*np_+j; b = ni*np_+j
            faces.append((a, b, b+1, a+1))
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces); me.update()
    o = bpy.data.objects.new(name, me)
    o.location = loc
    col.objects.link(o)
    recalc(o)
    if m is not None: assign(o, m)
    return o

def jitter(obj, amt=0.08, seed=None):
    rng = random.Random(seed) if seed is not None else random
    for v in obj.data.vertices:
        v.co += Vector((rng.uniform(-amt, amt), rng.uniform(-amt, amt),
                        rng.uniform(-amt, amt)))

def blob(col, name, loc, scale, m, subdiv=2, jit=0.06, seed=None):
    o = prim('ico', name, loc, scale=scale, subdivisions=subdiv, col=col, m=m)
    if jit: jitter(o, jit, seed)
    smooth(o)
    return o

def bendy_trunk(col, b_w, h, lean=(0,0), segs=5, r0=0.16, r1=0.07, m=None, name="trunk"):
    """Stacked tapered segments along a gentle arc — palm/leaning trunks."""
    pts = []
    for i in range(segs+1):
        t = i/segs
        pts.append(Vector((lean[0]*t*t, lean[1]*t*t, h*t)))
    objs = []
    for i in range(segs):
        a, bb = pts[i], pts[i+1]
        d = bb - a
        seg = prim('cyl', f"{name}{i}", (a+bb)*0.5,
                   radius=r0 + (r1-r0)*i/segs, depth=d.length*1.08,
                   vertices=9, col=col, m=m)
        seg.rotation_mode = 'QUATERNION'
        seg.rotation_quaternion = d.to_track_quat('Z','Y')
        smooth(seg)
        objs.append(seg)
    return pts[-1], objs

def canopy_cluster(col, cx, cy, cz, w, d, h, greens, seed=0, count=7):
    """Layered leaf canopy: dark under-blobs, mid blobs, light crown — real
    depth instead of one blob."""
    rng = random.Random(seed)
    # under-shade
    blob(col, "canopyDark", (cx, cy, cz + h*0.18),
         (w*0.46, d*0.46, h*0.30), mat("canD", greens[0]), jit=0.05, seed=seed)
    spots = [(-0.30,0.10,0.42,0.62),(0.30,-0.08,0.44,0.60),(0.02,0.28,0.50,0.55),
             (-0.05,-0.30,0.50,0.55),(0.24,0.22,0.58,0.48),(-0.26,-0.18,0.56,0.46)]
    for i in range(min(count, len(spots))):
        dx, dy, fz, fs = spots[i]
        g = greens[1] if i % 3 else greens[2]
        blob(col, f"can{i}", (cx+dx*w, cy+dy*d, cz+h*fz),
             (w*0.40*fs, d*0.40*fs, h*0.42*fs), mat(f"canM{i}", g),
             jit=0.05, seed=seed+i)
    blob(col, "crown", (cx+0.02*w, cy, cz+h*0.92),
         (w*0.24, d*0.24, h*0.22), mat("crown", greens[-1]), subdiv=1, jit=0.04)

def ring_marks(col, b_z, trunk_r, z_list, key="trunkDark", squash=0.35):
    for i, z in enumerate(z_list):
        prim('torus', f"ring{i}", (0, 0, z),
             major_radius=trunk_r, minor_radius=trunk_r*0.16,
             major_segments=10, minor_segments=4,
             scale=(1,1,squash), col=col, m=mat(f"ring{i}", key))

# ================================================================ FOREST

def hd_oak(col):
    """Broadleaf oak — thick tapered trunk, root flare, dense 7-blob canopy."""
    tr = prim('cone', "trunk", (0,0,1.5), radius1=0.34, radius2=0.20,
              depth=3.0, vertices=10, col=col, m=mat("ot","trunk"))
    smooth(tr)
    ring_marks(col, 0, 0.28, [0.7, 1.3, 1.9])
    # root flare — 5 squat toes
    for i in range(5):
        a = i*2*math.pi/5 + 0.4
        rt = prim('cone', f"root{i}",
                  (math.cos(a)*0.30, math.sin(a)*0.30, 0.16),
                  rot=(math.radians(70)*math.sin(a), -math.radians(70)*math.cos(a), 0),
                  radius1=0.13, radius2=0.03, depth=0.7,
                  vertices=6, col=col, m=mat(f"rt{i}","trunkDark"))
        smooth(rt)
    # two branch hints out of the trunk top
    for i, a in enumerate((0.9, 3.6)):
        br = prim('cone', f"br{i}", (math.cos(a)*0.5, math.sin(a)*0.5, 3.1),
                  rot=(math.radians(52)*math.sin(a), -math.radians(52)*math.cos(a), 0),
                  radius1=0.11, radius2=0.04, depth=1.1,
                  vertices=6, col=col, m=mat(f"br{i}","trunk"))
        smooth(br)
    canopy_cluster(col, 0, 0, 3.0, 3.2, 3.2, 2.6,
                   ["leafDeep","leafA","leafB","leafLight"], seed=11)

def hd_maple(col):
    """Autumn maple — same oak structure but amber/rust canopy accent."""
    tr = prim('cone', "trunk", (0,0,1.3), radius1=0.28, radius2=0.16,
              depth=2.6, vertices=9, col=col, m=mat("mt","trunkDark"))
    smooth(tr)
    for i in range(4):
        a = i*math.pi/2 + 0.7
        prim('cone', f"root{i}", (math.cos(a)*0.24, math.sin(a)*0.24, 0.12),
             rot=(math.radians(68)*math.sin(a), -math.radians(68)*math.cos(a), 0),
             radius1=0.10, radius2=0.02, depth=0.55, vertices=5,
             col=col, m=mat(f"mr{i}","trunkDark"))
    canopy_cluster(col, 0, 0, 2.7, 2.9, 2.9, 2.4,
                   ["mapleDeep","maple","leafWarm","leafWarm"], seed=23)

def hd_blossom(col):
    """Flowering cherry — pink canopy, dark forked trunk."""
    tr = prim('cone', "trunk", (0,0,1.2), radius1=0.26, radius2=0.15,
              depth=2.4, vertices=9, col=col, m=mat("bt","trunkDark"))
    smooth(tr)
    for i,(a,tilt) in enumerate([(0.5,30),(3.6,-34)]):
        br = prim('cone', f"br{i}", (math.cos(a)*0.42, math.sin(a)*0.42, 2.5),
                  rot=(math.radians(tilt)*math.sin(a), -math.radians(tilt)*math.cos(a), 0),
                  radius1=0.09, radius2=0.03, depth=1.0, vertices=6,
                  col=col, m=mat(f"bb{i}","trunkDark"))
        smooth(br)
    canopy_cluster(col, 0, 0, 2.5, 2.8, 2.8, 2.2,
                   ["blossomDeep","blossom","blossom","flowerW"], seed=31)
    # fallen petals sprinkled at the base
    for i in range(6):
        a = i*1.1
        prim('ico', f"petal{i}", (math.cos(a)*0.8, math.sin(a)*0.8, 0.02),
             scale=(0.09,0.09,0.03), subdivisions=1,
             col=col, m=mat(f"pet{i}","blossom" if i%2 else "blossomDeep"))

def hd_birch(col):
    """White birch — pale trunk with charcoal dash marks, light airy canopy."""
    tr = prim('cone', "trunk", (0,0,1.9), radius1=0.20, radius2=0.11,
              depth=3.8, vertices=9, col=col, m=mat("bi","birchBark"))
    smooth(tr)
    # charcoal band dashes wrapped around the trunk
    rng = random.Random(7)
    for i in range(7):
        z = 0.45 + i*0.45
        a = rng.uniform(0, math.pi)
        mark = prim('cube', f"mark{i}",
                    (math.cos(a)*0.16, math.sin(a)*0.16, z),
                    rot=(0,0,a), scale=(0.16,0.05,0.035),
                    col=col, m=mat(f"mk{i}","birchMark"))
    canopy_cluster(col, 0, 0, 3.6, 2.4, 2.4, 2.2,
                   ["leafDark","birchLeaf","birchLeaf","leafWarm"], seed=41, count=6)

def hd_pine(col, tiers=4, w=2.2, h=4.6, snow=False, cold=False):
    """Tiered conifer — stepped skirt layers, trunk glimpsed below."""
    tr = prim('cone', "trunk", (0,0,0.8), radius1=0.16, radius2=0.12,
              depth=1.7, vertices=8, col=col,
              m=mat("pt","trunkDark"))
    smooth(tr)
    g1, g2 = ("pineDark","pineCold") if cold else ("leafDeep","leafA")
    for i in range(tiers):
        t = i/tiers
        tier_w = w * (1 - t*0.72)
        tier_h = h*0.30
        z = 1.1 + i*(h*0.62/tiers) + tier_h*0.5
        skirt = prim('cone', f"tier{i}", (0, 0, z),
                     radius1=tier_w*0.5, radius2=tier_w*0.16, depth=tier_h,
                     vertices=9, col=col, m=mat(f"tg{i}", g1 if i%2 else g2))
        flat(skirt); jitter(skirt, 0.03, seed=i)
        # slight droop lip — a squashed ring under each tier edge
        prim('cone', f"lip{i}", (0, 0, z - tier_h*0.34),
             radius1=tier_w*0.52, radius2=tier_w*0.36, depth=tier_h*0.28,
             vertices=9, col=col, m=mat(f"tl{i}", g1))
        if snow:
            # snow blanket resting on the tier
            s = prim('cone', f"snow{i}", (0, 0, z + tier_h*0.22),
                     radius1=tier_w*0.34, radius2=tier_w*0.05,
                     depth=tier_h*0.30, vertices=9,
                     col=col, m=mat(f"sn{i}","snow"))
            smooth(s)
    tip = prim('cone', "tip", (0,0,h*0.93), radius1=w*0.13, radius2=0.01,
               depth=h*0.22, vertices=8, col=col, m=mat("tip", g2))
    flat(tip)
    if snow:
        blob(col, "snowTop", (0,0,h*1.0), (0.28,0.28,0.16),
             mat("snTop","snow"), subdiv=1, jit=0.02)

def hd_pine_a(col): hd_pine(col, tiers=4, w=2.3, h=4.8)
def hd_pine_b(col): hd_pine(col, tiers=5, w=1.8, h=5.4)
def hd_pine_snow(col): hd_pine(col, tiers=4, w=2.3, h=4.8, snow=True, cold=True)
def hd_pine_snow_b(col): hd_pine(col, tiers=5, w=1.9, h=5.6, snow=True, cold=True)

def hd_dead(col):
    """Gnarled dead tree — twisted trunk, three bare branches."""
    tr = prim('cone', "trunk", (0,0,1.6), radius1=0.30, radius2=0.10,
              depth=3.2, vertices=8, col=col, m=mat("dd","deadwood"))
    for v in tr.data.vertices:
        if v.co.z > 2.4:
            v.co.z -= random.uniform(0, 0.35)
            v.co.x += random.uniform(-0.08, 0.08)
    smooth(tr)
    for i,(z,a,tilt) in enumerate([(2.2,0.6,50),(2.7,2.9,-55),(1.8,4.4,42)]):
        br = prim('cone', f"limb{i}",
                  (math.cos(a)*0.28, math.sin(a)*0.28, z+0.3),
                  rot=(math.radians(tilt)*math.sin(a), -math.radians(tilt)*math.cos(a), math.radians(tilt*0.3)),
                  radius1=0.09, radius2=0.015, depth=1.3, vertices=6,
                  col=col, m=mat(f"dl{i}","deadDark"))
        smooth(br)
    for i in range(4):
        a = i*math.pi/2 + 0.3
        prim('cone', f"root{i}", (math.cos(a)*0.26, math.sin(a)*0.26, 0.12),
             rot=(math.radians(70)*math.sin(a), -math.radians(70)*math.cos(a), 0),
             radius1=0.10, radius2=0.02, depth=0.55, vertices=5,
             col=col, m=mat(f"dr{i}","deadDark"))

def hd_fern(col):
    """Fern — two rings of arcing tapered blades."""
    n = 9
    for ring,(count,tilt,lh) in enumerate([(9,42,0.9),(6,18,0.65)]):
        for i in range(count):
            a = (i + ring*0.5)*2*math.pi/count
            lf = prim('cone', f"f{ring}_{i}",
                      (math.cos(a)*0.10, math.sin(a)*0.10, lh*0.35),
                      rot=(math.radians(tilt)*math.sin(a), -math.radians(tilt)*math.cos(a), 0),
                      radius1=0.09, radius2=0.008, depth=lh, vertices=5,
                      col=col, m=mat(f"fm{ring}_{i}", "leafA" if i%2 else "leafDark"))
            smooth(lf)

def hd_bush(col, berries=True):
    """Dense shrub — three puffs + berry dots."""
    for i,(dx,dy,fz,fs,k) in enumerate([(0,0,0.55,1.0,"leafDark"),
                                        (-0.26,0.12,0.45,0.68,"leafA"),
                                        (0.28,-0.10,0.48,0.62,"leafB")]):
        blob(col, f"s{i}", (dx,dy,0.5*fz*2), (0.62*fs,0.62*fs,0.55*fs),
             mat(f"sh{i}",k), subdiv=2, jit=0.05, seed=i)
    if berries:
        rng = random.Random(3)
        for i in range(7):
            a = rng.uniform(0, 2*math.pi); r = rng.uniform(0.3,0.55)
            z = rng.uniform(0.35,0.8)
            prim('ico', f"berry{i}", (math.cos(a)*r, math.sin(a)*r, z),
                 scale=(0.06,0.06,0.06), subdivisions=1,
                 col=col, m=mat(f"bry{i}", "flowerR" if i%3 else "flowerY"))

def hd_flowers(col):
    """Wildflower patch — grass blades + coloured heads on stems."""
    rng = random.Random(9)
    for i in range(8):
        a = i*2*math.pi/8
        r = 0.30
        h = rng.uniform(0.25, 0.5)
        prim('cone', f"blade{i}", (math.cos(a)*r, math.sin(a)*r, h/2),
             rot=(math.radians(10)*math.sin(a), -math.radians(10)*math.cos(a), 0),
             radius1=0.03, radius2=0.006, depth=h, vertices=4,
             col=col, m=mat(f"bl{i}", ["leafA","leafB","leafWarm"][i%3]))
    for i in range(5):
        a = rng.uniform(0, 2*math.pi); r = rng.uniform(0.08,0.26)
        h = rng.uniform(0.3,0.55)
        prim('cyl', f"stem{i}", (math.cos(a)*r, math.sin(a)*r, h/2),
             radius=0.012, depth=h, vertices=4,
             col=col, m=mat(f"st{i}","leafDark"))
        prim('ico', f"head{i}", (math.cos(a)*r, math.sin(a)*r, h+0.04),
             scale=(0.07,0.07,0.05), subdivisions=1,
             col=col, m=mat(f"hd{i}", ["flowerR","flowerY","flowerP","flowerW"][i%4]))

def hd_grass(col):
    """Grass tuft — 9 varied blades."""
    rng = random.Random(5)
    for i in range(9):
        a = i*2*math.pi/9 + 0.3
        r = rng.uniform(0.04, 0.16)
        h = rng.uniform(0.28, 0.55)
        lf = prim('cone', f"g{i}", (math.cos(a)*r, math.sin(a)*r, h/2),
                  rot=(math.radians(rng.uniform(6,18))*math.sin(a),
                       -math.radians(rng.uniform(6,18))*math.cos(a), 0),
                  radius1=0.045, radius2=0.006, depth=h, vertices=4,
                  col=col, m=mat(f"gr{i}", ["leafWarm","leafA","leafB"][i%3]))
        smooth(lf)

def hd_mushrooms(col):
    """Red-cap mushroom cluster — three sizes."""
    rng = random.Random(4)
    for i,(x,y,h,cap) in enumerate([(-0.15,0.05,0.42,0.30),(0.18,-0.08,0.3,0.22),(0.02,0.22,0.24,0.17)]):
        st = prim('cyl', f"stem{i}", (x,y,h/2), radius=0.07*cap/0.3, depth=h,
                  vertices=7, col=col, m=mat(f"mst{i}","shroomStem"))
        smooth(st)
        cp = prim('sphere', f"cap{i}", (x,y,h), radius=cap, scale=(1,1,0.55),
                  segments=10, ring_count=6, col=col, m=mat(f"mc{i}","shroomCap"))
        smooth(cp)
        for d in range(3):
            a = rng.uniform(0,2*math.pi); rr = cap*0.5
            prim('ico', f"dot{i}_{d}", (x+math.cos(a)*rr, y+math.sin(a)*rr, h+cap*0.42),
                 scale=(0.035,0.035,0.02), subdivisions=1,
                 col=col, m=mat(f"md{i}_{d}","shroomDot"))

def hd_log(col):
    """Fallen mossy log — lies along X, cut faces at the ends."""
    body = prim('cyl', "log", (0,0,0.34), rot=(0,math.pi/2,0), radius=0.34,
                depth=2.6, vertices=10, col=col, m=mat("lg","trunk"))
    smooth(body)
    for x in (-1.31, 1.31):
        prim('cyl', f"cut{x}", (x,0,0.34), rot=(0,math.pi/2,0),
             radius=0.30, depth=0.04, vertices=10,
             col=col, m=mat(f"cut{x}","woodCut"))
        prim('torus', f"ring{x}", (x,0,0.34), rot=(0,math.pi/2,0),
             major_radius=0.17, minor_radius=0.025,
             major_segments=10, minor_segments=4,
             col=col, m=mat(f"cr{x}","trunkDark"))
    # moss blanket along the top
    blob(col, "moss", (0.1,0,0.60), (1.0,0.30,0.14),
         mat("lms","moss"), subdiv=2, jit=0.05)
    prim('cone', "stub", (0.6,-0.05,0.62), rot=(math.radians(-50),0,0),
         radius1=0.09, radius2=0.02, depth=0.5, vertices=6,
         col=col, m=mat("lst","trunkDark"))

def hd_stump(col):
    """Tree stump — ringed top, roots, moss fringe."""
    st = prim('cone', "stump", (0,0,0.5), radius1=0.44, radius2=0.36,
              depth=1.0, vertices=11, col=col, m=mat("stb","trunk"))
    smooth(st)
    prim('cyl', "top", (0,0,1.01), radius=0.37, depth=0.05, vertices=11,
         col=col, m=mat("stt","woodCut"))
    for i,rr in enumerate((0.12, 0.22, 0.30)):
        prim('torus', f"ring{i}", (0,0,1.035), major_radius=rr,
             minor_radius=0.018, major_segments=12, minor_segments=4,
             col=col, m=mat(f"sr{i}","trunkDark"))
    for i in range(4):
        a = i*math.pi/2 + 0.5
        rt = prim('cone', f"root{i}",
                  (math.cos(a)*0.42, math.sin(a)*0.42, 0.14),
                  rot=(math.radians(64)*math.sin(a), -math.radians(64)*math.cos(a), 0),
                  radius1=0.14, radius2=0.03, depth=0.6, vertices=6,
                  col=col, m=mat(f"srt{i}","trunkDark"))
        smooth(rt)
    blob(col, "moss", (-0.1,0.14,0.9), (0.3,0.24,0.10),
         mat("sms","moss"), subdiv=1, jit=0.03)

def hd_boulder(col, h=1.1, key="rock", mossy=True, warm=False):
    """Big faceted boulder — organic jitter, moss cap."""
    o = prim('ico', "rock", (0,0,h*0.42), subdivisions=2, radius=1.0,
             col=col, m=mat("br", "sandB" if warm else key))
    o.scale = (h*0.85, h*0.75, h*0.5)
    bpy.ops.object.transform_apply(scale=True)
    jitter(o, 0.10, seed=17); flat(o)
    if mossy:
        blob(col, "mosscap", (0.05,-0.02,h*0.80), (h*0.42,h*0.36,h*0.10),
             mat("bmc","moss"), subdiv=1, jit=0.04)

def hd_boulder_a(col): hd_boulder(col, 1.15)
def hd_boulder_big(col): hd_boulder(col, 1.9)
def hd_rockpile(col):
    """Pile of 4-5 assorted stones."""
    rng = random.Random(8)
    for i,(x,y,s,k) in enumerate([(-0.3,0.1,0.5,"rock"),(0.3,-0.1,0.42,"rockDark"),
                                  (0.0,0.35,0.36,"rockLight"),(-0.05,-0.32,0.30,"rock"),
                                  (0.12,0.05,0.62,"rockDark")]):
        o = prim('ico', f"r{i}", (x,y,s*0.5), subdivisions=1, radius=s,
                 col=col, m=mat(f"rp{i}",k))
        o.scale = (1, 0.85, 0.8)
        bpy.ops.object.transform_apply(scale=True)
        jitter(o, 0.05, seed=i); flat(o)

# ================================================================ CITY

def _windows(col, w, d, h, rows, cols_, y_face, mGlass, x0=-0.5, lit=None):
    """Recessed window grid on a facade facing -Y (y_face negative)."""
    rng = random.Random(2)
    for r in range(rows):
        for c in range(cols_):
            x = (c+0.5)/cols_ * w*0.8 - w*0.4 + x0
            z = (r+0.55)/rows * h*0.8 + h*0.08
            glow = lit is not None and rng.random() < 0.28
            prim('cube', f"win{r}_{c}",
                 (x, y_face, z), scale=(w*0.8/cols_*0.30, 0.04, h*0.8/rows*0.32),
                 col=col, m=mat(f"wg{r}_{c}", lit if glow else "glassCar", emit=0.8 if glow else 0))
            # sill
            prim('cube', f"sill{r}_{c}",
                 (x, y_face-0.01, z - h*0.8/rows*0.34),
                 scale=(w*0.8/cols_*0.36, 0.06, 0.025),
                 col=col, m=mat(f"sl{r}_{c}","concrete"))

def hd_bldg_brick(col):
    """Brick brownstone — window grid, cornice, rooftop water tank."""
    w, d, h = 4.6, 4.6, 8.0
    main = prim('cube', "body", (0,0,h/2), scale=(w/2,d/2,h/2),
                col=col, m=mat("bb","brick"))
    bevel(main, 0.05, 1)
    prim('cube', "base", (0,0,0.35), scale=(w/2+0.08,d/2+0.08,0.35),
         col=col, m=mat("bbb","brickDark"))
    _windows(col, w, d, h, 4, 3, -d/2-0.02, mat("g","glassCar"), lit="lampGlow")
    _windows(col, w, d, h, 4, 3,  d/2+0.02, mat("g2","glassCar"))
    # side windows on ±X too (rotated)
    for r in range(3):
        for c in range(2):
            z = (r+0.7)/3 * h*0.7 + h*0.15
            prim('cube', f"wX{r}_{c}",
                 (w/2+0.02, (c-0.5)*d*0.4, z), scale=(0.04, d*0.8/2*0.28, h*0.7/3*0.3),
                 col=col, m=mat(f"wx{r}_{c}","glassCar"))
    # cornice + parapet
    prim('cube', "cornice", (0,0,h-0.15), scale=(w/2+0.14,d/2+0.14,0.14),
         col=col, m=mat("bc","concrete"))
    prim('cube', "parapet", (0,0,h+0.12), scale=(w/2-0.05,d/2-0.05,0.12),
         col=col, m=mat("bp","brickDark"))
    # rooftop water tank on legs — the NYC silhouette
    for i,(lx,ly) in enumerate([(-0.9,-0.9),(0.9,-0.9),(-0.9,0.9),(0.9,0.9)]):
        prim('cube', f"leg{i}", (lx*0.55, ly*0.55, h+0.5),
             scale=(0.06,0.06,0.5), col=col, m=mat(f"wl{i}","steelDark"))
    tank = prim('cyl', "tank", (0,0,h+1.35), radius=0.55, depth=0.9,
                vertices=10, col=col, m=mat("wt","trunk"))
    prim('cone', "tankTop", (0,0,h+1.9), radius1=0.58, radius2=0.05,
         depth=0.35, vertices=10, col=col, m=mat("wtt","trunkDark"))
    prim('cube', "ac", (w*0.28,-d*0.28,h+0.25), scale=(0.4,0.3,0.22),
         col=col, m=mat("ac","concrete"))

def hd_bldg_shop(col):
    """Street shop — striped awning, big storefront glass, sign band."""
    w, d, h = 5.0, 4.4, 4.6
    main = prim('cube', "body", (0,0,h/2), scale=(w/2,d/2,h/2),
                col=col, m=mat("sb","tanBldg"))
    bevel(main, 0.05, 1)
    prim('cube', "base", (0,-d/2-0.02,0.1), scale=(w/2,0.08,0.1),
         col=col, m=mat("sbb","concreteD"))
    # shopfront glass + door on -Y face
    prim('cube', "glassL", (-w*0.22,-d/2-0.04,1.0),
         scale=(w*0.19,0.05,0.85), col=col, m=mat("sg","glassBlue",emit=0.35))
    prim('cube', "glassR", (w*0.22,-d/2-0.04,1.0),
         scale=(w*0.19,0.05,0.85), col=col, m=mat("sg2","glassBlue",emit=0.35))
    prim('cube', "door", (0,-d/2-0.06,0.95), scale=(0.35,0.05,0.95),
         col=col, m=mat("sd","signDark"))
    # striped awning — alternating wedges
    n = 6
    for i in range(n):
        x = (i+0.5)/n * w*0.86 - w*0.43
        a = prim('cube', f"awn{i}", (x,-d/2-0.35,1.95),
                 scale=(w*0.86/n*0.52, 0.5, 0.05),
                 col=col, m=mat(f"sa{i}", "awningR" if i%2 else "awningW"))
        a.rotation_euler.x = math.radians(-14)
    # sign band + second floor windows
    prim('cube', "sign", (0,-d/2-0.06,2.5), scale=(w*0.42,0.06,0.30),
         col=col, m=mat("ss","signDark"))
    prim('cube', "signIn", (0,-d/2-0.08,2.5), scale=(w*0.36,0.02,0.18),
         col=col, m=mat("ssi","lampGlow",emit=0.9))
    _windows(col, w, d, h*0.5, 1, 3, -d/2-0.02, mat("g3","glassCar"), x0=0, lit="lampGlow")
    # shift those windows up
    for o in list(col.objects):
        if o.name.startswith("win0_") or o.name.startswith("sill0_"):
            o.location.z += h*0.55
    prim('cube', "cornice", (0,0,h-0.1), scale=(w/2+0.1,d/2+0.1,0.1),
         col=col, m=mat("sc","concrete"))

def hd_bldg_modern(col):
    """Modern glass slab — mullion grid, lit windows, roof slab."""
    w, d, h = 4.2, 4.2, 9.5
    main = prim('cube', "body", (0,0,h/2), scale=(w/2,d/2,h/2),
                col=col, m=mat("mb","glassBlue",rough=0.35))
    bevel(main, 0.04, 1)
    # vertical mullions + floor bands, both street faces
    for c in range(4):
        x = (c+0.5)/4 * w - w/2
        prim('cube', f"mul{c}", (x,-d/2-0.03,h/2), scale=(0.05,0.04,h/2-0.4),
             col=col, m=mat(f"mm{c}","steelDark"))
    for r in range(5):
        z = (r+0.5)/5 * h*0.85 + h*0.08
        prim('cube', f"band{r}", (0,-d/2-0.03,z), scale=(w/2-0.15,0.04,0.05),
             col=col, m=mat(f"mbb{r}","steelDark"))
    rng = random.Random(5)
    for r in range(4):
        for c in range(3):
            if rng.random() < 0.25:
                x = (c+0.5)/3 * w*0.7 - w*0.35
                z = (r+0.75)/4 * h*0.75 + h*0.12
                prim('cube', f"lit{r}_{c}", (x,-d/2-0.045,z),
                     scale=(w*0.7/3*0.32,0.02,h*0.75/4*0.30),
                     col=col, m=mat(f"ml{r}_{c}","glassLit",emit=0.7))
    prim('cube', "roof", (0,0,h+0.12), scale=(w/2+0.06,d/2+0.06,0.12),
         col=col, m=mat("mr","concreteD"))
    prim('cube', "penthouse", (0.6,0.3,h+0.55), scale=(0.7,0.6,0.42),
         col=col, m=mat("mp","concrete"))

def hd_bldg_tower(col):
    """Stepped tower — two stacked masses + antenna."""
    w, d = 4.4, 4.4
    lo = prim('cube', "low", (0,0,3.0), scale=(w/2,d/2,3.0),
              col=col, m=mat("tl","concrete"))
    bevel(lo, 0.05, 1)
    hi = prim('cube', "high", (0,0,3.0+3.4), scale=(w*0.38,d*0.38,3.4),
              col=col, m=mat("th","tanBldg"))
    bevel(hi, 0.05, 1)
    _windows(col, w, d, 5.8, 4, 3, -d/2-0.02, mat("g4","glassCar"), lit="lampGlow")
    for o in list(col.objects):
        if o.name.startswith(("win","sill")): o.location.z += 0.6
    for r in range(4):
        for c in range(2):
            z = 6.6 + r*0.75
            prim('cube', f"hw{r}_{c}", ((c-0.25)*w*0.38,-d*0.38/2-0.02,z),
                 scale=(w*0.38*0.3,0.04,0.24),
                 col=col, m=mat(f"hw{r}_{c}","glassCar"))
    prim('cube', "cap", (0,0,6.4+0.15), scale=(w*0.38+0.08,d*0.38+0.08,0.15),
         col=col, m=mat("tc","brickDark"))
    prim('cyl', "antenna", (0,0,6.4+0.9), radius=0.04, depth=1.5,
         vertices=6, col=col, m=mat("ta","steelDark"))
    prim('ico', "beacon", (0,0,6.4+1.7), scale=(0.09,0.09,0.09), subdivisions=1,
         col=col, m=mat("tb","taillight",emit=1.6))

def hd_watertower(col):
    """Rooftop water tank — wood belly, steel bands, cone cap, 4 legs."""
    for i,(lx,ly) in enumerate([(-0.7,-0.7),(0.7,-0.7),(-0.7,0.7),(0.7,0.7)]):
        lg = prim('cube', f"leg{i}", (lx,ly,1.5), scale=(0.09,0.09,1.5),
                  col=col, m=mat(f"lg{i}","steelDark"))
        lg.rotation_euler.x = ly * math.radians(6)
        lg.rotation_euler.y = -lx * math.radians(6)
    prim('cube', "frame", (0,0,1.5), scale=(0.95,0.95,0.08),
         col=col, m=mat("wf","steelDark"))
    tank = prim('cyl', "tank", (0,0,2.5), radius=0.85, depth=1.5,
                vertices=12, col=col, m=mat("tw","trunk"))
    smooth(tank)
    for i,z in enumerate((1.9, 2.5, 3.1)):
        prim('torus', f"band{i}", (0,0,z), major_radius=0.87,
             minor_radius=0.035, major_segments=12, minor_segments=4,
             col=col, m=mat(f"tb{i}","steelDark"))
    prim('cone', "cap", (0,0,3.5), radius1=0.92, radius2=0.08, depth=0.7,
         vertices=12, col=col, m=mat("tc2","trunkDark"))
    prim('cube', "ladder", (0.9,-0.1,1.9), scale=(0.05,0.28,1.6),
         col=col, m=mat("ld","steelDark"))

def hd_streetlamp(col):
    """Curved-arm streetlamp — warm emissive head."""
    prim('cyl', "pole", (0,0,1.9), radius=0.07, depth=3.8, vertices=8,
         col=col, m=mat("lp","steelDark"))
    prim('cyl', "baseRing", (0,0,0.16), radius=0.13, depth=0.32, vertices=8,
         col=col, m=mat("lb","signDark"))
    # curved arm — 3 angled segments
    arm_pts = [Vector((0,0,3.75)), Vector((0.25,-0.05,3.92)), Vector((0.62,-0.08,3.9))]
    for i in range(2):
        a, b = arm_pts[i], arm_pts[i+1]
        d = b-a
        s = prim('cyl', f"arm{i}", (a+b)*0.5, radius=0.045, depth=d.length*1.1,
                 vertices=6, col=col, m=mat(f"la{i}","steelDark"))
        s.rotation_mode = 'QUATERNION'
        s.rotation_quaternion = d.to_track_quat('Z','Y')
    head = prim('cube', "head", (0.78,-0.08,3.84), scale=(0.22,0.13,0.07),
                col=col, m=mat("lh","signDark"))
    bevel(head, 0.03, 1)
    prim('cube', "bulb", (0.78,-0.08,3.77), scale=(0.15,0.09,0.03),
         col=col, m=mat("lbu","lampGlow",emit=1.5))

def hd_trafficlight(col):
    """Traffic light — pole, housing, three emissive lamps."""
    prim('cyl', "pole", (0,0,1.4), radius=0.06, depth=2.8, vertices=8,
         col=col, m=mat("tp","steelDark"))
    box = prim('cube', "box", (0,0,3.0), scale=(0.22,0.16,0.5),
               col=col, m=mat("tb3","signDark"))
    bevel(box, 0.03, 1)
    for i,(z,k) in enumerate([(3.28,"taillight"),(3.0,"lampGlow"),(2.72,"leafA")]):
        prim('cyl', f"lamp{i}", (0,-0.17,z), rot=(math.pi/2,0,0),
             radius=0.075, depth=0.03, vertices=10,
             col=col, m=mat(f"tl{i}",k,emit=1.4))
        # hood over each lamp
        prim('cube', f"hood{i}", (0,-0.20,z+0.08), scale=(0.10,0.07,0.02),
             col=col, m=mat(f"th{i}","signDark"))

def hd_busstop(col):
    """Bus shelter — posts, glass back, roof, bench, sign."""
    for x in (-1.4, 1.4):
        prim('cyl', f"post{x}", (x,0,1.15), radius=0.05, depth=2.3,
             vertices=8, col=col, m=mat(f"bp{x}","steelDark"))
    roof = prim('cube', "roof", (0,-0.05,2.35), scale=(1.7,0.7,0.08),
                col=col, m=mat("br5","brickDark"))
    bevel(roof, 0.04, 1)
    prim('cube', "glass", (0,0.28,1.25), scale=(1.5,0.03,0.85),
         col=col, m=mat("bg","glassBlue",emit=0.15))
    for x in (-0.9, 0.9):
        prim('cube', f"bleg{x}", (x,-0.1,0.28), scale=(0.08,0.28,0.28),
             col=col, m=mat(f"bl{x}","steelDark"))
    prim('cube', "seat", (0,-0.1,0.58), scale=(1.3,0.3,0.06),
         col=col, m=mat("bs","benchWood"))
    # route sign on the right post
    prim('cyl', "signpole", (1.7,0,1.6), radius=0.035, depth=3.2,
         vertices=6, col=col, m=mat("bsp","steelDark"))
    prim('cube', "sign", (1.7,0,3.0), scale=(0.3,0.06,0.4),
         col=col, m=mat("bsn","signDark"))
    prim('cube', "signface", (1.7,-0.04,3.0), scale=(0.24,0.02,0.32),
         col=col, m=mat("bsf","lampGlow",emit=0.8))

def hd_planter(col):
    """Terracotta planter — soil, shrubs, flowers."""
    box = prim('cube', "box", (0,0,0.3), scale=(0.75,0.4,0.3),
               col=col, m=mat("pb","planter"))
    bevel(box, 0.05, 1)
    prim('cube', "soil", (0,0,0.56), scale=(0.68,0.33,0.04),
         col=col, m=mat("ps","soil"))
    for i,(x,k) in enumerate([(-0.32,"leafDark"),(0.05,"leafA"),(0.35,"leafB")]):
        blob(col, f"sh{i}", (x,0,0.75), (0.22,0.20,0.18), mat(f"psh{i}",k),
             subdiv=1, jit=0.03)
    rng = random.Random(6)
    for i in range(4):
        x = rng.uniform(-0.4,0.4); y = rng.uniform(-0.15,0.15)
        prim('ico', f"fl{i}", (x,y,0.85+rng.uniform(0,0.1)),
             scale=(0.05,0.05,0.04), subdivisions=1,
             col=col, m=mat(f"pf{i}", ["flowerR","flowerY","flowerP"][i%3]))

def hd_bench(col):
    """Park bench — three wood slats, cast-iron frame."""
    for i,(z,dy) in enumerate([(0.62,0.0),(0.78,0.10),(0.94,0.14)]):
        prim('cube', f"back{i}", (0,0.22+dy,z), scale=(0.9,0.035,0.07),
             col=col, m=mat(f"bk{i}","benchWood"))
    for i,x in enumerate((-0.5,0,0.5)):
        prim('cube', f"seat{i}", (x,0.02,0.48), scale=(0.24,0.26,0.035),
             col=col, m=mat(f"bs{i}","benchWood"))
    for x in (-0.72, 0.72):
        leg = prim('cube', f"frame{x}", (x,0.05,0.24), scale=(0.05,0.30,0.24),
                   col=col, m=mat(f"bf{x}","signDark"))
        arm = prim('cube', f"arm{x}", (x,0.10,0.72), scale=(0.045,0.34,0.045),
                   col=col, m=mat(f"ba{x}","signDark"))

def hd_hydrant(col):
    body = prim('cyl', "body", (0,0,0.4), radius=0.16, depth=0.7, vertices=10,
                col=col, m=mat("hb","hydrant"))
    smooth(body); bevel(body, 0.02, 1)
    dome = prim('sphere', "dome", (0,0,0.78), radius=0.16, scale=(1,1,0.7),
                segments=10, ring_count=6, col=col, m=mat("hd2","hydrant"))
    smooth(dome)
    prim('cyl', "nut", (0,0,0.9), radius=0.05, depth=0.08, vertices=6,
         col=col, m=mat("hn","lampGlow"))
    for a in (0, math.pi):
        cap = prim('cyl', f"cap{a}", (math.cos(a)*0.19,math.sin(a)*0.19,0.5),
                   rot=(math.pi/2,0,a), radius=0.075, depth=0.09,
                   vertices=8, col=col, m=mat(f"hc{a}","lampGlow"))
        smooth(cap)
    prim('cyl', "base", (0,0,0.05), radius=0.22, depth=0.1, vertices=10,
         col=col, m=mat("hbb","hydrant"))

def hd_trashcan(col):
    body = prim('cyl', "can", (0,0,0.45), radius=0.26, depth=0.85,
                vertices=12, col=col, m=mat("tc","trashcan" if "trashcan" in PAL else "steelDark"))
    smooth(body)
    for z in (0.2, 0.45, 0.7):
        prim('torus', "rib", (0,0,z), major_radius=0.265, minor_radius=0.02,
             major_segments=12, minor_segments=4,
             col=col, m=mat("tr2","signDark"))
    prim('cyl', "lid", (0,0,0.92), radius=0.28, depth=0.09, vertices=12,
         col=col, m=mat("tld","signDark"))
    prim('cyl', "knob", (0,0,0.99), radius=0.05, depth=0.06, vertices=6,
         col=col, m=mat("tk","signDark"))

def hd_dumpster(col):
    """Green dumpster — angled lid, side ribs, caster wheels."""
    box = prim('cube', "body", (0,0,0.62), scale=(0.95,0.55,0.55),
               col=col, m=mat("db","dumpGrn"))
    bevel(box, 0.05, 1)
    lid = prim('cube', "lid", (0,0.02,1.2), scale=(0.98,0.56,0.06),
               col=col, m=mat("dl","dumpDark"))
    lid.rotation_euler.x = math.radians(4); bevel(lid, 0.03, 1)
    for x in (-0.5, 0, 0.5):
        prim('cube', f"rib{x}", (x,-0.56,0.6), scale=(0.04,0.03,0.45),
             col=col, m=mat(f"dr{x}","dumpDark"))
    for x in (-0.7, 0.7):
        for y in (-0.35, 0.35):
            prim('cyl', f"wheel{x}_{y}", (x,y,0.09), rot=(math.pi/2,0,0),
                 radius=0.09, depth=0.05, vertices=8,
                 col=col, m=mat("dw","tire"))

def hd_crates(col):
    """Stacked shipping crates — slat detail."""
    for i,(x,y,z,s,r) in enumerate([(-0.3,0,0.35,0.7,6),(0.32,0.05,0.32,0.62,-9),
                                    (0.02,-0.05,0.95,0.5,21)]):
        b = prim('cube', f"c{i}", (x,y,z), scale=(s/2,s/2,s/2),
                 rot=(0,0,math.radians(r)), col=col, m=mat(f"cr{i}","crate"))
        bevel(b, 0.03, 1)
        for fx in (-0.32, 0.32):
            prim('cube', f"edge{i}_{fx}", (x+fx*s, y, z), scale=(0.05,s/2+0.01,s/2+0.01),
                 rot=(0,0,math.radians(r)), col=col, m=mat(f"ce{i}_{fx}","crateEdge"))

def _car(col, body_key, w=0.75, l=1.9, h_body=0.35, cab_l=0.85, taxi=False):
    """Chunky car — beveled body, glass cabin, 4 wheels, lights."""
    body = prim('cube', "body", (0,0,0.42), scale=(w,l/2,h_body),
                col=col, m=mat("cb",body_key))
    bevel(body, 0.10, 2)
    cab = prim('cube', "cab", (0,0.05,0.42+h_body*0.9), scale=(w*0.78,cab_l/2,h_body*0.75),
               col=col, m=mat("cc","glassCar",rough=0.3))
    bevel(cab, 0.08, 2)
    for x in (-w*0.82, w*0.82):
        for y in (-l*0.3, l*0.3):
            wh = prim('cyl', f"wh{x}_{y}", (x,y,0.24), rot=(0,math.pi/2,0),
                      radius=0.21, depth=0.10, vertices=10,
                      col=col, m=mat("wt2","tire"))
            smooth(wh)
            prim('cyl', f"rim{x}_{y}", (x*1.01,y,0.24), rot=(0,math.pi/2,0),
                 radius=0.09, depth=0.11, vertices=8,
                 col=col, m=mat("wr","rim"))
    for x in (-w*0.55, w*0.55):
        prim('cube', f"head{x}", (x,-l/2-0.01,0.42), scale=(0.10,0.03,0.06),
             col=col, m=mat("chl","headlight",emit=0.9))
        prim('cube', f"tail{x}", (x,l/2+0.01,0.42), scale=(0.10,0.03,0.06),
             col=col, m=mat("ctl","taillight",emit=0.7))
    if taxi:
        prim('cube', "sign", (0,-0.05,0.42+h_body*0.9+h_body*0.78),
             scale=(0.28,0.12,0.08), col=col, m=mat("cs","lampGlow",emit=0.8))

def hd_taxi(col):   _car(col, "taxi", 0.78, 1.95, 0.36, 0.9, taxi=True)
def hd_sedan(col):  _car(col, "sedan", 0.76, 2.0, 0.34, 0.95)
def hd_van(col):
    body = prim('cube', "body", (0,0,0.6), scale=(0.8,1.1,0.5),
                col=col, m=mat("vb","van"))
    bevel(body, 0.08, 2)
    prim('cube', "windshield", (0,-0.92,0.75), scale=(0.68,0.05,0.24),
         col=col, m=mat("vw","glassCar"))
    for x in (-0.66, 0.66):
        for y in (-0.62, 0.62):
            wh = prim('cyl', f"wh{x}_{y}", (x,y,0.26), rot=(0,math.pi/2,0),
                      radius=0.23, depth=0.10, vertices=10,
                      col=col, m=mat("vt","tire"))
            smooth(wh)
    for x in (-0.45, 0.45):
        prim('cube', f"head{x}", (x,-1.12,0.45), scale=(0.09,0.03,0.07),
             col=col, m=mat("vh","headlight",emit=0.9))

# ================================================================ TEMPLE / VAULT

def hd_column(col):
    """Fluted column — stepped base, tapered shaft, echinus + abacus capital."""
    prim('cube', "plinth", (0,0,0.15), scale=(0.55,0.55,0.15),
         col=col, m=mat("cp","stoneShade"))
    prim('cube', "plinth2", (0,0,0.38), scale=(0.46,0.46,0.10),
         col=col, m=mat("cp2","stoneSand"))
    prim('torus', "baseRing", (0,0,0.55), major_radius=0.30, minor_radius=0.07,
         major_segments=12, minor_segments=6, col=col, m=mat("cb2","stoneSand"))
    shaft = prim('cone', "shaft", (0,0,1.9), radius1=0.27, radius2=0.22,
                 depth=2.7, vertices=12, col=col, m=mat("cs","stoneSand"))
    smooth(shaft)
    # fluting suggestion — thin shade strips
    for i in range(4):
        a = i*math.pi/2 + math.pi/4
        prim('cube', f"flute{i}", (math.cos(a)*0.245, math.sin(a)*0.245, 1.9),
             scale=(0.02,0.02,1.28), rot=(0,0,a), col=col, m=mat(f"cf{i}","stoneShade"))
    prim('torus', "neckRing", (0,0,3.28), major_radius=0.24, minor_radius=0.05,
         major_segments=12, minor_segments=5, col=col, m=mat("cn","stoneShade"))
    cap = prim('sphere', "echinus", (0,0,3.45), radius=0.34, scale=(1,1,0.42),
               segments=12, ring_count=6, col=col, m=mat("ce","stoneSand"))
    smooth(cap)
    prim('cube', "abacus", (0,0,3.68), scale=(0.5,0.5,0.09),
         col=col, m=mat("ca","stoneSand"))

def hd_column_broken(col):
    """Broken column — jagged top, fallen drum, rubble at base."""
    prim('cube', "plinth", (0,0,0.15), scale=(0.55,0.55,0.15),
         col=col, m=mat("bpp","stoneShade"))
    shaft = prim('cone', "shaft", (0,0,1.15), radius1=0.28, radius2=0.24,
                 depth=1.7, vertices=12, col=col, m=mat("bs","stoneSand"))
    for v in shaft.data.vertices:
        if v.co.z > 1.75:
            v.co.z -= random.uniform(0, 0.45)
            v.co.x += random.uniform(-0.06,0.06)
    flat(shaft)
    # fallen drum leaning against the base
    dr = prim('cyl', "drum", (0.55,-0.2,0.26), rot=(0,math.radians(72),0.4),
              radius=0.24, depth=0.5, vertices=10, col=col, m=mat("bd","stoneShade"))
    smooth(dr)
    blob(col, "chip", (-0.3,0.3,0.12), (0.16,0.14,0.10),
         mat("bc3","stoneShade"), subdiv=1, jit=0.02)

def hd_statue(col):
    """Guardian idol on a plinth — angular helm, pauldrons, gold trim."""
    prim('cube', "plinth", (0,0,0.3), scale=(0.6,0.6,0.3),
         col=col, m=mat("sp","stoneShade"))
    prim('cube', "plinthT", (0,0,0.65), scale=(0.5,0.5,0.08),
         col=col, m=mat("spt","marble"))
    body = prim('cube', "torso", (0,0,1.5), scale=(0.34,0.24,0.55),
                col=col, m=mat("st","marble"))
    bevel(body, 0.06, 1)
    prim('cube', "belt", (0,-0.01,1.15), scale=(0.30,0.24,0.05),
         col=col, m=mat("sbe","goldTrim"))
    for x in (-0.34, 0.34):  # pauldrons
        pd = prim('sphere', f"pauld{x}", (x,0,1.9), radius=0.20,
                  scale=(1,1,0.75), segments=8, ring_count=5,
                  col=col, m=mat(f"spa{x}","marble"))
        smooth(pd)
    head = prim('cube', "head", (0,0,2.2), scale=(0.17,0.17,0.20),
                col=col, m=mat("sh","marble"))
    bevel(head, 0.04, 1)
    prim('cone', "crest", (0,0,2.5), radius1=0.06, radius2=0.01, depth=0.35,
         vertices=5, col=col, m=mat("scr","goldTrim"))
    # short sword held down the front
    prim('cube', "blade", (0,-0.20,1.25), scale=(0.05,0.03,0.5),
         col=col, m=mat("sbl","concreteD"))
    prim('cube', "guard", (0,-0.20,1.62), scale=(0.16,0.05,0.04),
         col=col, m=mat("sg","goldTrim"))

def hd_brazier(col):
    """Fire bowl on tripod — emissive flame."""
    for i in range(3):
        a = i*2*math.pi/3
        lg = prim('cyl', f"leg{i}", (math.cos(a)*0.16, math.sin(a)*0.16, 0.4),
                  rot=(math.radians(18)*math.sin(a), -math.radians(18)*math.cos(a), 0),
                  radius=0.03, depth=0.85, vertices=6,
                  col=col, m=mat(f"bz{i}","bronze"))
    bowl = lathe("bowl", [(0.02,0.0),(0.28,0.06),(0.40,0.20),(0.42,0.28),(0.38,0.28)],
                 col, m=mat("bb2","bronze"), segments=12, loc=(0,0,0.8))
    # coal bed + flame
    blob(col, "coals", (0,0,0.86), (0.3,0.3,0.07), mat("bcl","charred"), subdiv=1)
    fl = prim('cone', "flame", (0,0,1.15), radius1=0.16, radius2=0.02,
              depth=0.5, vertices=7, col=col, m=mat("bf","lava",emit=2.2))
    flat(fl)
    prim('cone', "flameIn", (0,0,1.1), radius1=0.09, radius2=0.01,
         depth=0.34, vertices=6, col=col, m=mat("bfi","lavaHot",emit=2.8))

def hd_urn(col):
    """Amphora — lathed clay body, two handles."""
    lathe("urn", [(0.16,0.0),(0.20,0.04),(0.10,0.12),(0.30,0.35),(0.34,0.55),
                  (0.28,0.75),(0.14,0.85),(0.13,1.0),(0.20,1.05)],
          col, m=mat("ub","planter"), segments=12)
    for a in (0, math.pi):
        h = prim('torus', f"h{a}", (math.cos(a)*0.24, math.sin(a)*0.24, 0.75),
                 rot=(math.pi/2,0,a), major_radius=0.14, minor_radius=0.03,
                 major_segments=8, minor_segments=5,
                 scale=(1,1,1.3), col=col, m=mat(f"uh{a}","planter"))
    prim('torus', "rim", (0,0,1.06), major_radius=0.19, minor_radius=0.035,
         major_segments=12, minor_segments=5, col=col, m=mat("ur","sandDark"))

def hd_rubble(col):
    """Broken masonry chunks."""
    rng = random.Random(12)
    for i,(x,y,s) in enumerate([(-0.25,0.05,0.30),(0.22,-0.1,0.24),(0.05,0.3,0.18),
                                (-0.02,-0.28,0.14),(0.35,0.25,0.12)]):
        o = prim('cube', f"r{i}", (x,y,s*0.5), scale=(s,s*0.8,s*0.7),
                 rot=(rng.uniform(-0.3,0.3),rng.uniform(-0.3,0.3),rng.uniform(0,3)),
                 col=col, m=mat(f"rb{i}","stoneShade" if i%2 else "stoneSand"))
        bevel(o, s*0.15, 1); flat(o)

# ================================================================ CANYON

def hd_mesa(col, w=6.0, h=5.0, layers=5, tall=False):
    """Layered strata butte — alternating sandstone bands, beveled edges."""
    rng = random.Random(3)
    keys = ["sandB","sandA","sandC","sandA","sandDark"]
    z = 0
    for i in range(layers):
        t = i/layers
        lh = h/layers * rng.uniform(0.85,1.2)
        lw = w*(1-t*0.55)*rng.uniform(0.92,1.05)
        ld = w*0.8*(1-t*0.55)*rng.uniform(0.92,1.05)
        ox, oy = rng.uniform(-0.12,0.12)*w*t, rng.uniform(-0.10,0.10)*w*t
        o = prim('cube', f"strata{i}", (ox,oy,z+lh/2),
                 scale=(lw/2,ld/2,lh/2), col=col, m=mat(f"ms{i}",keys[i%5]))
        bevel(o, min(lw,ld)*0.10, 1); flat(o)
        z += lh*0.92
    # cap boulder
    blob(col, "cap", (0,0,z+h*0.03), (w*0.18,w*0.16,h*0.09),
         mat("mc","sandDark"), subdiv=1, jit=0.05)

def hd_mesa_a(col): hd_mesa(col, 6.0, 4.2, 5)
def hd_mesa_b(col): hd_mesa(col, 4.5, 6.0, 6)
def hd_rockspire(col):
    """Hoodoo — tall stacked column, cap rock."""
    rng = random.Random(9)
    z = 0; w = 1.5
    for i in range(5):
        lh = rng.uniform(0.7,1.1)
        o = prim('cyl', f"seg{i}", (rng.uniform(-0.08,0.08),rng.uniform(-0.08,0.08),z+lh/2),
                 radius=w*(0.5 - i*0.05), depth=lh, vertices=9,
                 col=col, m=mat(f"rs{i}",["sandC","sandB","sandA"][i%3]))
        flat(o); jitter(o, 0.04, seed=i)
        z += lh*0.9
    blob(col, "cap", (0,0,z+0.3), (w*0.55,w*0.5,0.35),
         mat("rc","sandDark"), subdiv=1, jit=0.05); flat(col.objects["cap"])

def hd_saguaro(col):
    """Saguaro — ribbed trunk + two elbowed arms + bloom tips."""
    body = prim('cyl', "trunk", (0,0,1.3), radius=0.28, depth=2.6,
                vertices=10, col=col, m=mat("sg","cactus"))
    smooth(body)
    prim('sphere', "top", (0,0,2.6), radius=0.28, scale=(1,1,0.6),
         segments=10, ring_count=5, col=col, m=mat("st","cactus"))
    # ribs
    for i in range(6):
        a = i*2*math.pi/6
        prim('cube', f"rib{i}", (math.cos(a)*0.275,math.sin(a)*0.275,1.3),
             scale=(0.02,0.02,1.28), col=col, m=mat(f"sr{i}","cactusDark"))
    # arms — vertical stubs with rounded tops, elbowed out
    for i,(side,zh) in enumerate([(-1,1.3),(1,1.7)]):
        arm = prim('cyl', f"arm{i}", (side*0.42,0,zh), radius=0.14, depth=0.8,
                   vertices=8, col=col, m=mat(f"sa{i}","cactus"))
        smooth(arm)
        prim('sphere', f"armtop{i}", (side*0.42,0,zh+0.4), radius=0.14,
             scale=(1,1,0.6), segments=8, ring_count=5,
             col=col, m=mat(f"sat{i}","cactus"))
        el = prim('cyl', f"elb{i}", (side*0.30,0,zh-0.35), rot=(0,math.radians(90)*0+side*math.radians(70),0),
                  radius=0.13, depth=0.4, vertices=8,
                  col=col, m=mat(f"se{i}","cactus"))
        smooth(el)
    prim('ico', "bloom", (0,0,2.9), scale=(0.09,0.09,0.09), subdivisions=1,
         col=col, m=mat("sb","cactusBloom"))

def hd_barrelcactus(col):
    """Barrel cactus — ribbed sphere + flower crown."""
    body = prim('sphere', "body", (0,0,0.5), radius=0.5, scale=(1,1,0.85),
                segments=12, ring_count=8, col=col, m=mat("bc","cactus"))
    smooth(body)
    for i in range(8):
        a = i*2*math.pi/8
        prim('cube', f"rib{i}", (math.cos(a)*0.48,math.sin(a)*0.48,0.5),
             scale=(0.02,0.02,0.36), rot=(0,0,a),
             col=col, m=mat(f"br{i}","cactusDark"))
    prim('ico', "bloom", (0,0,0.98), scale=(0.12,0.12,0.08), subdivisions=1,
         col=col, m=mat("bb","flowerY"))

def hd_skull(col):
    """Bleached longhorn skull — cranium, snout, swept horns."""
    cr = prim('cube', "cranium", (0,0.1,0.42), scale=(0.30,0.28,0.24),
              col=col, m=mat("kc","boneWhite"))
    bevel(cr, 0.10, 2)
    sn = prim('cube', "snout", (0,-0.22,0.30), scale=(0.16,0.20,0.14),
              col=col, m=mat("ks","boneWhite"))
    bevel(sn, 0.05, 1)
    for x in (-0.09, 0.09):
        prim('cube', f"eye{x}", (x,-0.16,0.46), scale=(0.07,0.04,0.09),
             col=col, m=mat(f"ke{x}","signDark"))
    for side in (-1, 1):
        h1 = prim('cone', f"horn{side}", (side*0.38,0.14,0.55),
                  rot=(0,math.radians(80)*side,math.radians(-18)),
                  radius1=0.07, radius2=0.015, depth=0.6, vertices=7,
                  col=col, m=mat(f"kh{side}","boneWhite"))
        smooth(h1)

def hd_drybrush(col):
    """Tumbleweed ball — sparse crossed twigs."""
    rng = random.Random(15)
    for i in range(9):
        a = rng.uniform(0,2*math.pi); e = rng.uniform(-0.8,0.8)
        tw = prim('cone', f"tw{i}", (0,0,0.35),
                  rot=(rng.uniform(0,3),rng.uniform(0,3),a),
                  radius1=0.02, radius2=0.005, depth=rng.uniform(0.6,0.95),
                  vertices=4, col=col, m=mat(f"dt{i}","dryBrush"))
        # pull each twig roughly through the centre
        tw.location.x += math.cos(a)*0.05; tw.location.y += math.sin(a)*0.05

def hd_rock_desert(col): hd_boulder(col, 1.2, mossy=False, warm=True)

# ================================================================ VOLCANO

def hd_basalt_hex(col):
    """Hexagonal basalt column cluster — Giant's Causeway silhouette."""
    rng = random.Random(21)
    spots = [(0,0,2.6),(0.62,0.1,1.9),(-0.55,0.25,1.5),(0.3,-0.6,1.1),
             (-0.3,-0.5,0.8),(0.05,0.65,1.2),(-0.62,-0.15,0.55)]
    for i,(x,y,h) in enumerate(spots):
        c = prim('cyl', f"hex{i}", (x,y,h/2), radius=0.34, depth=h,
                 vertices=6, col=col, m=mat(f"hx{i}","basalt" if i%2 else "basalt2"))
        flat(c)
        prim('cyl', f"top{i}", (x,y,h+0.02), radius=0.30, depth=0.05,
             vertices=6, col=col, m=mat(f"ht{i}","basalt2"))
    # glowing lava seams between the front columns
    for i,(x,y) in enumerate([(0.32,-0.28),(-0.42,-0.12)]):
        prim('cube', f"seam{i}", (x,y,0.35), scale=(0.05,0.05,0.35),
             col=col, m=mat(f"hs{i}","lava",emit=1.8))

def hd_obsidian(col):
    """Faceted obsidian shard cluster — emissive cracks at the base."""
    for i,(x,y,h,r) in enumerate([(0,0,2.2,0.42),(0.5,0.2,1.3,0.26),(-0.42,0.15,1.0,0.22),
                                  (0.15,-0.45,0.75,0.18),(-0.3,-0.4,0.5,0.14)]):
        s = prim('cone', f"spike{i}", (x,y,h*0.45),
                 rot=(random.uniform(-0.12,0.12),random.uniform(-0.12,0.12),0),
                 radius1=r, radius2=0.02, depth=h, vertices=6,
                 col=col, m=mat(f"ob{i}","obsidian",rough=0.3,metal=0.35))
        flat(s); jitter(s, r*0.15, seed=i)
    for i,(x,y) in enumerate([(0.28,0.28),(-0.2,-0.25)]):
        prim('cube', f"crack{i}", (x,y,0.18), scale=(0.04,0.04,0.30),
             rot=(0.2,0.3,i), col=col, m=mat(f"oc{i}","lava",emit=1.6))

def hd_charred(col):
    """Burnt trunk — charcoal body, ember cracks, two stubs."""
    tr = prim('cone', "trunk", (0,0,1.4), radius1=0.26, radius2=0.10,
              depth=2.8, vertices=8, col=col, m=mat("ct","charred"))
    for v in tr.data.vertices:
        if v.co.z > 2.1: v.co.z -= random.uniform(0,0.3)
    flat(tr)
    for i,(z,a) in enumerate([(0.7,0.8),(1.3,2.6),(1.9,4.4)]):
        prim('cube', f"ember{i}",
             (math.cos(a)*0.20,math.sin(a)*0.20,z),
             scale=(0.05,0.04,0.16), rot=(0,0,a),
             col=col, m=mat(f"ce{i}","lava" if i%2 else "lavaHot",emit=2.0))
    for i,(a,tilt) in enumerate([(0.9,48),(3.8,-52)]):
        prim('cone', f"stub{i}", (math.cos(a)*0.2,math.sin(a)*0.2,1.6+i*0.5),
             rot=(math.radians(tilt)*math.sin(a),-math.radians(tilt)*math.cos(a),0),
             radius1=0.07, radius2=0.015, depth=0.7, vertices=5,
             col=col, m=mat(f"cs{i}","charred"))

def hd_lavabomb(col):
    """Dark volcanic bomb — glowing seam over the top."""
    o = prim('ico', "rock", (0,0,0.55), subdivisions=2, radius=0.75,
             col=col, m=mat("lb","basalt"))
    o.scale = (1,0.85,0.75); bpy.ops.object.transform_apply(scale=True)
    jitter(o, 0.08, seed=5); flat(o)
    for i,(x,y) in enumerate([(-0.2,0.1),(0.25,-0.05),(0.0,-0.3)]):
        prim('cube', f"seam{i}", (x,y,0.95-i*0.14),
             scale=(0.16,0.05,0.04), rot=(0.2,0.5*i,i*0.9),
             col=col, m=mat(f"ls{i}","lavaHot",emit=2.0))

# ================================================================ FROST

def hd_icecrystal(col):
    """Glacier crystal cluster — tall faceted prisms."""
    for i,(x,y,h,r,tilt) in enumerate([(0,0,1.9,0.30,4),(0.42,0.15,1.2,0.20,10),
                                       (-0.38,0.12,0.95,0.17,-12),(0.1,-0.4,0.7,0.14,18),
                                       (-0.15,-0.35,0.5,0.11,-20)]):
        s = prim('cone', f"cr{i}", (x,y,h*0.45),
                 rot=(math.radians(tilt)*0.3, math.radians(tilt), 0),
                 radius1=r, radius2=r*0.28, depth=h, vertices=6,
                 col=col, m=mat(f"ic{i}","ice" if i%2 else "iceDeep",rough=0.25,emit=0.35))
        flat(s); jitter(s, r*0.10, seed=i)
    blob(col, "snowBase", (0,0,0.08), (0.7,0.6,0.10),
         mat("isb","snow"), subdiv=1, jit=0.03)

def hd_snowdrift(col):
    """Wind-carved snow drift — smooth lumps."""
    for i,(x,y,s) in enumerate([(0,0,1.0),(-0.55,0.2,0.6),(0.5,0.15,0.55)]):
        blob(col, f"d{i}", (x,y,0.12*s*2*0.5), (0.75*s,0.6*s,0.30*s),
             mat(f"sd{i}","snow" if i==0 else "snowShade"), subdiv=2, jit=0.03, seed=i)

def hd_frozenrock(col):
    """Blue-grey rock with a snow cap."""
    o = prim('ico', "rock", (0,0,0.5), subdivisions=2, radius=0.7,
             col=col, m=mat("fr","frostRock"))
    o.scale = (1,0.9,0.72); bpy.ops.object.transform_apply(scale=True)
    jitter(o, 0.07, seed=3); flat(o)
    blob(col, "cap", (0.02,0,0.92), (0.55,0.5,0.14),
         mat("fc","snow"), subdiv=1, jit=0.03)

def hd_snowman(col):
    """Snowman — three lumps, coal eyes, carrot nose, twig arms."""
    for i,(z,s) in enumerate([(0.45,0.55),(1.15,0.42),(1.75,0.30)]):
        b = blob(col, f"ball{i}", (0,0,z), (s,s,s), mat(f"sb{i}","snow"), subdiv=2, jit=0.02)
    prim('cone', "nose", (0,-0.30,1.78), rot=(math.pi/2+0.12,0,0),
         radius1=0.05, radius2=0.008, depth=0.3, vertices=6,
         col=col, m=mat("no","maple"))
    for x in (-0.09, 0.09):
        prim('ico', f"eye{x}", (x,-0.24,1.85), scale=(0.035,0.035,0.035),
             subdivisions=1, col=col, m=mat(f"ey{x}","charred"))
    for side in (-1,1):
        prim('cone', f"arm{side}", (side*0.55,0,1.2),
             rot=(0,math.radians(75)*side,0), radius1=0.025, radius2=0.008,
             depth=0.65, vertices=4, col=col, m=mat(f"ar{side}","deadDark"))
    # scarf band
    prim('torus', "scarf", (0,0,1.52), major_radius=0.26, minor_radius=0.07,
         major_segments=10, minor_segments=5, col=col, m=mat("sc","awningR"))

# ================================================================ LAGOON

def hd_palm(col, lean=(0.9,0), fronds=8, h=4.2):
    """Coconut palm — curved segmented trunk, drooping frond ring."""
    top, _ = bendy_trunk(col, 0.4, h, lean=lean, segs=5, r0=0.17, r1=0.10,
                         m=mat("ptr","palmTrunk"))
    # trunk rings
    for i in range(3):
        t = (i+1)/4
        prim('torus', f"pring{i}", (lean[0]*t*t, lean[1]*t*t, h*t),
             major_radius=0.15-0.05*t, minor_radius=0.02,
             major_segments=8, minor_segments=4,
             col=col, m=mat(f"pr{i}","palmRings"))
    crown = Vector((lean[0], lean[1], h))
    blob(col, "crownBase", (crown.x,crown.y,crown.z+0.05), (0.3,0.3,0.22),
         mat("pcb","frondDark"), subdiv=1)
    rng = random.Random(7)
    for i in range(fronds):
        a = i*2*math.pi/fronds + rng.uniform(-0.1,0.1)
        droop = rng.uniform(0.5, 0.95)
        # upper arcing wedge then drooping tip wedge — a solid folded frond
        f1 = prim('cone', f"fr{i}a",
                  (crown.x+math.cos(a)*0.55, crown.y+math.sin(a)*0.55, crown.z+0.16),
                  rot=(math.radians(78)*math.sin(a),
                       -math.radians(78)*math.cos(a), 0),
                  radius1=0.34, radius2=0.05, depth=1.5, vertices=5,
                  scale=(1,1,0.28), col=col,
                  m=mat(f"fa{i}","frond" if i%2 else "frondDark"))
        flat(f1)
        f2 = prim('cone', f"fr{i}b",
                  (crown.x+math.cos(a)*1.35, crown.y+math.sin(a)*1.35,
                   crown.z+0.16-0.9*droop*0.55),
                  rot=(math.radians(150)*math.sin(a), -math.radians(150)*math.cos(a), 0),
                  radius1=0.22, radius2=0.02, depth=1.15, vertices=5,
                  scale=(1,1,0.22), col=col,
                  m=mat(f"fb{i}","frondLight" if i%3 else "frond"))
        flat(f2)
    for i,a in enumerate((0.6, 2.2, 4.4)):
        prim('ico', f"coco{i}", (crown.x+math.cos(a)*0.20, crown.y+math.sin(a)*0.20, crown.z-0.12),
             scale=(0.11,0.11,0.12), subdivisions=1,
             col=col, m=mat(f"cc{i}","coconut"))

def hd_palm_a(col): hd_palm(col, lean=(0.8,0), fronds=9, h=4.4)
def hd_palm_b(col): hd_palm(col, lean=(1.6,0.3), fronds=7, h=3.8)

def hd_driftwood(col):
    """Bleached driftwood — forked weathered log."""
    main = prim('cyl', "log", (0,0,0.22), rot=(0,math.pi/2,0.15), radius=0.16,
                depth=1.8, vertices=8, col=col, m=mat("dw","drift"))
    smooth(main)
    for i,(x,ang) in enumerate([(0.7,40),(0.85,-55),(-0.6,60)]):
        prim('cone', f"fork{i}", (x,0,0.4),
             rot=(math.radians(ang),0,0), radius1=0.06, radius2=0.015,
             depth=0.55, vertices=5, col=col, m=mat(f"df{i}","drift"))
    prim('cyl', "cut", (0.92,0,0.27), rot=(0,math.pi/2,0.15), radius=0.14,
         depth=0.03, vertices=8, col=col, m=mat("dc","woodCut"))

def hd_beachrock(col):
    """Smooth tide-worn rock."""
    o = prim('ico', "rock", (0,0,0.4), subdivisions=2, radius=0.7,
             col=col, m=mat("brk","beachRock"))
    o.scale = (1.1,0.9,0.62); bpy.ops.object.transform_apply(scale=True)
    jitter(o, 0.05, seed=8); smooth(o)

def hd_shells(col):
    """Shells + starfish scatter."""
    for i,(x,y) in enumerate([(-0.3,0.1),(0.15,-0.2)]):
        sh = prim('cone', f"shell{i}", (x,y,0.07),
                  rot=(math.pi/2.3, 0, i*1.2),
                  radius1=0.12, radius2=0.02, depth=0.3, vertices=7,
                  scale=(1,1,0.6), col=col, m=mat(f"sh{i}","shell"))
        flat(sh)
    # starfish — centre + 5 flat arms
    blob(col, "star", (0.25,0.2,0.05), (0.08,0.08,0.04),
         mat("st","starfish"), subdiv=1)
    for i in range(5):
        a = i*2*math.pi/5
        arm = prim('cone', f"sarm{i}", (0.25+math.cos(a)*0.10,0.2+math.sin(a)*0.10,0.05),
                   rot=(math.pi/2*math.sin(a), -math.pi/2*math.cos(a), 0),
                   radius1=0.05, radius2=0.008, depth=0.22, vertices=4,
                   scale=(1,1,0.5), col=col, m=mat(f"sa{i}","starfish"))

def hd_coral(col):
    """Branching coral — stacked pink branches."""
    rng = random.Random(11)
    for i,(x,y,h) in enumerate([(0,0,0.9),(0.18,0.1,0.7),(-0.16,-0.05,0.6),(0.05,-0.18,0.5)]):
        br = prim('cyl', f"br{i}", (x,y,h/2), radius=0.07, depth=h,
                  vertices=6, col=col, m=mat(f"co{i}","coral" if i%2 else "coralDeep"))
        br.rotation_euler.x = rng.uniform(-0.15,0.15)
        br.rotation_euler.y = rng.uniform(-0.15,0.15)
        smooth(br)
        blob(col, f"tip{i}", (x,y,h+0.03), (0.09,0.09,0.09),
             mat(f"ct{i}","coral"), subdiv=1)

# ================================================================ NEON

def hd_neon_pylon(col):
    """Dark pillar wrapped in glow rings."""
    body = prim('cube', "post", (0,0,1.5), scale=(0.4,0.4,1.5),
                col=col, m=mat("np","neonBase",rough=0.4))
    bevel(body, 0.05, 1)
    for i,(z,k) in enumerate([(0.7,"neonCyan"),(1.5,"neonPink"),(2.3,"neonViolet")]):
        prim('cyl', f"ring{i}", (0,0,z), radius=0.55, depth=0.07,
             vertices=12, col=col, m=mat(f"nr{i}",k,emit=2.0))
    prim('cube', "tip", (0,0,3.05), scale=(0.46,0.46,0.08),
         col=col, m=mat("nt","holoBlue",emit=1.6))

def hd_neon_sign(col):
    """Floating holo sign — frame + glowing bars."""
    frame = prim('cube', "frame", (0,0,1.6), scale=(0.85,0.08,0.55),
                 col=col, m=mat("nf","neonBase",rough=0.35))
    bevel(frame, 0.04, 1)
    prim('cube', "face", (0,-0.085,1.6), scale=(0.75,0.02,0.45),
         col=col, m=mat("nfa","neonViolet",emit=0.9))
    # glyph bars
    for i,(x,w,k) in enumerate([(-0.2,0.3,"neonCyan"),(0.18,0.22,"neonPink")]):
        prim('cube', f"glyph{i}", (x,-0.11,1.6+0.08*i), scale=(w/2,0.02,0.05),
             col=col, m=mat(f"ng{i}",k,emit=2.4))
    prim('cube', "glyphDot", (0.32,-0.11,1.48), scale=(0.05,0.02,0.05),
         col=col, m=mat("ngd","lampGlow",emit=2.4))
    prim('cube', "base", (0,0,0.2), scale=(0.3,0.3,0.2),
         col=col, m=mat("nb","neonBase"))
    prim('cyl', "strut", (0,0,0.9), radius=0.05, depth=1.0, vertices=6,
         col=col, m=mat("ns","neonBase"))

def hd_holo(col):
    """Floating holo cubes cluster on a dark base."""
    prim('cube', "base", (0,0,0.15), scale=(0.5,0.5,0.15),
         col=col, m=mat("hb2","neonBase"))
    for i,(x,y,z,s,k) in enumerate([(0,0,0.95,0.5,"holoBlue"),
                                    (0.22,0.1,1.5,0.28,"neonCyan"),
                                    (-0.2,-0.05,1.85,0.18,"neonPink")]):
        c = prim('cube', f"cube{i}", (x,y,z), scale=(s/2,s/2,s/2),
                 rot=(0.3,0.5*i,0.4*i), col=col, m=mat(f"hc{i}",k,emit=1.4))
        bevel(c, s*0.08, 1)

def hd_neon_shard(col):
    """Angular neon crystal — glow shard wrapped by dark edge blades."""
    s = prim('cone', "core", (0,0,1.0), radius1=0.34, radius2=0.07,
             depth=2.0, vertices=5, col=col, m=mat("nc","neonCyan",emit=1.8))
    flat(s); jitter(s, 0.04, seed=2)
    for i in range(3):  # dark blades leaning against the glow
        a = i*2*math.pi/3 + 0.5
        b = prim('cone', f"blade{i}",
                 (math.cos(a)*0.30, math.sin(a)*0.30, 0.85),
                 rot=(math.radians(10)*math.sin(a), -math.radians(10)*math.cos(a), 0),
                 radius1=0.16, radius2=0.03, depth=1.7, vertices=4,
                 col=col, m=mat(f"nsh{i}","neonBase"))
        flat(b)
    prim('cube', "base", (0,0,0.1), scale=(0.5,0.5,0.1),
         col=col, m=mat("nba","neonBase"))

# ================================================================ ROADS
# Ground slabs — pivot convention: TOP surface at z=0, body hangs down.
# Placed flush on the tile's collider cube so gameplay is untouched.

def _slab(col, name, w, l, top, thick, key, rough=0.9):
    o = prim('cube', name, (0, 0, top - thick/2), scale=(w/2, l/2, thick/2),
             col=col, m=mat(name, key, rough=rough))
    bevel(o, 0.05, 1)
    return o

def hd_rd_dirt(col):
    """Forest dirt path 9.4x24 — wheel ruts, grass verge lips, pebbles."""
    _slab(col, "path", 9.4, 24, 0, 0.22, "dirtPath", rough=0.95)
    for i, x in enumerate((-1.6, 1.6)):
        prim('cube', f"rut{i}", (x, 0, 0.006), scale=(0.55, 12, 0.012),
             col=col, m=mat(f"rut{i}", "dirtDark", rough=0.98))
    for i, x in enumerate((-4.55, 4.55)):
        v = prim('cube', f"verge{i}", (x, 0, 0.015), scale=(0.32, 12, 0.05),
                 col=col, m=mat(f"v{i}", "grassEdge"))
        bevel(v, 0.03, 1)
    rng = random.Random(31)
    for i in range(26):
        x, y = rng.uniform(-4.0, 4.0), rng.uniform(-11.4, 11.4)
        if i % 3 == 0:
            blob(col, f"tuft{i}", (x, y, 0.02), (0.16, 0.14, 0.10),
                 mat(f"tf{i}", "leafB", rough=0.95), subdiv=1, jit=0.03, seed=i)
        else:
            blob(col, f"peb{i}", (x, y, 0.005), (0.10, 0.09, 0.045),
                 mat(f"pb{i}", "stoneSand"), subdiv=1, jit=0.03, seed=i)

def hd_rd_sand(col):
    """Canyon sandy wash 9.4x24 — wind-ripple ridges, embedded flat stones."""
    _slab(col, "wash", 9.4, 24, 0, 0.22, "sandA", rough=0.98)
    rng = random.Random(77)
    for i in range(10):
        y = -11.0 + i * 2.4 + rng.uniform(-0.5, 0.5)
        r = prim('cube', f"rip{i}", (rng.uniform(-1, 1), y, 0.008),
                 scale=(4.2, 0.09, 0.016), rot=(0, 0, rng.uniform(-0.08, 0.08)),
                 col=col, m=mat(f"r{i}", "sandLight", rough=0.98))
        bevel(r, 0.03, 1)
    for i, x in enumerate((-4.55, 4.55)):
        prim('cube', f"edge{i}", (x, 0, 0.012), scale=(0.35, 12, 0.045),
             col=col, m=mat(f"e{i}", "sandB"))
    for i in range(14):
        blob(col, f"st{i}", (rng.uniform(-3.8, 3.8), rng.uniform(-11, 11), -0.01),
             (rng.uniform(0.18, 0.4), rng.uniform(0.15, 0.35), 0.08),
             mat(f"s{i}", rng.choice(["stoneSand", "sandDark", "rockLight"])),
             subdiv=1, jit=0.05, seed=40+i)

def hd_rd_city(col):
    """9.6x24 asphalt — lane-splitting dashes, edge lines, manholes, tar seams."""
    _slab(col, "road", 9.6, 24, 0, 0.20, "asphalt", rough=0.55)
    for i in range(8):                      # dashes sit ON the lane seams (x=±1.1)
        for j, x in enumerate((-1.1, 1.1)):
            prim('cube', f"dash{i}_{j}", (x, -10.5+i*3.0, 0.008),
                 scale=(0.07, 0.65, 0.012), col=col,
                 m=mat("dash", "lanePaint", rough=0.5))
    for i, x in enumerate((-4.45, 4.45)):
        prim('cube', f"eline{i}", (x, 0, 0.008), scale=(0.07, 12, 0.012),
             col=col, m=mat(f"el{i}", "lanePaint", rough=0.5))
    rng = random.Random(9)
    for i in range(3):
        prim('cyl', f"mh{i}", (rng.uniform(-2.5, 2.5), -8+i*7.5, 0.006),
             radius=0.34, depth=0.015, vertices=12,
             col=col, m=mat(f"mh{i}", "manhole", rough=0.35, metal=0.4))
    for i in range(6):
        prim('cube', f"tar{i}", (rng.uniform(-2, 2), rng.uniform(-10, 10), 0.006),
             scale=(rng.uniform(0.5, 1.6), 0.05, 0.010),
             rot=(0, 0, rng.uniform(-0.25, 0.25)),
             col=col, m=mat(f"tar{i}", "tarLine", rough=0.7))

def hd_rd_walk(col):
    """Sidewalk 2.1x24 — paving joints + curb lip on the +x (street) edge."""
    _slab(col, "walk", 2.1, 24, 0, 0.24, "sidewalk", rough=0.5)
    for i in range(12):
        prim('cube', f"j{i}", (0, -11+i*2.0, 0.004), scale=(1.05, 0.035, 0.010),
             col=col, m=mat(f"j{i}", "sidewalkD", rough=0.6))
    c = prim('cube', "curb", (0.98, 0, 0.02), scale=(0.07, 12, 0.10),
             col=col, m=mat("curb", "curb", rough=0.5))
    bevel(c, 0.02, 1)

def hd_rd_stone(col):
    """Flagstone path 9.4x24 — beveled slabs on a grout bed, worn borders."""
    _slab(col, "bed", 9.4, 24, -0.01, 0.20, "grout", rough=0.95)
    rng = random.Random(5)
    for zi in range(10):
        for xi, x in enumerate((-3.45, -1.15, 1.15, 3.45)):
            f = prim('cube', f"flag{zi}_{xi}",
                     (x + rng.uniform(-0.05, 0.05),
                      -10.8 + zi*2.4 + rng.uniform(-0.06, 0.06),
                      0.015 + rng.uniform(0, 0.012)),
                     scale=(rng.uniform(1.0, 1.1), rng.uniform(1.05, 1.18), 0.035),
                     col=col,
                     m=mat(f"f{zi}{xi}", rng.choice(
                         ["stoneSand", "stoneSand", "stoneShade", "sandLight"]),
                         rough=0.85))
            bevel(f, 0.05, 1)
    for i, x in enumerate((-4.6, 4.6)):
        prim('cube', f"edge{i}", (x, 0, 0.02), scale=(0.10, 12, 0.07),
             col=col, m=mat(f"be{i}", "stoneShade"))

def hd_rd_plaza(col):
    """24x24 turn plaza — weathered 4m flagstone grid on grout."""
    _slab(col, "pbed", 24, 24, -0.01, 0.20, "grout", rough=0.95)
    rng = random.Random(13)
    for zi in range(6):
        for xi in range(6):
            f = prim('cube', f"pf{zi}{xi}",
                     (-10+xi*4 + rng.uniform(-0.08, 0.08),
                      -10+zi*4 + rng.uniform(-0.08, 0.08),
                      0.012 + rng.uniform(0, 0.014)),
                     scale=(rng.uniform(1.85, 1.95), rng.uniform(1.85, 1.95), 0.035),
                     col=col,
                     m=mat(f"pf{zi}{xi}", rng.choice(
                         ["stoneSand", "stoneShade", "sandLight", "stoneSand"]),
                         rough=0.85))
            bevel(f, 0.06, 1)
            f.rotation_euler[2] = rng.uniform(-0.02, 0.02)

def hd_rd_lava(col):
    """2.8x24 basalt ledge — glowing heat seams + jagged drop-edge chips."""
    _slab(col, "ledge", 2.8, 24, 0, 0.30, "basalt", rough=0.9)
    rng = random.Random(21)
    for i in range(5):
        prim('cube', f"seam{i}",
             (rng.uniform(-0.9, 0.9), rng.uniform(-10, 10), 0.005),
             scale=(0.05, rng.uniform(1.2, 2.6), 0.012),
             rot=(0, 0, rng.uniform(-0.3, 0.3)),
             col=col, m=mat(f"vs{i}", "lava", emit=2.4))
    for i in range(16):
        x = (-1 if i % 2 == 0 else 1) * (1.32 + rng.uniform(0, 0.12))
        c = prim('cube', f"chip{i}", (x, rng.uniform(-11.5, 11.5),
                 rng.uniform(-0.02, 0.02)),
                 scale=(rng.uniform(0.10, 0.22), rng.uniform(0.15, 0.4), 0.06),
                 rot=(0, rng.uniform(-0.12, 0.12), rng.uniform(-0.4, 0.4)),
                 col=col,
                 m=mat(f"ch{i}", "basalt2" if i % 3 else "obsidian", rough=0.7))
        flat(c)
    for i in range(4):
        blob(col, f"glow{i}", (rng.uniform(-1.1, 1.1), rng.uniform(-10, 10), 0.0),
             (0.10, 0.10, 0.05), mat(f"gb{i}", "lavaHot", emit=2.8),
             subdiv=1, jit=0.02, seed=i)

def hd_rd_ice(col):
    """9.4x24 packed snow lane — three icy ribbons + drift lips."""
    _slab(col, "snowbase", 9.4, 24, 0, 0.24, "snow", rough=0.6)
    for i, x in enumerate((-2.2, 0, 2.2)):
        prim('cube', f"ice{i}", (x, 0, 0.008), scale=(0.75, 12, 0.016),
             col=col, m=mat(f"ic{i}", "ice", rough=0.18))
    for i, x in enumerate((-4.55, 4.55)):
        blob(col, f"lip{i}", (x, 0, -0.02), (0.5, 12, 0.22),
             mat(f"dl{i}", "snowShade", rough=0.7), subdiv=2, jit=0.05, seed=i)
    rng = random.Random(51)
    for i in range(12):
        x, y = rng.uniform(-3.6, 3.6), rng.uniform(-11, 11)
        blob(col, f"s{i}", (x, y, -0.015),
             (rng.uniform(0.12, 0.3), rng.uniform(0.12, 0.28), 0.09),
             mat(f"sp{i}", "iceDeep" if i % 4 == 0 else "snowShade", rough=0.4),
             subdiv=1, jit=0.04, seed=90+i)

def hd_rd_basalt(col):
    """9.4x24 ember floor — dark basalt, glowing lane veins, raised plates."""
    _slab(col, "base", 9.4, 24, 0, 0.24, "basalt", rough=0.85)
    for i, x in enumerate((-1.1, 1.1)):
        prim('cube', f"vein{i}", (x, 0, 0.008), scale=(0.06, 12, 0.016),
             col=col, m=mat(f"vn{i}", "lava", emit=2.4))
    for i, x in enumerate((-3.4, 3.4)):
        prim('cube', f"seam{i}", (x, 0, 0.008), scale=(0.08, 12, 0.016),
             col=col, m=mat(f"es{i}", "lavaHot", emit=2.8))
    rng = random.Random(63)
    for i in range(12):
        prim('cube', f"plate{i}", (rng.uniform(-4, 4), rng.uniform(-11, 11), 0.004),
             scale=(rng.uniform(0.5, 1.2), rng.uniform(0.4, 0.9), 0.012),
             rot=(0, 0, rng.uniform(-0.3, 0.3)), col=col,
             m=mat(f"pl{i}", "basalt2" if i % 2 else "charred", rough=0.9))

def hd_rd_neon(col):
    """9.4x24 neon deck — dark panels, cyan lane guides, pink edge glow."""
    _slab(col, "deck", 9.4, 24, 0, 0.24, "neonFloor", rough=0.35)
    for zi in range(6):
        prim('cube', f"seam{zi}", (0, -10+zi*4, 0.004), scale=(4.7, 0.03, 0.01),
             col=col, m=mat(f"ps{zi}", "panelSeam", rough=0.4))
    for i, x in enumerate((-1.1, 1.1)):
        prim('cube', f"guide{i}", (x, 0, 0.008), scale=(0.05, 12, 0.016),
             col=col, m=mat(f"gd{i}", "neonCyan", emit=2.2))
    for i, x in enumerate((-4.25, 4.25)):
        prim('cube', f"edge{i}", (x, 0, 0.008), scale=(0.07, 12, 0.016),
             col=col, m=mat(f"eg{i}", "neonPink", emit=2.2))
    rng = random.Random(19)
    for i in range(8):
        prim('cube', f"hd{i}", (rng.choice([-2.2, 0, 2.2]),
             rng.uniform(-11, 11), 0.006), scale=(0.35, 0.05, 0.01),
             col=col, m=mat(f"hb{i}",
             rng.choice(["neonViolet", "holoBlue", "neonCyan"]), emit=1.6))

def hd_rd_slide(col):
    """9.4x24 lagoon flume floor — aqua channel, lane runnels, foam streaks."""
    _slab(col, "chute", 9.4, 24, 0, 0.24, "slideAqua", rough=0.25)
    for i, x in enumerate((-2.2, 0, 2.2)):
        prim('cube', f"runnel{i}", (x, 0, 0.006), scale=(0.35, 12, 0.014),
             col=col, m=mat(f"rn{i}", "slideDeep", rough=0.15))
    for i, x in enumerate((-4.35, 4.35)):
        b = prim('cube', f"berm{i}", (x, 0, 0.08), scale=(0.35, 12, 0.32),
                 col=col, m=mat(f"bm{i}", "slideAqua", rough=0.3))
        bevel(b, 0.08, 2)
    rng = random.Random(27)
    for i in range(9):
        prim('cube', f"foam{i}", (rng.uniform(-3.4, 3.4), rng.uniform(-11, 11),
             0.010), scale=(rng.uniform(0.3, 0.7), rng.uniform(0.15, 0.45), 0.008),
             rot=(0, 0, rng.uniform(-0.2, 0.2)), col=col,
             m=mat(f"fm{i}", "foam", rough=0.4))

# ================================================================ registry

BUILDERS = {
    # forest
    "hd_oak": hd_oak, "hd_maple": hd_maple, "hd_blossom": hd_blossom,
    "hd_birch": hd_birch, "hd_pine_a": hd_pine_a, "hd_pine_b": hd_pine_b,
    "hd_dead": hd_dead, "hd_fern": hd_fern, "hd_bush": hd_bush,
    "hd_flowers": hd_flowers, "hd_grass": hd_grass, "hd_mushrooms": hd_mushrooms,
    "hd_log": hd_log, "hd_stump": hd_stump, "hd_boulder": hd_boulder_a,
    "hd_boulder_big": hd_boulder_big, "hd_rockpile": hd_rockpile,
    # city
    "hd_bldg_brick": hd_bldg_brick, "hd_bldg_shop": hd_bldg_shop,
    "hd_bldg_modern": hd_bldg_modern, "hd_bldg_tower": hd_bldg_tower,
    "hd_watertower": hd_watertower, "hd_streetlamp": hd_streetlamp,
    "hd_trafficlight": hd_trafficlight, "hd_busstop": hd_busstop,
    "hd_planter": hd_planter, "hd_bench": hd_bench, "hd_hydrant": hd_hydrant,
    "hd_trashcan": hd_trashcan, "hd_dumpster": hd_dumpster,
    "hd_crates": hd_crates, "hd_taxi": hd_taxi, "hd_sedan": hd_sedan,
    "hd_van": hd_van,
    # temple / vault
    "hd_column": hd_column, "hd_column_broken": hd_column_broken,
    "hd_statue": hd_statue, "hd_brazier": hd_brazier, "hd_urn": hd_urn,
    "hd_rubble": hd_rubble,
    # canyon
    "hd_mesa_a": hd_mesa_a, "hd_mesa_b": hd_mesa_b,
    "hd_rockspire": hd_rockspire, "hd_saguaro": hd_saguaro,
    "hd_barrelcactus": hd_barrelcactus, "hd_skull": hd_skull,
    "hd_drybrush": hd_drybrush, "hd_rock_desert": hd_rock_desert,
    # volcano
    "hd_basalt_hex": hd_basalt_hex, "hd_obsidian": hd_obsidian,
    "hd_charred": hd_charred, "hd_lavabomb": hd_lavabomb,
    # frost
    "hd_pine_snow": hd_pine_snow, "hd_pine_snow_b": hd_pine_snow_b,
    "hd_icecrystal": hd_icecrystal, "hd_snowdrift": hd_snowdrift,
    "hd_frozenrock": hd_frozenrock, "hd_snowman": hd_snowman,
    # lagoon
    "hd_palm_a": hd_palm_a, "hd_palm_b": hd_palm_b,
    "hd_driftwood": hd_driftwood, "hd_beachrock": hd_beachrock,
    "hd_shells": hd_shells, "hd_coral": hd_coral,
    # neon
    "hd_neon_pylon": hd_neon_pylon, "hd_neon_sign": hd_neon_sign,
    "hd_holo": hd_holo, "hd_neon_shard": hd_neon_shard,
    # road surfaces (pivot at slab top — drop onto the tile's collider cube)
    "hd_rd_dirt": hd_rd_dirt, "hd_rd_sand": hd_rd_sand,
    "hd_rd_city": hd_rd_city, "hd_rd_walk": hd_rd_walk,
    "hd_rd_stone": hd_rd_stone, "hd_rd_plaza": hd_rd_plaza,
    "hd_rd_lava": hd_rd_lava, "hd_rd_ice": hd_rd_ice,
    "hd_rd_basalt": hd_rd_basalt, "hd_rd_neon": hd_rd_neon,
    "hd_rd_slide": hd_rd_slide,
}

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
    tex_dir = os.path.join(out_dir, "textures")
    if os.path.isdir(tex_dir): shutil.rmtree(tex_dir, ignore_errors=True)
    n = sum(len(o.data.polygons) for o in bpy.data.objects if o.type=='MESH')
    print(f"[hd] {asset_id}: {n} tris")

only = sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else []
for aid, fn in BUILDERS.items():
    if only and aid not in only: continue
    try:
        col = reset()
        fn(col)
        export(col, aid)
    except Exception as e:
        print(f"[hd] FAILED {aid}: {e}")
        import traceback; traceback.print_exc()

print("[hd] ALL DONE")
