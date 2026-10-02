"""Decimate the heavy PolyHaven scans in-place for the mobile build.
Re-exports each .gltf keeping the same filename + texture URI layout so
Unity guids and prefab references stay intact."""
import bpy, os, sys

MODELS = "/Users/someshsampat/Documents/UnityProjects/Androif Hide Seek/Android Hide Seek/Assets/WarriorRun/Art/Realistic/Models"

# name -> decimate collapse ratio
TARGETS = {
    "island_tree_02":        0.03,   # 874K -> ~26K (the only leafy tree)
    "quiver_tree_01":        0.15,   # 88K -> ~13K
    "quiver_tree_02":        0.20,   # 49K -> ~10K
    "boulder_01":            0.15,   # 67K -> ~10K
    "namaqualand_boulder_02":0.20,   # 53K -> ~10K
    "dead_tree_trunk":       0.20,   # 52K -> ~10K
    "fire_hydrant":          0.20,   # 52K -> ~10K
    "dead_tree_trunk_02":    0.20,   # 46K -> ~9K
    "namaqualand_rocks_01":  0.25,   # 45K -> ~11K
    "rock_moss_set_01":      0.30,   # 34K -> ~10K
    "wild_rooibos_bush":     0.30,   # 33K -> ~10K
    "concrete_road_barrier": 0.30,   # 31K -> ~9K
    "namaqualand_boulder_04":0.30,   # 30K -> ~9K
    "grass_medium_01":       0.30,   # 27K -> ~8K
    "street_lamp_01":        0.50,   # 17K -> ~9K
}

for name, ratio in TARGETS.items():
    path = os.path.join(MODELS, name, name + ".gltf")
    if not os.path.exists(path):
        print(f"SKIP {name} (missing)")
        continue
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=path)
    before = sum(len(o.data.vertices) for o in bpy.data.objects if o.type == "MESH")
    for o in bpy.data.objects:
        if o.type != "MESH":
            continue
        bpy.context.view_layer.objects.active = o
        mod = o.modifiers.new("dec", "DECIMATE")
        mod.decimate_type = "COLLAPSE"
        mod.ratio = ratio
        bpy.ops.object.modifier_apply(modifier=mod.name)
    after = sum(len(o.data.vertices) for o in bpy.data.objects if o.type == "MESH")
    bpy.ops.export_scene.gltf(
        filepath=path,
        export_format="GLTF_SEPARATE",
        export_keep_originals=True,   # keep textures/ URIs, don't re-copy images
        export_apply=True,
        export_animations=False,
    )
    print(f"OK {name}: {before} -> {after} verts")
print("DONE")
