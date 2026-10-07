using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using WarriorRun.World;

namespace WarriorRun.EditorTools
{
    /// <summary>
    /// Extra content pass — runs as part of the stylized build, after the
    /// material/scene restyle. Adds:
    ///   1. three brand-new biomes — Frost peaks (ice/snow), Ember wastes
    ///      (basalt + lava), Neon night (synthwave dark + glow) — each with a
    ///      pair of procedural tile skins, decor prefabs and obstacle prefabs
    ///   2. extra "wildcard" obstacles mixed into every zone's pool
    ///   3. rewires Game.unity's TrackManager: longer zones + rebalanced pools
    ///      (the old synthesized beat loops are retired — Resources/Audio now
    ///      ships real CC0/CC-BY tracks; see MUSIC_CREDITS.txt)
    /// </summary>
    public static class ExtraContent
    {
        const string Root = "Assets/WarriorRun";
        static readonly Dictionary<string, Material> mats = new();
        static readonly Dictionary<string, GameObject> pf = new();
        static Mesh cone4, cone6;

        public static void Apply()
        {
            LoadMats();
            CreateMaterials();
            CreateCones();
            CreateObstaclePrefabs();
            CreateDecorPrefabs();
            CreateTiles();
            WireGameScene();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[ExtraContent] done — 3 biomes, 8 obstacles, 13 decor");
        }

        // ================= helpers =================

        static Color Hex(string h)
        {
            if (!h.StartsWith("#")) h = "#" + h;
            ColorUtility.TryParseHtmlString(h, out var c); return c;
        }

        static void LoadMats()
        {
            foreach (var f in Directory.GetFiles(Root + "/Materials", "*.mat"))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(f.Replace('\\', '/'));
                if (m != null) mats[m.name] = m;
            }
        }

        static Material Mat(string name, string hex, float smooth = 0.12f, float emission = 0f)
        {
            string path = Root + "/Materials/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            var c = Hex(hex);
            m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            if (emission > 0f)
            {
                m.EnableKeyword("_EMISSION");
                if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", c * emission);
            }
            EditorUtility.SetDirty(m);
            mats[name] = m;
            return m;
        }

        static Material M(string name) => mats.TryGetValue(name, out var m) ? m : null;

        static void CreateMaterials()
        {
            // frost — pale blue/white, icy accents
            Mat("SnowFloor", "#EAF3F9", 0.30f);
            Mat("SnowBank", "#D6E4EE", 0.22f);
            Mat("SnowBed",  "#C2D4E4", 0.10f);
            Mat("IceLane",  "#9FD4EC", 0.40f);
            Mat("IceCrystal", "#8AD8F2", 0.48f);
            Mat("SnowCap",  "#FFFFFF", 0.30f);
            Mat("PineSnow", "#4E9468", 0.12f);
            Mat("TrunkCold", "#6B4A30", 0.10f);
            // ember — dark basalt + glowing lava veins
            Mat("Basalt",     "#3B3334", 0.08f);
            Mat("BasaltWall", "#292022", 0.06f);
            Mat("AshBed",     "#4C423C", 0.05f);
            Mat("Obsidian",   "#261D2C", 0.42f);
            Mat("EmberRock",  "#52362A", 0.14f);
            Mat("Charred",    "#201A1A", 0.05f);
            Mat("LavaLine",   "#FF7A26", 0.15f, 2.4f);
            Mat("LavaCore",   "#FFC23A", 0.15f, 2.8f);
            // neon — near-black indigo with saturated glow trims
            Mat("NeonFloor",  "#1D1F38", 0.20f);
            Mat("NeonWall",   "#13142B", 0.10f);
            Mat("NeonDark",   "#252847", 0.30f);
            Mat("NeonCyan",   "#2FE8DE", 0.20f, 2.0f);
            Mat("NeonPink",   "#FF4FB0", 0.20f, 2.0f);
            Mat("NeonViolet", "#8A5CFF", 0.20f, 1.7f);
            Mat("HoloBlue",   "#63D8FF", 0.30f, 1.4f);
        }

        // ---- tiny geometry helpers (same pattern as the builder's Prim) ----

        static GameObject Prim(PrimitiveType type, string name, Material mat, Transform parent, Vector3 pos, Vector3 scale, bool keepCollider = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            if (mat != null) go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            if (!keepCollider)
            {
                var c = go.GetComponent<Collider>();
                if (c != null) UnityEngine.Object.DestroyImmediate(c);
            }
            return go;
        }

        static GameObject MeshGO(Mesh mesh, string name, Material mat, Transform parent, Vector3 pos, Vector3 scale, float rotY = 0f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.transform.localRotation = Quaternion.Euler(0f, rotY, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>Low-poly cone — 4 sides reads as a pyramid, 6 as a pine canopy.</summary>
        static Mesh Cone(int seg)
        {
            var v = new List<Vector3> { new Vector3(0f, 1f, 0f), Vector3.zero };
            for (int i = 0; i < seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                v.Add(new Vector3(Mathf.Cos(a) * 0.5f, 0f, Mathf.Sin(a) * 0.5f));
            }
            var t = new List<int>();
            for (int i = 0; i < seg; i++) { t.Add(0); t.Add(2 + (i + 1) % seg); t.Add(2 + i); }
            for (int i = 0; i < seg; i++) { t.Add(1); t.Add(2 + i); t.Add(2 + (i + 1) % seg); }
            var m = new Mesh { vertices = v.ToArray(), triangles = t.ToArray() };
            m.RecalculateNormals();
            return m;
        }

        static void CreateCones()
        {
            cone4 = LoadOrMakeCone(4, "XCone4");
            cone6 = LoadOrMakeCone(6, "XCone6");
        }

        static Mesh LoadOrMakeCone(int seg, string name)
        {
            string path = Root + "/Meshes/" + name + ".asset";
            var m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (m == null)
            {
                m = Cone(seg); m.name = name;
                AssetDatabase.CreateAsset(m, path);
            }
            return m;
        }

        static GameObject SavePrefab(GameObject root, string name)
        {
            string path = Root + "/Prefabs/" + name + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            pf[name] = p;
            return p;
        }

        static void AddTrig(GameObject root, Vector3 c, Vector3 s)
        {
            var col = root.AddComponent<BoxCollider>();
            col.isTrigger = true; col.center = c; col.size = s;
        }

        /// <summary>Blender HD pack model (Art/Realistic/Models/hd_*) placed under parent.</summary>
        static GameObject BModel(string id, Transform parent, Vector3 localPos, float rotY = 0f)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Art/Realistic/Models/" + id + "/" + id + ".gltf");
            if (src == null) { Debug.LogWarning("[ExtraContent] missing " + id); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            go.name = id;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0f, rotY, 0f);
            foreach (var c in go.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(c);
            DetailSurfaceUpgrade.ReskinChildren(go);
            return go;
        }

        /// <summary>Uniform-scale a placed model until its bounds hit the target size.</summary>
        static void FitModel(GameObject go, float? targetX = null, float? targetY = null)
        {
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return;
            var b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            float s = 0f;
            if (targetX.HasValue) s = Mathf.Max(s, targetX.Value / Mathf.Max(0.01f, b.size.x));
            if (targetY.HasValue) s = Mathf.Max(s, targetY.Value / Mathf.Max(0.01f, b.size.y));
            if (s > 0f) go.transform.localScale *= s;
        }

        /// <summary>HD decor prefab — model inside a Body child so sway animates the model only.</summary>
        static GameObject DecorR(string id, string name, float targetY, float rotY = 0f, float sway = 0f)
        {
            var root = new GameObject(name);
            var body = new GameObject("Body"); body.transform.SetParent(root.transform, false);
            var m = BModel(id, body.transform, Vector3.zero, rotY);
            if (m != null) FitModel(m, targetY: targetY);
            if (sway > 0f) { var a = Anim(root, body.transform); a.swayDeg = sway; a.swayFreq = 1.6f; }
            return SavePrefab(root, name);
        }

        static ObstacleAnim Anim(GameObject root, Transform target)
        {
            var a = root.AddComponent<ObstacleAnim>();
            a.target = target;
            return a;
        }

        // ================= new obstacles =================

        static void CreateObstaclePrefabs()
        {
            // big packed snowball — dodge it (frost)
            {
                var root = new GameObject("X_Snowball");
                var body = new GameObject("Body"); body.transform.SetParent(root.transform, false);
                Prim(PrimitiveType.Sphere, "Ball", M("SnowCap"), body.transform, new Vector3(0, 1.0f, 0), new Vector3(1.7f, 1.7f, 1.7f));
                Prim(PrimitiveType.Sphere, "Lump1", M("SnowBank"), body.transform, new Vector3(0.8f, 0.45f, 0.35f), new Vector3(0.6f, 0.6f, 0.6f));
                Prim(PrimitiveType.Sphere, "Lump2", M("SnowBank"), body.transform, new Vector3(-0.75f, 0.5f, -0.3f), new Vector3(0.45f, 0.45f, 0.45f));
                AddTrig(root, new Vector3(0, 1.0f, 0), new Vector3(1.8f, 2.0f, 1.8f));
                root.AddComponent<Obstacle>().kind = ObstacleKind.WallBlock;
                var a = Anim(root, body.transform); a.squashAmp = 0.06f; a.squashFreq = 1.6f;
                SavePrefab(root, "X_Snowball");
            }
            // jagged ice crystal cluster — dodge it (frost)
            {
                var root = new GameObject("X_IceCrystal");
                var body = new GameObject("Body"); body.transform.SetParent(root.transform, false);
                var m = BModel("hd_icecrystal", body.transform, Vector3.zero, 15f);
                if (m != null) FitModel(m, targetY: 2.1f);
                AddTrig(root, new Vector3(0, 1.0f, 0), new Vector3(1.7f, 2.0f, 1.2f));
                root.AddComponent<Obstacle>().kind = ObstacleKind.WallBlock;
                SavePrefab(root, "X_IceCrystal");
            }
            // low snow mound — jump it (frost)
            {
                var root = new GameObject("X_SnowMound");
                var m = BModel("hd_snowdrift", root.transform, new Vector3(0, -0.05f, 0));
                if (m != null) FitModel(m, targetX: 2.3f);
                AddTrig(root, new Vector3(0, 0.42f, 0), new Vector3(2.1f, 0.85f, 1.1f));
                root.AddComponent<Obstacle>().kind = ObstacleKind.LowBarrier;
                SavePrefab(root, "X_SnowMound");
            }
            // glowing lava pool — jump it (ember)
            {
                var root = new GameObject("X_LavaPool");
                var body = new GameObject("Body"); body.transform.SetParent(root.transform, false);
                Prim(PrimitiveType.Cylinder, "Rim", M("EmberRock"), body.transform, new Vector3(0, 0.14f, 0), new Vector3(2.2f, 0.14f, 1.6f));
                var lava = Prim(PrimitiveType.Cylinder, "Lava", M("LavaCore"), body.transform, new Vector3(0, 0.26f, 0), new Vector3(1.8f, 0.07f, 1.2f));
                Prim(PrimitiveType.Cube, "Ember1", M("LavaLine"), body.transform, new Vector3(0.4f, 0.34f, 0.1f), new Vector3(0.18f, 0.05f, 0.18f));
                Prim(PrimitiveType.Cube, "Ember2", M("LavaLine"), body.transform, new Vector3(-0.45f, 0.34f, -0.15f), new Vector3(0.14f, 0.05f, 0.14f));
                AddTrig(root, new Vector3(0, 0.35f, 0), new Vector3(2.0f, 0.8f, 1.4f));
                root.AddComponent<Obstacle>().kind = ObstacleKind.LowBarrier;
                var a = Anim(root, lava.transform); a.squashAmp = 0.15f; a.squashFreq = 1.8f;
                SavePrefab(root, "X_LavaPool");
            }
            // obsidian spike cluster — dodge it (ember)
            {
                var root = new GameObject("X_ObsidianSpike");
                var body = new GameObject("Body"); body.transform.SetParent(root.transform, false);
                var m = BModel("hd_obsidian", body.transform, Vector3.zero, 25f);
                if (m != null) FitModel(m, targetY: 2.3f);
                AddTrig(root, new Vector3(0, 1.05f, 0), new Vector3(1.6f, 2.1f, 1.1f));
                root.AddComponent<Obstacle>().kind = ObstacleKind.WallBlock;
                SavePrefab(root, "X_ObsidianSpike");
            }
            // neon beam on two posts — slide under it (neon)
            {
                var root = new GameObject("X_NeonBeam");
                var body = new GameObject("Body"); body.transform.SetParent(root.transform, false);
                Prim(PrimitiveType.Cube, "PostL", M("NeonDark"), body.transform, new Vector3(-1.0f, 0.7f, 0), new Vector3(0.16f, 1.4f, 0.16f));
                Prim(PrimitiveType.Cube, "PostR", M("NeonDark"), body.transform, new Vector3(1.0f, 0.7f, 0), new Vector3(0.16f, 1.4f, 0.16f));
                var beam = Prim(PrimitiveType.Cube, "Beam", M("NeonPink"), body.transform, new Vector3(0, 1.35f, 0), new Vector3(2.2f, 0.18f, 0.18f));
                Prim(PrimitiveType.Cube, "BeamGlow", M("NeonViolet"), body.transform, new Vector3(0, 1.55f, 0), new Vector3(2.2f, 0.06f, 0.06f));
                AddTrig(root, new Vector3(0, 1.25f, 0), new Vector3(2.2f, 0.55f, 0.5f));
                root.AddComponent<Obstacle>().kind = ObstacleKind.HighBarrier;
                var a = Anim(root, beam.transform); a.squashAmp = 0.10f; a.squashFreq = 3.2f;
                SavePrefab(root, "X_NeonBeam");
            }
            // dark pillar wrapped in neon rings — dodge it (neon)
            {
                var root = new GameObject("X_NeonPillar");
                var body = new GameObject("Body"); body.transform.SetParent(root.transform, false);
                var m = BModel("hd_neon_pylon", body.transform, Vector3.zero, 10f);
                if (m != null) FitModel(m, targetY: 2.4f);
                AddTrig(root, new Vector3(0, 1.15f, 0), new Vector3(1.3f, 2.3f, 1.3f));
                root.AddComponent<Obstacle>().kind = ObstacleKind.WallBlock;
                SavePrefab(root, "X_NeonPillar");
            }
            // rolling banded barrel — dodge it (shared wildcard)
            {
                var root = new GameObject("X_RollBarrel");
                var body = new GameObject("Body"); body.transform.SetParent(root.transform, false);
                var b = Prim(PrimitiveType.Cylinder, "Barrel", M("WoodDark"), body.transform, new Vector3(0, 0.9f, 0), new Vector3(1.3f, 0.75f, 1.3f));
                b.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                var s1 = Prim(PrimitiveType.Cylinder, "Band1", M("Stripe"), body.transform, new Vector3(0, 0.9f, 0.32f), new Vector3(1.36f, 0.06f, 1.36f));
                s1.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                var s2 = Prim(PrimitiveType.Cylinder, "Band2", M("Stripe"), body.transform, new Vector3(0, 0.9f, -0.32f), new Vector3(1.36f, 0.06f, 1.36f));
                s2.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                AddTrig(root, new Vector3(0, 0.9f, 0), new Vector3(1.5f, 1.8f, 1.5f));
                root.AddComponent<Obstacle>().kind = ObstacleKind.WallBlock;
                var a = Anim(root, body.transform); a.spinDegPerSec = 110f; a.spinAxis = Vector3.right;
                SavePrefab(root, "X_RollBarrel");
            }
        }

        // ================= new decor =================

        static GameObject Decor(string name, System.Action<Transform> build, float sway = 0f)
        {
            var root = new GameObject(name);
            var body = new GameObject("Body"); body.transform.SetParent(root.transform, false);
            build(body.transform);
            if (sway > 0f) { var a = Anim(root, body.transform); a.swayDeg = sway; a.swayFreq = 1.6f; }
            return SavePrefab(root, name);
        }

        static void PineSnow(Transform t, float h = 1f)
        {
            Prim(PrimitiveType.Cylinder, "Trunk", M("TrunkCold"), t, new Vector3(0, 0.5f * h, 0), new Vector3(0.22f * h, 0.5f * h, 0.22f * h));
            MeshGO(cone6, "Tier1", M("PineSnow"), t, new Vector3(0, 0.55f * h, 0), new Vector3(2.0f * h, 1.5f * h, 2.0f * h));
            MeshGO(cone6, "Tier2", M("PineSnow"), t, new Vector3(0, 1.45f * h, 0), new Vector3(1.5f * h, 1.2f * h, 1.5f * h));
            MeshGO(cone6, "Tier3", M("PineSnow"), t, new Vector3(0, 2.15f * h, 0), new Vector3(1.0f * h, 1.0f * h, 1.0f * h));
            // snow sitting on each tier edge — the thing that sells "winter"
            Prim(PrimitiveType.Sphere, "Snow1", M("SnowCap"), t, new Vector3(0.55f * h, 1.05f * h, 0.2f * h), new Vector3(0.7f * h, 0.22f * h, 0.7f * h));
            Prim(PrimitiveType.Sphere, "Snow2", M("SnowCap"), t, new Vector3(-0.4f * h, 1.8f * h, -0.15f * h), new Vector3(0.55f * h, 0.2f * h, 0.55f * h));
            Prim(PrimitiveType.Sphere, "SnowTop", M("SnowCap"), t, new Vector3(0, 3.05f * h, 0), new Vector3(0.4f * h, 0.2f * h, 0.4f * h));
        }

        static void IceShardCluster(Transform t, float s = 1f)
        {
            var c1 = Prim(PrimitiveType.Cube, "S1", M("IceCrystal"), t, new Vector3(0, 0.8f * s, 0), new Vector3(0.4f * s, 1.6f * s, 0.4f * s));
            c1.transform.localRotation = Quaternion.Euler(0f, 15f, -8f);
            var c2 = Prim(PrimitiveType.Cube, "S2", M("IceLane"), t, new Vector3(0.45f * s, 0.5f * s, 0.1f * s), new Vector3(0.28f * s, 1.0f * s, 0.28f * s));
            c2.transform.localRotation = Quaternion.Euler(0f, -30f, 16f);
            Prim(PrimitiveType.Cube, "Base", M("SnowBank"), t, new Vector3(0, 0.05f * s, 0), new Vector3(1.1f * s, 0.12f * s, 0.8f * s));
        }

        static void ObsidianCluster(Transform t, float s = 1f)
        {
            MeshGO(cone4, "S1", M("Obsidian"), t, new Vector3(0, 0, 0), new Vector3(1.0f * s, 2.2f * s, 1.0f * s), 15f);
            MeshGO(cone4, "S2", M("Basalt"), t, new Vector3(0.7f * s, 0, 0.3f * s), new Vector3(0.6f * s, 1.3f * s, 0.6f * s), 60f);
            Prim(PrimitiveType.Cube, "Glow", M("LavaLine"), t, new Vector3(0.35f * s, 0.4f * s, 0.2f * s), new Vector3(0.07f * s, 0.45f * s, 0.07f * s));
        }

        static void CreateDecorPrefabs()
        {
            // --- frost — Blender HD: tiered snow pines, crystals, drifts ---
            DecorR("hd_pine_snow", "F_PineSnow", 4.4f, sway: 1.6f);
            DecorR("hd_pine_snow_b", "F_PineSnowTall", 6.2f, sway: 1.4f);
            DecorR("hd_icecrystal", "F_IceShard", 2.0f);
            DecorR("hd_frozenrock", "F_SnowRock", 1.4f);
            DecorR("hd_snowman", "F_Snowman", 2.2f);
            DecorR("hd_snowdrift", "F_SnowDrift", 1.0f);
            // --- ember — obsidian shards, charred trees, lava vents ---
            DecorR("hd_obsidian", "E_Obsidian", 2.4f);
            Decor("E_LavaVent", t =>
            {
                Prim(PrimitiveType.Cylinder, "Ring", M("EmberRock"), t, new Vector3(0, 0.18f, 0), new Vector3(1.6f, 0.18f, 1.6f));
                var core = Prim(PrimitiveType.Cylinder, "Core", M("LavaCore"), t, new Vector3(0, 0.3f, 0), new Vector3(1.15f, 0.1f, 1.15f));
                var rock = BModel("hd_lavabomb", t, new Vector3(0.9f, 0f, 0.2f), 40f);
                if (rock != null) FitModel(rock, targetY: 0.8f);
                var a = Anim(t.root.gameObject, core.transform); a.squashAmp = 0.18f; a.squashFreq = 1.4f;
            });
            DecorR("hd_charred", "E_CharredTrunk", 3.4f, sway: 1.2f);
            DecorR("hd_lavabomb", "E_EmberCrag", 1.5f);
            DecorR("hd_basalt_hex", "E_Basalt", 2.6f);
            // --- neon — glow pylons, holo signs, floating cubes ---
            DecorR("hd_neon_pylon", "N_Pylon", 3.4f);
            DecorR("hd_holo", "N_HoloCube", 2.6f);
            DecorR("hd_neon_shard", "N_GlowTotem", 2.6f);
            DecorR("hd_neon_sign", "N_Sign", 2.8f);
        }

        // ================= new tile skins =================

        static void CreateTiles()
        {
            pf["Tile_Frost_A"] = MakeFrostTile(211, "Tile_Frost_A");
            pf["Tile_Frost_B"] = MakeFrostTile(277, "Tile_Frost_B");
            pf["Tile_Ember_A"] = MakeEmberTile(311, "Tile_Ember_A");
            pf["Tile_Ember_B"] = MakeEmberTile(383, "Tile_Ember_B");
            pf["Tile_Neon_A"]  = MakeNeonTile(401, "Tile_Neon_A");
            pf["Tile_Neon_B"]  = MakeNeonTile(467, "Tile_Neon_B");
        }

        /// <summary>Snowy flume — pale floor, icy lane sheen, snow banks,
        /// pines + crystals + snowmen baked along the verges.</summary>
        static GameObject MakeFrostTile(int seed, string name)
        {
            var root = new GameObject("TrackTile");
            // Blender slab carries the icy lane ribbons + drift lips; cube stays as collider
            var fl = Prim(PrimitiveType.Cube, "Floor", M("SnowFloor"), root.transform, new Vector3(0, -0.3f, 12), new Vector3(9.4f, 0.6f, 24f), keepCollider: true);
            fl.GetComponent<MeshRenderer>().enabled = false;
            BModel("hd_rd_ice", root.transform, Vector3.zero);
            foreach (var s in new[] { -1f, 1f })
                Prim(PrimitiveType.Cube, "Bank", M("SnowBank"), root.transform, new Vector3(s * 4.75f, -0.05f, 12), new Vector3(1.0f, 0.5f, 24f));
            Prim(PrimitiveType.Cube, "UnderBed", M("SnowBed"), root.transform, new Vector3(0, -0.75f, 12), new Vector3(64f, 0.5f, 24f), keepCollider: true);

            var rng = new System.Random(seed);
            for (int zi = 0; zi < 8; zi++)
                foreach (var s in new[] { -1f, 1f })
                {
                    float z = zi * 3.1f + (float)rng.NextDouble() * 1.6f;
                    float x = s * (5.6f + (float)rng.NextDouble() * 3.2f);
                    double r = rng.NextDouble();
                    if (r < 0.42f)
                    {
                        var m = BModel(rng.NextDouble() < 0.55f ? "hd_pine_snow" : "hd_pine_snow_b",
                            root.transform, new Vector3(x, -0.5f, z), rng.Next(360));
                        if (m != null) FitModel(m, targetY: 3.4f + (float)rng.NextDouble() * 2.6f);
                    }
                    else if (r < 0.6f)
                    {
                        var m = BModel("hd_icecrystal", root.transform, new Vector3(x, -0.5f, z), rng.Next(360));
                        if (m != null) FitModel(m, targetY: 1.4f + (float)rng.NextDouble() * 1.1f);
                    }
                    else if (r < 0.72f)
                    {
                        var m = BModel("hd_frozenrock", root.transform, new Vector3(x, -0.5f, z), rng.Next(360));
                        if (m != null) FitModel(m, targetX: 1.8f + (float)rng.NextDouble() * 1.0f);
                    }
                    else if (r < 0.8f)
                    {
                        var m = BModel("hd_snowman", root.transform, new Vector3(x, -0.5f, z), rng.Next(360));
                        if (m != null) FitModel(m, targetY: 1.9f + (float)rng.NextDouble() * 0.6f);
                    }
                    else if (r < 0.92f)
                    {
                        var m = BModel("hd_snowdrift", root.transform, new Vector3(x, -0.55f, z), rng.Next(360));
                        if (m != null) FitModel(m, targetX: 2.4f + (float)rng.NextDouble() * 1.4f);
                    }
                }
            // horizon — big drifts + distant pines
            for (int zi = 0; zi < 4; zi++)
                foreach (var s in new[] { -1f, 1f })
                {
                    if (rng.NextDouble() < 0.75f)
                    {
                        var m = BModel("hd_snowdrift", root.transform,
                            new Vector3(s * (11.5f + (float)rng.NextDouble() * 5f), -0.55f, zi * 6f + 2f), rng.Next(360));
                        if (m != null) FitModel(m, targetX: 4.5f + (float)rng.NextDouble() * 3f);
                    }
                    if (rng.NextDouble() < 0.6f)
                    {
                        var m = BModel(rng.NextDouble() < 0.5f ? "hd_pine_snow" : "hd_pine_snow_b", root.transform,
                            new Vector3(s * (13f + (float)rng.NextDouble() * 5f), -0.5f, zi * 6f + (float)rng.NextDouble() * 3f), rng.Next(360));
                        if (m != null) FitModel(m, targetY: 7f + (float)rng.NextDouble() * 3.5f);
                    }
                }
            return SavePrefab(root, name);
        }

        /// <summary>Volcanic bed — dark basalt floor split by glowing lava
        /// seams, obsidian shards and ember vents on the banks.</summary>
        static GameObject MakeEmberTile(int seed, string name)
        {
            var root = new GameObject("TrackTile");
            // Blender slab carries the crack network + ember veins; cube stays as collider
            var fl = Prim(PrimitiveType.Cube, "Floor", M("Basalt"), root.transform, new Vector3(0, -0.3f, 12), new Vector3(9.4f, 0.6f, 24f), keepCollider: true);
            fl.GetComponent<MeshRenderer>().enabled = false;
            BModel("hd_rd_basalt", root.transform, Vector3.zero);
            foreach (var s in new[] { -1f, 1f })
                Prim(PrimitiveType.Cube, "WallEdge", M("BasaltWall"), root.transform, new Vector3(s * 4.7f, -0.02f, 12), new Vector3(0.9f, 0.55f, 24f));
            Prim(PrimitiveType.Cube, "UnderBed", M("AshBed"), root.transform, new Vector3(0, -0.75f, 12), new Vector3(64f, 0.5f, 24f), keepCollider: true);

            var rng = new System.Random(seed);
            for (int zi = 0; zi < 8; zi++)
                foreach (var s in new[] { -1f, 1f })
                {
                    float z = zi * 3.1f + (float)rng.NextDouble() * 1.6f;
                    float x = s * (5.6f + (float)rng.NextDouble() * 3.4f);
                    double r = rng.NextDouble();
                    if (r < 0.38f)
                    {
                        var m = BModel("hd_obsidian", root.transform, new Vector3(x, -0.5f, z), rng.Next(360));
                        if (m != null) FitModel(m, targetY: 1.8f + (float)rng.NextDouble() * 1.8f);
                    }
                    else if (r < 0.58f)
                    {
                        var g = new GameObject("Vent"); g.transform.SetParent(root.transform, false);
                        g.transform.localPosition = new Vector3(x, -0.5f, z);
                        Prim(PrimitiveType.Cylinder, "Rim", M("EmberRock"), g.transform, Vector3.zero, new Vector3(1.6f, 0.16f, 1.6f));
                        Prim(PrimitiveType.Cylinder, "Core", M("LavaCore"), g.transform, new Vector3(0, 0.2f, 0), new Vector3(1.1f, 0.08f, 1.1f));
                    }
                    else if (r < 0.78f)
                    {
                        var m = BModel("hd_charred", root.transform, new Vector3(x, -0.5f, z), rng.Next(360));
                        if (m != null) FitModel(m, targetY: 2.6f + (float)rng.NextDouble() * 1.6f);
                    }
                    else if (r < 0.92f)
                    {
                        var m = BModel("hd_lavabomb", root.transform, new Vector3(x, -0.5f, z), rng.Next(360));
                        if (m != null) FitModel(m, targetX: 1.5f + (float)rng.NextDouble() * 0.9f);
                    }
                }
            // horizon — basalt columns + ember-seamed boulders
            for (int zi = 0; zi < 4; zi++)
                foreach (var s in new[] { -1f, 1f })
                {
                    if (rng.NextDouble() < 0.7f)
                    {
                        var m = BModel("hd_basalt_hex", root.transform,
                            new Vector3(s * (12f + (float)rng.NextDouble() * 5f), -0.6f, zi * 6f + (float)rng.NextDouble() * 3f), rng.Next(90));
                        if (m != null) FitModel(m, targetY: 5.5f + (float)rng.NextDouble() * 4f);
                    }
                    if (rng.NextDouble() < 0.5f)
                    {
                        var m = BModel("hd_lavabomb", root.transform,
                            new Vector3(s * (10.5f + (float)rng.NextDouble() * 4f), -0.3f, zi * 6f + 3f), rng.Next(360));
                        if (m != null) FitModel(m, targetX: 2.2f + (float)rng.NextDouble() * 1.2f);
                    }
                }
            return SavePrefab(root, name);
        }

        /// <summary>Synthwave night strip — near-black floor with neon lane
        /// guides, glowing pylons and floating holo cubes past the walls.</summary>
        static GameObject MakeNeonTile(int seed, string name)
        {
            var root = new GameObject("TrackTile");
            // Blender slab carries the neon lane guides + edge glow; cube stays as collider
            var fl = Prim(PrimitiveType.Cube, "Floor", M("NeonFloor"), root.transform, new Vector3(0, -0.3f, 12), new Vector3(9.4f, 0.6f, 24f), keepCollider: true);
            fl.GetComponent<MeshRenderer>().enabled = false;
            BModel("hd_rd_neon", root.transform, Vector3.zero);
            foreach (var s in new[] { -1f, 1f })
                Prim(PrimitiveType.Cube, "WallEdge", M("NeonWall"), root.transform, new Vector3(s * 4.75f, 0.15f, 12), new Vector3(0.8f, 0.9f, 24f));
            Prim(PrimitiveType.Cube, "UnderBed", M("NeonDark"), root.transform, new Vector3(0, -0.75f, 12), new Vector3(64f, 0.5f, 24f), keepCollider: true);

            var rng = new System.Random(seed);
            for (int zi = 0; zi < 8; zi++)
                foreach (var s in new[] { -1f, 1f })
                {
                    float z = zi * 3.1f + (float)rng.NextDouble() * 1.6f;
                    float x = s * (5.6f + (float)rng.NextDouble() * 3.4f);
                    double r = rng.NextDouble();
                    if (r < 0.4f)
                    {
                        var m = BModel("hd_neon_pylon", root.transform, new Vector3(x, -0.5f, z), rng.Next(360));
                        if (m != null) FitModel(m, targetY: 3.0f + (float)rng.NextDouble() * 1.4f);
                    }
                    else if (r < 0.58f)
                    {
                        var m = BModel("hd_neon_shard", root.transform, new Vector3(x, -0.5f, z), rng.Next(360));
                        if (m != null) FitModel(m, targetY: 2.0f + (float)rng.NextDouble() * 1.2f);
                    }
                    else if (r < 0.72f)
                    {
                        var m = BModel("hd_neon_sign", root.transform, new Vector3(x, -0.5f, z), s > 0 ? 180f + rng.Next(-20, 20) : rng.Next(-20, 20));
                        if (m != null) FitModel(m, targetY: 2.6f + (float)rng.NextDouble() * 1.0f);
                    }
                    else if (r < 0.92f)
                    {
                        // floating holo panel — reads great against dark
                        var m = BModel("hd_holo", root.transform,
                            new Vector3(x, 0.9f + (float)rng.NextDouble() * 1.4f, z), rng.Next(360));
                        if (m != null) FitModel(m, targetY: 1.6f + (float)rng.NextDouble() * 0.8f);
                    }
                }
            // horizon — distant neon orbs + glowing billboard slabs
            for (int zi = 0; zi < 4; zi++)
                foreach (var s in new[] { -1f, 1f })
                {
                    if (rng.NextDouble() < 0.6f)
                        Prim(PrimitiveType.Sphere, "Orb", M(rng.NextDouble() < 0.5f ? "NeonPink" : "NeonCyan"), root.transform,
                            new Vector3(s * (12f + (float)rng.NextDouble() * 6f), 2.2f + (float)rng.NextDouble() * 2.5f, zi * 6f + 2f),
                            new Vector3(1.1f, 1.1f, 1.1f));
                    if (rng.NextDouble() < 0.7f)
                    {
                        var m = BModel(rng.NextDouble() < 0.6f ? "hd_neon_sign" : "hd_neon_pylon", root.transform,
                            new Vector3(s * (12.5f + (float)rng.NextDouble() * 5f), -0.4f, zi * 6f + (float)rng.NextDouble() * 3f), rng.Next(360));
                        if (m != null) FitModel(m, targetY: 4.5f + (float)rng.NextDouble() * 3.5f);
                    }
                }
            return SavePrefab(root, name);
        }

        // ================= beats =================

        const int SR = 44100;

        static void WriteWav(string path, float[] samples)
        {
            using var fs = new FileStream(path, FileMode.Create);
            using var bw = new BinaryWriter(fs);
            int dataLen = samples.Length * 2;
            bw.Write("RIFF".ToCharArray()); bw.Write(36 + dataLen); bw.Write("WAVEfmt ".ToCharArray());
            bw.Write(16); bw.Write((short)1); bw.Write((short)1);
            bw.Write(SR); bw.Write(SR * 2); bw.Write((short)2); bw.Write((short)16);
            bw.Write("data".ToCharArray()); bw.Write(dataLen);
            foreach (var s in samples) bw.Write((short)Mathf.Clamp(s * 32767f, -32768f, 32767f));
        }

        class Ev
        {
            public float at; public int kind; public int note; public float vol; public float dur;
            public Ev(float a, int k, int n = 0, float v = 1f, float d = 0.25f) { at = a; kind = k; note = n; vol = v; dur = d; }
        }

        static float Note(int midi) => 440f * Mathf.Pow(2f, (midi - 69) / 12f);

        /// <summary>Render a pattern of synth hits into a seamless bar-aligned loop.</summary>
        static float[] RenderBeats(float bpm, int bars, List<Ev> events, int seed)
        {
            float beat = 60f / bpm, bar = beat * 4f;
            int total = (int)(bar * bars * SR);
            var mix = new float[total];
            var rng = new System.Random(seed);
            float Saw(float t, float f) { float p = t * f % 1f; return p * 2f - 1f; }

            foreach (var e in events)
            {
                int start = (int)(e.at * beat * SR);
                int len = Mathf.Min((int)(e.dur * beat * SR), total - start);
                // drums need their decay tail — never cut them shorter than 0.35 s
                if (e.kind == 0 || e.kind == 1 || e.kind == 6)
                    len = Mathf.Min(Mathf.Max(len, (int)(0.35f * SR)), total - start);
                if (len <= 0) continue;
                float f = Note(e.note);
                for (int i = 0; i < len; i++)
                {
                    float t = i / (float)SR, u = i / (float)len, s = 0f;
                    switch (e.kind)
                    {
                        case 0: // kick — sine pitch-drop + click
                            s = (Mathf.Sin(2f * Mathf.PI * (42f + 118f * Mathf.Exp(-t * 26f)) * t) * Mathf.Exp(-t * 13f)
                                + Mathf.Sign(Saw(t, 4000f)) * Mathf.Exp(-t * 300f) * 0.3f) * 0.95f;
                            break;
                        case 1: // snare — noise burst + body tone
                            s = (((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 17f) * 0.8f
                                + Mathf.Sin(2f * Mathf.PI * 190f * t) * Mathf.Exp(-t * 26f) * 0.5f) * 0.72f;
                            break;
                        case 2: // closed hat
                            s = ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 72f) * 0.30f;
                            break;
                        case 3: // open hat
                            s = ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 13f) * 0.32f;
                            break;
                        case 4: // 808 bass — sub sine w/ a whisper of saw
                            s = (Mathf.Sin(2f * Mathf.PI * f * t) * 0.75f + Saw(t, f) * 0.22f)
                                * Mathf.Min(1f, t * 30f) * Mathf.Pow(1f - u, 0.7f) * 0.5f;
                            break;
                        case 5: // pluck stab
                            s = (Mathf.Sin(2f * Mathf.PI * f * t) + 0.4f * Mathf.Sin(2f * Mathf.PI * 3f * f * t))
                                * Mathf.Exp(-t * 9f) * 0.5f;
                            break;
                        case 6: // clap — filtered noise burst
                            s = ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 22f) * 0.55f;
                            break;
                        case 7: // dark bell — sine + odd partial
                            s = (Mathf.Sin(2f * Mathf.PI * f * t) + 0.3f * Mathf.Sin(2f * Mathf.PI * 2.76f * f * t))
                                * Mathf.Exp(-t * 5f) * 0.4f;
                            break;
                    }
                    mix[start + i] += s * e.vol;
                }
            }
            // normalise to a safe peak
            float peak = 0.001f;
            foreach (var s in mix) peak = Mathf.Max(peak, Mathf.Abs(s));
            float g = 0.92f / peak;
            for (int i = 0; i < total; i++) mix[i] *= g;
            return mix;
        }

        static List<Ev> BoomBap()
        {
            var ev = new List<Ev>();
            int[] roots = { 33, 33, 29, 29, 36, 36, 31, 31 }; // A A F F C C G G
            int[] penta = { 69, 72, 74, 76, 79 };
            var rng = new System.Random(7);
            for (int b = 0; b < 8; b++)
            {
                float o = b * 4f;
                ev.Add(new Ev(o + 0f, 0)); ev.Add(new Ev(o + 1.75f, 0)); ev.Add(new Ev(o + 2.5f, 0));
                if (b % 2 == 1) ev.Add(new Ev(o + 3.5f, 0, 0, 0.8f));
                ev.Add(new Ev(o + 1f, 1)); ev.Add(new Ev(o + 3f, 1));
                for (int h = 0; h < 8; h++)
                    ev.Add(new Ev(o + h * 0.5f + (h % 2 == 1 ? 0.035f : 0f), 2, 0, h % 2 == 1 ? 0.9f : 0.65f));
                if (b % 2 == 1) ev.Add(new Ev(o + 3.5f, 3, 0, 0.8f));
                foreach (var bt in new[] { 0f, 1.75f, 2.5f })
                    ev.Add(new Ev(o + bt, 4, roots[b], 0.9f, 0.42f));
                if (b % 2 == 0)
                {
                    ev.Add(new Ev(o + 1.25f, 5, penta[rng.Next(penta.Length)], 0.5f));
                    ev.Add(new Ev(o + 3.25f, 5, penta[rng.Next(penta.Length)], 0.45f));
                }
            }
            return ev;
        }

        static List<Ev> Trap()
        {
            var ev = new List<Ev>();
            int[] roots = { 33, 29, 31, 36 }; // A F G C — two bars each
            int[] penta = { 57, 60, 62, 64, 67 };
            var rng = new System.Random(11);
            for (int b = 0; b < 8; b++)
            {
                float o = b * 4f;
                ev.Add(new Ev(o + 0f, 0)); ev.Add(new Ev(o + 1.5f, 0));
                ev.Add(new Ev(o + 2.75f, 0));
                if (b % 4 == 3) ev.Add(new Ev(o + 3.5f, 0, 0, 0.85f));
                ev.Add(new Ev(o + 2f, 6)); // clap on the half-time backbeat
                for (int h = 0; h < 16; h++)
                    ev.Add(new Ev(o + h * 0.25f, 2, 0, h % 4 == 0 ? 1f : 0.55f));
                if (b % 2 == 1) // hat roll into the next bar
                    for (int h = 0; h < 6; h++)
                        ev.Add(new Ev(o + 3.5f + h * (1f / 12f), 2, 0, 0.7f + h * 0.05f));
                ev.Add(new Ev(o + 0f, 4, roots[b / 2], 1f, 3.6f));
                if (b % 2 == 0)
                {
                    ev.Add(new Ev(o + 1f, 5, penta[rng.Next(penta.Length)] - 12, 0.4f));
                    ev.Add(new Ev(o + 3.5f, 5, penta[rng.Next(penta.Length)] - 12, 0.4f));
                }
            }
            return ev;
        }

        static List<Ev> Funk()
        {
            var ev = new List<Ev>();
            int[] riff = { 40, 52, 40, 43, 40, 52, 40, 38 }; // E octaves, walks to D
            var rng = new System.Random(5);
            for (int b = 0; b < 8; b++)
            {
                float o = b * 4f;
                ev.Add(new Ev(o + 0f, 0)); ev.Add(new Ev(o + 1.5f, 0));
                ev.Add(new Ev(o + 2f, 0)); ev.Add(new Ev(o + 3.5f, 0));
                ev.Add(new Ev(o + 1f, 1)); ev.Add(new Ev(o + 3f, 1));
                foreach (var bt in new[] { 0.5f, 1.5f, 2.5f, 3.5f })
                    ev.Add(new Ev(o + bt, 3, 0, 0.75f));
                for (int h = 0; h < 16; h++)
                    if (h % 4 != 2) ev.Add(new Ev(o + h * 0.25f, 2, 0, 0.4f));
                foreach (var bt in new[] { 0f, 0.5f, 1.5f, 2f, 2.5f, 3.5f })
                    ev.Add(new Ev(o + bt, 4, riff[rng.Next(riff.Length)], 0.75f, 0.3f));
                // wah-guitar-ish stabs on the offbeats
                foreach (var bt in new[] { 1f, 3f })
                    foreach (var n in new[] { 64, 67, 71 })
                        ev.Add(new Ev(o + bt, 5, n, 0.28f, 0.18f));
            }
            return ev;
        }

        static List<Ev> Dark()
        {
            var ev = new List<Ev>();
            int[] roots = { 33, 33, 28, 28, 31, 31, 33, 33 }; // A E G A drones
            int[] penta = { 57, 60, 63, 65, 67 };
            var rng = new System.Random(13);
            for (int b = 0; b < 8; b++)
            {
                float o = b * 4f;
                ev.Add(new Ev(o + 0f, 0)); ev.Add(new Ev(o + 2f, 0));
                ev.Add(new Ev(o + 2f, 6, 0, 0.9f));
                foreach (var bt in new[] { 0.5f, 2.5f })
                    ev.Add(new Ev(o + bt, 2, 0, 0.55f));
                foreach (var bt in new[] { 1.5f, 3.5f })
                    ev.Add(new Ev(o + bt, 2, 0, 0.3f));
                ev.Add(new Ev(o + 0f, 4, roots[b], 1f, 3.8f));
                if (b % 2 == 1)
                {
                    ev.Add(new Ev(o + 1f, 7, penta[rng.Next(penta.Length)], 0.5f));
                    ev.Add(new Ev(o + 3.5f, 7, penta[rng.Next(penta.Length)], 0.4f));
                }
            }
            return ev;
        }

        static void WriteBeats()
        {
            string dir = Root + "/Resources/Audio";
            var specs = new (string file, float bpm, List<Ev> ev, int seed)[]
            {
                ("beat_boombap", 94f,  BoomBap(), 7),
                ("beat_trap",    142f, Trap(),    11),
                ("beat_funk",    106f, Funk(),    5),
                ("beat_dark",    78f,  Dark(),    13),
            };
            foreach (var (file, bpm, events, seed) in specs)
                WriteWav(dir + "/" + file + ".wav", RenderBeats(bpm, 8, events, seed));
            AssetDatabase.Refresh();
            // stream — long loops are too big to decode into memory up front
            foreach (var f in Directory.GetFiles(dir, "beat_*.wav"))
            {
                var imp = AssetImporter.GetAtPath(f.Replace('\\', '/')) as AudioImporter;
                if (imp == null) continue;
                var s = imp.defaultSampleSettings;
                s.loadType = AudioClipLoadType.Streaming;
                imp.defaultSampleSettings = s;
                imp.SaveAndReimport();
            }
        }

        // ================= scene wiring =================

        static GameObject P(string name) =>
            pf.TryGetValue(name, out var p) ? p
                : AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/" + name + ".prefab");

        static void SetPrefabArray(SerializedProperty arr, params string[] names)
        {
            arr.arraySize = names.Length;
            for (int i = 0; i < names.Length; i++)
                arr.GetArrayElementAtIndex(i).objectReferenceValue = P(names[i]);
        }

        static void SetZone(SerializedProperty z, string name, string tile, string[] tiles,
            string[] decor, string[] feats, string[] obst,
            float xMin, float xMax, int dCount, int fCount, string fog,
            string[] seq = null, bool noTurns = false)
        {
            z.FindPropertyRelative("name").stringValue = name;
            z.FindPropertyRelative("tilePrefab").objectReferenceValue = tile != null ? P(tile) : null;
            var tp = z.FindPropertyRelative("tilePrefabs");
            if (tiles != null) SetPrefabArray(tp, tiles);
            else tp.arraySize = 0;
            var sq = z.FindPropertyRelative("tileSequence");
            if (seq != null) SetPrefabArray(sq, seq);
            else sq.arraySize = 0;
            var nt = z.FindPropertyRelative("noTurns");
            if (nt != null) nt.boolValue = noTurns;
            SetPrefabArray(z.FindPropertyRelative("decorPrefabs"), decor);
            if (feats != null) SetPrefabArray(z.FindPropertyRelative("featurePrefabs"), feats);
            else z.FindPropertyRelative("featurePrefabs").arraySize = 0;
            if (obst != null) SetPrefabArray(z.FindPropertyRelative("obstaclePrefabs"), obst);
            else z.FindPropertyRelative("obstaclePrefabs").arraySize = 0;
            z.FindPropertyRelative("decorXMin").floatValue = xMin;
            z.FindPropertyRelative("decorXMax").floatValue = xMax;
            z.FindPropertyRelative("decorCount").intValue = dCount;
            z.FindPropertyRelative("featureCount").intValue = fCount;
            var sl = z.FindPropertyRelative("singleLane");
            if (sl != null) sl.boolValue = false;
            var fc = z.FindPropertyRelative("fogColor");
            fc.colorValue = Hex(fog);
        }

        static void WireGameScene()
        {
            string p = Root + "/Scenes/Game.unity";
            var scene = EditorSceneManager.OpenScene(p, OpenSceneMode.Single);
            var tm = UnityEngine.Object.FindFirstObjectByType<TrackManager>();
            if (tm == null) { Debug.LogWarning("[ExtraContent] TrackManager not found"); return; }
            var so = new SerializedObject(tm);
            so.FindProperty("tilesPerZone").intValue = 14;        // ~336 m per biome — zones finally get to breathe
            so.FindProperty("baseObstacleChance").floatValue = 0.18f;
            so.FindProperty("maxObstacleChance").floatValue = 0.33f;
            SetPrefabArray(so.FindProperty("extraObstaclePrefabs"),
                "X_RollBarrel", "X_Snowball", "X_IceCrystal", "X_NeonBeam", "X_LavaPool", "X_ObsidianSpike");

            var zones = so.FindProperty("zones");
            // builder order: 0 Temple, 1 Forest, 2 Vault, 3 Canyon,
            // 4 Volcano (single-lane), 5 City, 6 Lagoon
            // rebalance existing pools — fewer standing trees, more variety
            SetPrefabArray(zones.GetArrayElementAtIndex(1).FindPropertyRelative("obstaclePrefabs"),
                "LogBarrier", "RockJump", "HighBarrier", "N_Stump", "WallBlock", "N_DeadFall");
            SetPrefabArray(zones.GetArrayElementAtIndex(3).FindPropertyRelative("obstaclePrefabs"),
                "RockJump", "CactusBlock", "SpikeTrap", "RockBlock", "WallBlock");
            // volcano is single-lane — only obstacles you can clear in place
            // (jumpable lava pools / barriers), never wall blocks
            SetPrefabArray(zones.GetArrayElementAtIndex(4).FindPropertyRelative("obstaclePrefabs"),
                "RockJump", "X_LavaPool", "RockJump", "HighBarrier");
            zones.GetArrayElementAtIndex(4).FindPropertyRelative("singleLane").boolValue = true;
            SetPrefabArray(zones.GetArrayElementAtIndex(5).FindPropertyRelative("obstaclePrefabs"),
                "C_Bench", "C_Dumpster", "HighBarrier", "C_Crates", "C_CarBlock", "WallBlock");
            SetPrefabArray(zones.GetArrayElementAtIndex(6).FindPropertyRelative("obstaclePrefabs"),
                "W_Wave", "W_Buoy", "SpikeTrap", "W_Geyser");

            // append the new biomes — Frost/Ember/Neon runners, then the
            // zip gorge: an ordered entry→canyon→landing rope descent
            zones.arraySize = 11;
            SetZone(zones.GetArrayElementAtIndex(7), "Frost",
                "Tile_Frost_A", new[] { "Tile_Frost_A", "Tile_Frost_B" },
                new[] { "F_PineSnow", "F_IceShard", "F_SnowRock", "F_Snowman", "F_PineSnowTall", "F_SnowDrift" },
                null, new[] { "X_Snowball", "X_IceCrystal", "X_SnowMound", "X_IceCrystal" },
                5.8f, 15f, 11, 0, "#DCEBF2");
            SetZone(zones.GetArrayElementAtIndex(8), "Ember",
                "Tile_Ember_A", new[] { "Tile_Ember_A", "Tile_Ember_B" },
                new[] { "E_Obsidian", "E_LavaVent", "E_CharredTrunk", "E_EmberCrag", "E_Basalt", "E_Obsidian" },
                null, new[] { "X_ObsidianSpike", "X_LavaPool", "SpikeTrap", "X_ObsidianSpike" },
                5.8f, 15f, 11, 0, "#3C2B2A");
            SetZone(zones.GetArrayElementAtIndex(9), "Neon",
                "Tile_Neon_A", new[] { "Tile_Neon_A", "Tile_Neon_B" },
                new[] { "N_Pylon", "N_HoloCube", "N_GlowTotem", "N_Sign", "N_Pylon", "N_HoloCube" },
                null, new[] { "X_NeonBeam", "X_NeonPillar", "X_NeonBeam", "X_RollBarrel" },
                5.8f, 15f, 10, 0, "#1B1E3A");
            SetZone(zones.GetArrayElementAtIndex(10), "Gorge",
                null, null, new string[0], null, null,
                5.8f, 15f, 0, 0, "#A9C6B8",
                seq: new[] { "Tile_Zip_Entry", "Tile_Zip_Mid", "Tile_Zip_Mid",
                             "Tile_Zip_Mid", "Tile_Zip_Mid", "Tile_Zip_Mid", "Tile_Zip_Exit" },
                noTurns: true);

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(tm);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
    }
}
