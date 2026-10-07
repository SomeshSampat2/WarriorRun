#!/usr/bin/env python3
"""Render contact sheets of the hd_* pack for a visual quality check."""
import bpy, os, sys, math
from mathutils import Vector

ROOT = os.path.dirname(os.path.abspath(__file__))
MODELS = os.path.join(ROOT, "Assets", "WarriorRun", "Art", "Realistic", "Models")
OUT = os.path.join(ROOT, "hd_preview")

SHEETS = {
    "forest":  ["hd_oak","hd_maple","hd_blossom","hd_birch","hd_pine_a","hd_pine_b",
                "hd_dead","hd_fern","hd_bush","hd_flowers","hd_grass","hd_mushrooms",
                "hd_log","hd_stump","hd_boulder","hd_rockpile"],
    "city":    ["hd_bldg_brick","hd_bldg_shop","hd_bldg_modern","hd_bldg_tower",
                "hd_watertower","hd_streetlamp","hd_trafficlight","hd_busstop",
                "hd_planter","hd_bench","hd_hydrant","hd_trashcan",
                "hd_dumpster","hd_crates","hd_taxi","hd_sedan","hd_van"],
    "temple":  ["hd_column","hd_column_broken","hd_statue","hd_brazier","hd_urn","hd_rubble"],
    "canyon":  ["hd_mesa_a","hd_mesa_b","hd_rockspire","hd_saguaro","hd_barrelcactus",
                "hd_skull","hd_drybrush","hd_rock_desert"],
    "volcano": ["hd_basalt_hex","hd_obsidian","hd_charred","hd_lavabomb"],
    "frost":   ["hd_pine_snow","hd_pine_snow_b","hd_icecrystal","hd_snowdrift",
                "hd_frozenrock","hd_snowman"],
    "lagoon":  ["hd_palm_a","hd_palm_b","hd_driftwood","hd_beachrock","hd_shells","hd_coral"],
    "neon":    ["hd_neon_pylon","hd_neon_sign","hd_holo","hd_neon_shard"],
    "roads":   ["hd_rd_dirt","hd_rd_sand","hd_rd_city","hd_rd_walk",
                "hd_rd_stone","hd_rd_plaza","hd_rd_lava","hd_rd_ice",
                "hd_rd_basalt","hd_rd_neon","hd_rd_slide"],
}

# big slabs need elbow room — long axis runs along the grid column
SPACING = {"roads": 30.0}

only = sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else list(SHEETS)
os.makedirs(OUT, exist_ok=True)

for sheet in only:
    ids = SHEETS[sheet]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    col = bpy.data.collections.new("S")
    sc.collection.children.link(col)

    cols = math.ceil(math.sqrt(len(ids)))
    rows = math.ceil(len(ids)/cols)
    spacing = SPACING.get(sheet, 10.0)

    for i, aid in enumerate(ids):
        gltf = os.path.join(MODELS, aid, aid + ".gltf")
        if not os.path.exists(gltf): print("MISSING", aid); continue
        before = set(bpy.data.objects)
        bpy.ops.import_scene.gltf(filepath=gltf)
        news = [o for o in bpy.data.objects if o not in before]
        root = bpy.data.objects.new(aid, None)
        col.objects.link(root)
        gx = (i % cols) * spacing - (cols-1)*spacing/2
        gy = (i // cols) * spacing - (rows-1)*spacing/2
        root.location = (gx, gy, 0)
        for o in news:
            if o.parent is None:
                o.parent = root
                for c in list(o.users_collection):
                    c.objects.unlink(o)
                col.objects.link(o)

    # ground
    bpy.ops.mesh.primitive_plane_add(size=500, location=(0,0,-0.01))
    pl = bpy.context.active_object
    m = bpy.data.materials.new("Ground"); m.use_nodes = True
    m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.09,0.10,0.12,1)
    pl.data.materials.append(m)

    # camera — 3/4 view over the grid
    zoom = max(1.0, spacing / 14.0)
    bpy.ops.object.camera_add(location=(spacing*cols*0.75*zoom, -spacing*rows*1.05*zoom, spacing*cols*0.62*zoom))
    cam = bpy.context.active_object
    cam.rotation_euler = (math.radians(62), 0, math.radians(33))
    sc.camera = cam
    cam.data.lens = 50

    # sun + ambient
    bpy.ops.object.light_add(type='SUN', location=(5,-5,10))
    sun = bpy.context.active_object
    sun.rotation_euler = (math.radians(40), math.radians(-20), math.radians(20))
    sun.data.energy = 3.2
    w = bpy.data.worlds.new("W"); sc.world = w
    w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.35,0.42,0.55,1)
    w.node_tree.nodes["Background"].inputs[1].default_value = 0.7

    sc.render.engine = 'BLENDER_EEVEE_NEXT' if hasattr(bpy.types, 'BLENDER_EEVEE_NEXT') else 'BLENDER_EEVEE'
    try: sc.render.engine = 'BLENDER_EEVEE_NEXT'
    except Exception: sc.render.engine = 'BLENDER_EEVEE'
    sc.render.resolution_x = 1600; sc.render.resolution_y = 1100
    sc.render.filepath = os.path.join(OUT, f"hd_{sheet}.png")
    bpy.ops.render.render(write_still=True)
    print("SHEET", sheet, "->", sc.render.filepath)
