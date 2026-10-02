#!/usr/bin/env python3
"""Download CC0 assets from PolyHaven for the realistic art upgrade.
Models -> .gltf + .bin + textures (glTFast imports natively in Unity)
Textures -> diff/nor/rough jpg sets for tiling materials
HDRIs -> .hdr skyboxes
"""
import json, os, sys, urllib.request, concurrent.futures

BASE = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                    "Assets", "PolyDash", "Art", "Realistic")
M_DIR = os.path.join(BASE, "Models")
T_DIR = os.path.join(BASE, "Textures")
S_DIR = os.path.join(BASE, "Sky")
for d in (M_DIR, T_DIR, S_DIR):
    os.makedirs(d, exist_ok=True)

MODELS = [
    # forest
    ("fir_tree_01", "2k"), ("pine_tree_01", "2k"), ("tree_stump_01", "2k"),
    ("tree_stump_02", "2k"), ("dead_tree_trunk", "2k"), ("dead_tree_trunk_02", "2k"),
    ("rock_moss_set_01", "2k"), ("boulder_01", "2k"), ("coast_rocks_02", "2k"),
    ("fern_02", "1k"), ("grass_medium_01", "1k"), ("pine_roots", "1k"),
    # city
    ("concrete_road_barrier", "2k"), ("concrete_road_barrier_02", "2k"),
    ("fire_hydrant", "2k"), ("street_lamp_01", "2k"), ("metal_trash_can", "2k"),
    ("wooden_crate_01", "2k"), ("wooden_crate_02", "2k"), ("painted_wooden_bench", "2k"),
    ("planter_box_01", "1k"), ("trashbag", "1k"), ("covered_car", "2k"),
    ("modular_street_seating", "1k"),
    # canyon / desert
    ("namaqualand_boulder_02", "2k"), ("namaqualand_boulder_04", "2k"),
    ("namaqualand_rocks_01", "2k"), ("moon_rock_01", "2k"),
    ("sand_rocks_small_01", "2k"), ("quiver_tree_01", "2k"),
    ("quiver_tree_02", "2k"), ("wild_rooibos_bush", "1k"),
    # features / ruins
    ("gothic_statue", "2k"), ("horse_statue_01", "2k"), ("wooden_lantern_01", "1k"),
    ("old_military_crate", "2k"), ("wine_barrel_01", "1k"),
]

TEXTURES = [
    "asphalt_02", "asphalt_track", "concrete_pavement", "concrete_pavers",
    "grass_path_2", "leafy_grass", "forest_floor", "rocky_trail",
    "sand_02", "red_sand", "mud_cracked_dry_03", "sandstone_cracks",
    "rock_wall_04", "cliff_side", "rock_ground", "mossy_rock",
    "wood_planks", "bark_brown_01", "painted_brick", "concrete_floor_worn_02",
]

HDRIS = [("kloppenheim_02_puresky", "2k"), ("qwantani_sunset_puresky", "2k")]

def fetch(url, dest):
    if os.path.exists(dest) and os.path.getsize(dest) > 0:
        return f"skip {dest}"
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    req = urllib.request.Request(url, headers={"User-Agent": "asset-dl"})
    with urllib.request.urlopen(req, timeout=120) as r, open(dest, "wb") as f:
        f.write(r.read())
    return f"ok   {dest}"

def api(url):
    req = urllib.request.Request(url, headers={"User-Agent": "asset-dl"})
    with urllib.request.urlopen(req, timeout=60) as r:
        return json.load(r)

def pick_res(node, want):
    if want in node: return node[want]
    for r in ("2k", "1k", "4k", "8k"):
        if r in node: return node[r]
    return None

jobs = []

for mid, res in MODELS:
    try:
        files = api(f"https://api.polyhaven.com/files/{mid}")
        gnode = files.get("gltf") or {}
        entry = pick_res(gnode, res)
        if not entry or "gltf" not in entry:
            print(f"!! no gltf for {mid}"); continue
        g = entry["gltf"]
        root = os.path.join(M_DIR, mid)
        jobs.append((g["url"], os.path.join(root, f"{mid}.gltf")))
        for rel, info in (g.get("include") or {}).items():
            url = info["url"] if isinstance(info, dict) else info
            jobs.append((url, os.path.join(root, rel)))
    except Exception as e:
        print(f"!! model {mid}: {e}")

for tid in TEXTURES:
    try:
        files = api(f"https://api.polyhaven.com/files/{tid}")
        for key in ("Diffuse", "nor_gl", "Rough", "AO"):
            node = files.get(key)
            if not node: continue
            entry = pick_res(node, "1k")
            if not entry: continue
            fmt = entry.get("jpg") or entry.get("png")
            if not fmt: continue
            ext = "jpg" if "jpg" in entry else "png"
            jobs.append((fmt["url"], os.path.join(T_DIR, tid, f"{tid}_{key}_1k.{ext}")))
    except Exception as e:
        print(f"!! texture {tid}: {e}")

for hid, res in HDRIS:
    try:
        files = api(f"https://api.polyhaven.com/files/{hid}")
        node = files.get("hdri") or {}
        entry = pick_res(node, res)
        if entry and "hdr" in entry:
            jobs.append((entry["hdr"]["url"], os.path.join(S_DIR, f"{hid}.hdr")))
    except Exception as e:
        print(f"!! hdri {hid}: {e}")

print(f"{len(jobs)} files to download")
with concurrent.futures.ThreadPoolExecutor(max_workers=10) as ex:
    futs = {ex.submit(fetch, u, d): (u, d) for u, d in jobs}
    fails = 0
    for fut in concurrent.futures.as_completed(futs):
        try:
            fut.result()
        except Exception as e:
            fails += 1
            print(f"FAIL {futs[fut][0]}: {e}")
print(f"done, {fails} failures")
