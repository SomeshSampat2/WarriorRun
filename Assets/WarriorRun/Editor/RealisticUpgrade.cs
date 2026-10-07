using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace WarriorRun.EditorTools
{
    /// <summary>
    /// Swaps the stylized (KayKit/Quaternius/procedural) visuals for scanned
    /// CC0 assets downloaded from PolyHaven into Art/Realistic/, upgrades the
    /// shared materials to PBR texture sets, and installs an HDRI sky.
    /// Idempotent — safe to re-run after "WarriorRun/Rebuild All".
    /// Batch entry:  -executeMethod WarriorRun.EditorTools.RealisticUpgrade.Run
    /// </summary>
    public static class RealisticUpgrade
    {
        const string Root = "Assets/WarriorRun";
        const string RealDir = Root + "/Art/Realistic";
        const string ModelDir = RealDir + "/Models";
        const string TexDir = RealDir + "/Textures";
        const string SkyDir = RealDir + "/Sky";
        static readonly List<string> log = new();
        static void Log(string m) { log.Add(m); Debug.Log("[RealisticUpgrade] " + m); }

        // ------------------------------------------------------------------
        // prop prefab -> model id.  hd_* = Blender HD pack (blender_hd_assets.py),
        // bl_* = older Blender pieces, rest = PolyHaven scans kept only as
        // fallbacks.  fit: H=equal height, W=equal width, B=fit inside
        // existing collider box.  lie: rotate model horizontal.
        // still: remove idle animation (dead props shouldn't breathe/teeter).
        // ------------------------------------------------------------------
        struct Swap { public string model; public string fb; public char fit; public float lie; public float yaw; public float lift; public bool still;
            public Swap(string m, char f='H', float l=0, float y=0, float up=0, string fb=null, bool still=false)
            { model=m; this.fb=fb; fit=f; lie=l; yaw=y; lift=up; this.still=still; } }

        static readonly Dictionary<string, Swap> PropMap = new()
        {
            // --- forest trees ---
            ["N_Pine1"]=new("hd_pine_a"), ["N_Pine2"]=new("hd_pine_b"),
            ["K_Pine"]=new("hd_pine_b"), ["K_Oak"]=new("hd_oak"),
            ["K_Tall"]=new("hd_pine_a"), ["N_Birch"]=new("hd_birch"),
            ["TreeA"]=new("hd_oak"), ["TreeB"]=new("hd_maple"),
            // --- palms / lagoon ---
            ["Q_PalmA"]=new("hd_palm_a"), ["Q_PalmB"]=new("hd_palm_b"),
            ["W_Palm1"]=new("hd_palm_a"), ["W_Palm2"]=new("hd_palm_b"),
            ["W_Palm3"]=new("hd_palm_a"),
            // --- dead wood / logs / stumps ---
            ["N_Dead"]=new("hd_dead"), ["Q_Dead"]=new("hd_dead"),
            ["Q_Dead2"]=new("hd_dead"),
            ["N_DeadFall"]=new("hd_log",'B',0,0,0,null,true),
            ["N_Stump"]=new("hd_stump",'B',0,0,0,null,true),
            ["Q_Stump"]=new("hd_stump",'B',0,0,0,null,true),
            ["N_Log"]=new("hd_log",'W',0,0,0,null,true),
            ["K_Log"]=new("hd_log",'W',0,0,0,null,true),
            ["Q_Log"]=new("hd_log",'W',0,0,0,null,true),
            ["LogBarrier"]=new("hd_log",'B',0,0,0,null,true),
            // --- rocks ---
            ["Rock"]=new("hd_boulder",'H',0,0,0,null,true),
            ["RockJump"]=new("hd_boulder",'B',0,0,0,null,true),
            ["RockBlock"]=new("hd_boulder_big",'B',0,0,0,null,true),
            ["N_Rock1"]=new("hd_boulder",'W',0,0,0,null,true),
            ["N_RockM"]=new("hd_rockpile",'W'),
            ["Q_RockA"]=new("hd_boulder",'W',0,0,0,null,true),
            ["Q_RockB"]=new("hd_rockpile",'W'),
            ["Q_RockD"]=new("hd_rock_desert",'W'), ["Q_RockD2"]=new("hd_rock_desert",'W'),
            ["W_Rock"]=new("hd_beachrock",'W'), ["D_Rubble"]=new("hd_rubble",'W'),
            ["K_Cliff"]=new("hd_mesa_a"), ["K_CliffTop"]=new("hd_mesa_b"),
            ["K_Cave"]=new("hd_rockspire"),
            // --- foliage ---
            ["Bush"]=new("hd_bush"), ["N_Bush"]=new("hd_bush"),
            ["K_Bush"]=new("hd_bush"), ["Q_Bush"]=new("hd_bush"),
            ["Q_BushS"]=new("hd_fern"), ["W_Bush"]=new("hd_bush"),
            ["C_Bush"]=new("hd_planter"), ["N_Plant"]=new("hd_fern"),
            ["Q_Plant"]=new("hd_fern"), ["Q_GrassL"]=new("hd_grass"),
            ["K_Grass"]=new("hd_grass"), ["Q_Flower"]=new("hd_flowers"),
            ["W_Flowers"]=new("hd_flowers"),
            // --- desert cactus ---
            ["N_Cactus"]=new("hd_saguaro"), ["N_CactusF"]=new("hd_barrelcactus"),
            ["K_Cactus"]=new("hd_saguaro"), ["K_CactusS"]=new("hd_barrelcactus"),
            ["CactusBlock"]=new("hd_barrelcactus",'B'),
            // --- city ---
            ["C_Hydrant"]=new("hd_hydrant"), ["C_TrashA"]=new("hd_trashcan"),
            ["C_TrashB"]=new("hd_trashcan"), ["C_Box"]=new("hd_crates"),
            ["C_Crates"]=new("hd_crates",'B',0,0,0,null,true),
            ["C_Dumpster"]=new("hd_dumpster",'B',0,0,0,null,true),
            ["C_Bench"]=new("hd_bench",'H',0,0,0,null,true),
            ["C_CarBlock"]=new("hd_sedan",'B',0,0,0,null,true),
            ["C_Tower"]=new("hd_streetlamp"),
            // --- shared obstacles ---
            ["LowBarrier"]=new("hd_boulder",'B',0,0,0,null,true),
            ["HighBarrier"]=new("hd_log",'B',0,0,0,null,true),
            ["WallBlock"]=new("hd_boulder_big",'B',0,0,0,null,true),
            ["SpikeTrap"]=new("hd_rockpile",'B',0,0,0,null,true),
            // --- temple / vault ---
            ["D_Barrel"]=new("hd_urn"), ["D_Keg"]=new("hd_urn"),
            ["D2_Trunk"]=new("hd_crates"), ["D_Crates"]=new("hd_crates"),
            ["D_Box"]=new("hd_crates"),
            ["D_RuinCol"]=new("hd_column_broken",'H',0,0,0,null,true),
            ["WallFeature_Torch"]=new("hd_brazier"),
            ["WF_Torch"]=new("hd_brazier"),
            // --- extra biomes ---
            ["F_PineSnow"]=new("hd_pine_snow"), ["F_PineSnowTall"]=new("hd_pine_snow_b"),
            ["F_IceShard"]=new("hd_icecrystal"), ["F_SnowRock"]=new("hd_frozenrock"),
            ["F_Snowman"]=new("hd_snowman"), ["F_SnowDrift"]=new("hd_snowdrift"),
            ["E_Obsidian"]=new("hd_obsidian"), ["E_CharredTrunk"]=new("hd_charred"),
            ["E_EmberCrag"]=new("hd_lavabomb"), ["E_Basalt"]=new("hd_basalt_hex"),
            ["N_Pylon"]=new("hd_neon_pylon"), ["N_HoloCube"]=new("hd_holo"),
            ["N_GlowTotem"]=new("hd_neon_shard"), ["N_Sign"]=new("hd_neon_sign"),
            ["X_IceCrystal"]=new("hd_icecrystal",'B'),
            ["X_SnowMound"]=new("hd_snowdrift",'B'),
            ["X_ObsidianSpike"]=new("hd_obsidian",'B'),
            ["X_NeonPillar"]=new("hd_neon_pylon",'B'),
        };

        // tile-embedded model instances, keyed by source-file stem (lower)
        static readonly (string key, string[] models, char fit, float lie)[] TileMap =
        {
            ("pinetree",   new[]{"hd_pine_a","hd_pine_b"}, 'H', 0),
            ("birchtree",  new[]{"hd_birch","hd_pine_b"}, 'H', 0),
            ("mapletree",  new[]{"hd_maple","hd_blossom"}, 'H', 0),
            ("normaltree", new[]{"hd_oak","hd_maple"}, 'H', 0),
            ("palmtree",   new[]{"hd_palm_a","hd_palm_b"}, 'H', 0),
            ("deadtree",   new[]{"hd_dead"}, 'H', 0),
            ("cliff",      new[]{"hd_mesa_a","hd_mesa_b","hd_rockspire"}, 'H', 0),
            ("rock",       new[]{"hd_boulder","hd_rockpile","hd_boulder_big"}, 'W', 0),
            ("bush",       new[]{"hd_bush","hd_fern"}, 'H', 0),
            ("grass",      new[]{"hd_grass"}, 'H', 0),
            ("flower",     new[]{"hd_flowers"}, 'H', 0),
            ("plant",      new[]{"hd_fern","hd_bush"}, 'H', 0),
            ("log",        new[]{"hd_log","hd_dead"}, 'W', 90),
            ("cactus",     new[]{"hd_saguaro","hd_barrelcactus"}, 'H', 0),
            ("firehydrant",new[]{"hd_hydrant"}, 'H', 0),
            ("trash_a",    new[]{"hd_trashcan"}, 'H', 0),
            ("trash_b",    new[]{"hd_trashcan"}, 'H', 0),
            ("streetlight",new[]{"hd_streetlamp"}, 'H', 0),
            ("trafficlight",new[]{"hd_trafficlight"}, 'H', 0),
            ("bench",      new[]{"hd_bench"}, 'H', 0),
            ("box_",       new[]{"hd_crates"}, 'H', 0),
            ("barrel",     new[]{"hd_urn"}, 'H', 0),
            ("keg",        new[]{"hd_urn"}, 'H', 0),
            ("rubble",     new[]{"hd_rubble"}, 'W', 0),
            ("crate",      new[]{"hd_crates"}, 'H', 0),
            ("trunk_large",new[]{"hd_crates"}, 'H', 0),
        };

        // shared material -> (texture id, tiling, tint, smoothness)
        static readonly Dictionary<string,(string tex,float tile,Color tint,float smooth)> MatMap = new()
        {
            ["Grass"]=("leafy_grass",4,W(0.95f),0.10f),
            ["GrassDark"]=("grass_path_2",4,W(0.9f),0.10f),
            ["PathDirt"]=("forest_floor",3,W(1f),0.10f),
            ["PathSand"]=("red_sand",3,W(1f),0.10f),
            ["SandGround"]=("sand_02",4,W(1f),0.08f),
            ["SandDune"]=("sand_02",6,H("E8D3AC"),0.08f),
            ["CanyonBed"]=("mud_cracked_dry_03",4,W(1f),0.08f),
            ["RoadAsphalt"]=("asphalt_02",4,W(1f),0.25f),
            ["Sidewalk"]=("concrete_pavement",3,W(1f),0.30f),
            ["Curb"]=("concrete_pavers",4,W(1f),0.25f),
            ["Plaza"]=("concrete_floor_worn_02",3,W(1f),0.30f),
            ["Track"]=("sandstone_cracks",3,W(1f),0.15f),
            ["TrackDark"]=("sandstone_cracks",3,W(0.8f),0.12f),
            ["StoneDark"]=("rock_wall_04",2,W(1f),0.10f),
            ["MountainA"]=("cliff_side",2,W(1f),0.10f),
            ["MountainB"]=("cliff_side",2,W(0.85f),0.10f),
            ["Rock"]=("rock_ground",1,W(1f),0.15f),
            ["Nat3_Rock_"]=("rock_ground",1,W(1f),0.15f),
            ["Nat3_Rock_Rocks_Dark_Green"]=("mossy_rock",1,W(1f),0.15f),
            ["Nat3_Rock_Rocks_Desert"]=("red_sand",1,W(1f),0.12f),
            ["Nat3_Rock_Rocks_Red_Desert"]=("red_sandstone_wall",1,W(1f),0.12f),
            ["Trunk"]=("bark_brown_01",1,W(1f),0.10f),
            ["Wood"]=("wood_planks",1,W(1f),0.30f),
            ["WoodDark"]=("wood_planks",1,W(0.65f),0.30f),
            ["ObeliskSand"]=("red_sandstone_wall",1,W(1f),0.18f),
            ["PyramidSand"]=("sandstone_blocks_04",2,W(1f),0.15f),
            ["PyramidShade"]=("sandstone_blocks_04",2,W(0.75f),0.12f),
        };
        static Color W(float v)=>new Color(v,v,v);
        static Color H(string h){ColorUtility.TryParseHtmlString("#"+h,out var c);return c;}

        [MenuItem("WarriorRun/Realistic Upgrade (PolyHaven)")]
        static void Menu() => Run();

        /// <summary>Runs once automatically if the sentinel file exists — lets a batch
        /// write the marker + drop this script in while the editor is already open.
        /// full_realistic_android.pending additionally builds the APK.</summary>
        const string Marker = "Assets/realistic_upgrade.pending";
        const string FullMarker = "Assets/full_realistic_android.pending";
        const string LightMarker = "Assets/light_build.pending";
        [InitializeOnLoadMethod]
        static void AutoRun()
        {
            EditorApplication.delayCall += () =>
            {
                try
                {
                    if (File.Exists(Marker)) { File.Delete(Marker); Run(); }
                    if (File.Exists(FullMarker)) { File.Delete(FullMarker); UpgradeAndBuildAndroid(); }
                    if (File.Exists(LightMarker)) { File.Delete(LightMarker); LightBuildAndroid(); }
                }
                catch (Exception e) { Debug.LogException(e); }
            };
        }

        /// <summary>Batch entry: swap giant photogrammetry meshes for light models,
        /// remove the unused scan sets, then build the APK. No scene/density changes.</summary>
        public static void LightBuildAndroid()
        {
            int n = ReplaceHeavyModels();
            foreach (var h in HeavyToLight.Keys)
                AssetDatabase.DeleteAsset($"{ModelDir}/{h}");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Log($"Light pass swapped {n} instances, removed {HeavyToLight.Count} scan sets");
            WarriorRunBuilder.BuildAndroid();
        }

        // multi-hundred-MB photogrammetry scans -> Blender HD stand-ins
        static readonly Dictionary<string,string> HeavyToLight = new()
        {
            ["pine_tree_01"]       = "hd_pine_a",
            ["fir_tree_01"]        = "hd_pine_b",
            ["jacaranda_tree"]     = "hd_blossom",
            ["island_tree_01"]     = "hd_oak",
            ["island_tree_03"]     = "hd_maple",
            ["tree_small_02"]      = "hd_dead",
            ["coast_rocks_02"]     = "hd_boulder",
            ["sand_rocks_small_01"]= "hd_rock_desert",
        };

        /// <summary>Find prefab instances of the giant scans inside every prefab
        /// (props and tiles) and swap them for the light replacement in place.</summary>
        static int ReplaceHeavyModels()
        {
            int swaps = 0;
            foreach (var tp in Directory.GetFiles(Root + "/Prefabs", "*.prefab"))
            {
                string ap = ToAssetPath(tp);
                var root = PrefabUtility.LoadPrefabContents(ap);
                try
                {
                    var hits = new List<(Transform tr,string stem)>();
                    foreach (var tr in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (!PrefabUtility.IsAnyPrefabInstanceRoot(tr.gameObject)) continue;
                        string srcPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(tr.gameObject);
                        if (string.IsNullOrEmpty(srcPath) || srcPath.EndsWith(".prefab")) continue;
                        string stem = Path.GetFileNameWithoutExtension(srcPath).ToLowerInvariant();
                        if (HeavyToLight.ContainsKey(stem)) hits.Add((tr,stem));
                    }
                    hits = hits.Where(h => !hits.Any(o => o.tr != h.tr && h.tr.IsChildOf(o.tr))).ToList();
                    foreach (var (tr,stem) in hits)
                    {
                        var msrc = LoadModel(HeavyToLight[stem]);
                        if (msrc == null) continue;
                        var parent = tr.parent;
                        var lp = tr.localPosition; var lr = tr.localRotation; var ls = tr.localScale;
                        Bounds oldB = VisualBounds(tr.gameObject);
                        UnityEngine.Object.DestroyImmediate(tr.gameObject);
                        var inst = (GameObject)PrefabUtility.InstantiatePrefab(msrc);
                        inst.name = HeavyToLight[stem];
                        inst.transform.SetParent(parent,false);
                        inst.transform.localPosition = lp;
                        inst.transform.localRotation = lr;
                        Place(inst, new Swap(inst.name,'H'), oldB, oldB.min.y, keepPos:true, oldScale:ls);
                        swaps++;
                    }
                    if (hits.Count > 0) PrefabUtility.SaveAsPrefabAsset(root, ap);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            Log($"Heavy instances replaced: {swaps}");
            return swaps;
        }

        /// <summary>Batch entry: shrink decor footprints that spill onto the lanes,
        /// then rebuild the APK. Run after the Blender decimation pass.</summary>
        public static void FixAndBuildAndroid()
        {
            int n = ClampDecorFootprints();
            RaiseDecorFloor();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Log($"Decor footprints clamped: {n}");
            WarriorRunBuilder.BuildAndroid();
        }

        /// <summary>Runtime decor spawns at zone.decorXMin; a low floor plus a wide
        /// scan can reach the lanes. Force every zone's floor up so the inward
        /// edge of spawned props stays behind the wall face (~3.8).</summary>
        static void RaiseDecorFloor()
        {
            string p = $"{Root}/Scenes/Game.unity";
            var scene = EditorSceneManager.OpenScene(p, OpenSceneMode.Single);
            var tm = UnityEngine.Object.FindFirstObjectByType<WarriorRun.World.TrackManager>();
            if (tm != null)
            {
                var so = new SerializedObject(tm);
                var zones = so.FindProperty("zones");
                if (zones != null && zones.isArray)
                    for (int i = 0; i < zones.arraySize; i++)
                    {
                        var xmin = zones.GetArrayElementAtIndex(i).FindPropertyRelative("decorXMin");
                        if (xmin != null && xmin.floatValue < 5.9f) xmin.floatValue = 5.9f;
                    }
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(tm);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
        }

        /// <summary>Scanned models are wide formations — their meshes reach metres
        /// past the pivot, so side-decor spawned at |x|=6.2 bleeds onto the lanes
        /// (wall face ~3.8). Prop prefabs: recentre XZ on pivot + clamp half-extent
        /// to 1.9m. Tile-embedded decor: clamp only the inward reach to |x|>=4.35.
        /// Obstacles are meant to block the road — skipped entirely.</summary>
        static int ClampDecorFootprints()
        {
            int fixed_ = 0;
            foreach (var tp in Directory.GetFiles(Root + "/Prefabs", "*.prefab"))
            {
                string ap = ToAssetPath(tp);
                bool tile = Path.GetFileName(ap).StartsWith("Tile_");
                var root = PrefabUtility.LoadPrefabContents(ap);
                try
                {
                    if (root.GetComponent<WarriorRun.World.Obstacle>() != null) continue;
                    bool dirty = false;
                    foreach (var tr in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (tr == null) continue;   // destroyed as a child of an earlier delete
                        if (!PrefabUtility.IsAnyPrefabInstanceRoot(tr.gameObject)) continue;
                        string srcPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(tr.gameObject);
                        if (string.IsNullOrEmpty(srcPath) || !srcPath.EndsWith(".gltf")) continue;

                        if (!tile)
                        {
                            var b = VisualBounds(tr.gameObject);
                            var p = tr.localPosition;
                            var shift = new Vector3(p.x - b.center.x, 0f, p.z - b.center.z);
                            if (shift.sqrMagnitude > 0.0004f) { tr.localPosition += shift; dirty = true; }
                            float half = Mathf.Max(Mathf.Abs(b.min.x - p.x), Mathf.Abs(b.max.x - p.x),
                                                   Mathf.Abs(b.min.z - p.z), Mathf.Abs(b.max.z - p.z));
                            if (half > 1.6f) { tr.localScale *= 1.6f / half; dirty = true; }
                            if (dirty) fixed_++;
                        }
                        else
                        {
                            float px = tr.localPosition.x;
                            float ax = Mathf.Abs(px);
                            if (ax < 4.4f)
                            {
                                // authored inside the lane corridor — a "decor"
                                // sitting on the road is a bug, drop it
                                UnityEngine.Object.DestroyImmediate(tr.gameObject);
                                fixed_++; dirty = true;
                                continue;
                            }
                            var b = VisualBounds(tr.gameObject);
                            // signed reach toward road centre — stays positive even
                            // when the mesh crosses the centre line entirely
                            float reach = px > 0 ? px - b.min.x : b.max.x - px;
                            float allowed = ax - 4.35f;
                            if (reach > allowed * 3f)
                            {
                                // spans the whole road (e.g. a 40m fallen trunk) —
                                // shrinking leaves a stump, delete instead
                                UnityEngine.Object.DestroyImmediate(tr.gameObject);
                                fixed_++; dirty = true;
                            }
                            else if (reach > allowed)
                            {
                                tr.localScale *= Mathf.Clamp(allowed / reach, 0.2f, 1f);
                                fixed_++; dirty = true;
                            }
                        }
                    }
                    if (dirty) PrefabUtility.SaveAsPrefabAsset(root, ap);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            Log($"Footprints clamped on {fixed_} decor instances");
            return fixed_;
        }

        /// <summary>Batch entry: full asset upgrade, then the Android APK.</summary>
        public static void UpgradeAndBuildAndroid()
        {
            Run();
            WarriorRunBuilder.BuildAndroid();
        }

        /// <summary>Batch entry: procedural rebuild (eyes-free) + realistic swap + APK.</summary>
        public static void FullRealisticAndroid()
        {
            WarriorRunBuilder.Build();
            Run();
            WarriorRunBuilder.BuildAndroid();
        }

        public static void Run()
        {
            log.Clear();
            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                FixTextureImporters();
                UpgradeMaterials();
                int nProps = UpgradePropPrefabs();
                int nTiles = UpgradeTilePrefabs();
                CreateConeObstacle();
                StripEyesEverywhere();
                UpgradeScenes();
                EnableSSAO();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Log($"DONE — {nProps} prop prefabs, {nTiles} tile instances swapped");
            }
            catch (Exception e) { Log("FAILED: " + e); Debug.LogException(e); }
        }

        // ---------------- textures ----------------
        static void FixTextureImporters()
        {
            foreach (var f in Directory.GetFiles(TexDir,"*.*",SearchOption.AllDirectories)
                         .Concat(Directory.GetFiles(ModelDir,"*.*",SearchOption.AllDirectories)))
            {
                string ap = ToAssetPath(f);
                var ti = AssetImporter.GetAtPath(ap) as TextureImporter;
                if (ti == null) continue;
                bool dirty = false;
                bool dataMap = f.Contains("_nor_") || f.Contains("_rough") || f.Contains("_arm_")
                            || f.Contains("_disp") || f.Contains("_mask") || f.Contains("_metal");
                if (f.Contains("_nor_") && ti.textureType != TextureImporterType.NormalMap)
                { ti.textureType = TextureImporterType.NormalMap; dirty = true; }
                if (ti.sRGBTexture == dataMap) { ti.sRGBTexture = !dataMap; dirty = true; }
                if (ti.maxTextureSize > 1024) { ti.maxTextureSize = 1024; dirty = true; }
                if (!ti.mipmapEnabled) { ti.mipmapEnabled = true; dirty = true; }
                if (dirty) { ti.SaveAndReimport(); }
            }
            Log("Texture importers fixed");
        }

        static void UpgradeMaterials()
        {
            foreach (var kv in MatMap)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>($"{Root}/Materials/{kv.Key}.mat");
                if (m == null) { Log("mat missing: " + kv.Key); continue; }
                var (tex,tile,tint,smooth) = kv.Value;
                var diff = LoadTex(tex,"Diffuse"); var nor = LoadTex(tex,"nor_gl");
                if (diff == null) { Log("tex missing: " + tex); continue; }
                m.SetTexture("_BaseMap", diff);
                if (nor != null) { m.SetTexture("_BumpMap", nor); m.EnableKeyword("_NORMALMAP"); }
                m.SetTextureScale("_BaseMap", new Vector2(tile,tile));
                m.SetTextureScale("_BumpMap", new Vector2(tile,tile));
                m.SetColor("_BaseColor", tint);
                m.SetFloat("_Smoothness", smooth);
                EditorUtility.SetDirty(m);
            }
            // water likes specular
            foreach (var n in new[]{"WaterDeep","PondBlue","SlideFloor","SlideWall"})
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>($"{Root}/Materials/{n}.mat");
                if (m){ m.SetFloat("_Smoothness",0.9f); EditorUtility.SetDirty(m);}
            }
            Log("Materials upgraded");
        }

        static Texture2D LoadTex(string tex,string kind)
        {
            foreach (var ext in new[]{"jpg","png"})
            {
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{tex}/{tex}_{kind}_1k.{ext}");
                if (t != null) return t;
            }
            // model textures sometimes carry the map under a different suffix
            return null;
        }

        // ---------------- prefab swaps ----------------
        static int UpgradePropPrefabs()
        {
            int done = 0;
            foreach (var kv in PropMap)
            {
                string path = $"{Root}/Prefabs/{kv.Key}.prefab";
                if (!File.Exists(path)) continue;
                var src = LoadModel(kv.Value.model) ??
                          (kv.Value.fb != null ? LoadModel(kv.Value.fb) : null);
                if (src == null) { Log("model missing: " + kv.Value.model); continue; }

                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    // target volume: collider box for obstacles, else old visual bounds
                    Bounds? box = ColliderBox(root);
                    Bounds old = VisualBounds(root);
                    StripVisualChildren(root);
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
                    inst.transform.SetParent(root.transform,false);
                    Place(inst, kv.Value, box ?? old, old.min.y);
                    KillLife(root, kv.Value.still);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    done++;
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            Log($"Prop prefabs swapped: {done}");
            return done;
        }

        static int UpgradeTilePrefabs()
        {
            int swaps = 0;
            var tiles = Directory.GetFiles(Root + "/Prefabs","Tile_*.prefab");
            foreach (var tp in tiles)
            {
                string ap = ToAssetPath(tp);
                bool city = tp.Contains("Tile_City");
                var root = PrefabUtility.LoadPrefabContents(ap);
                int tileSwaps = 0;
                try
                {
                    // collect deepest model-prefab instances whose stem we can map
                    var hits = new List<(Transform tr,string stem)>();
                    foreach (var tr in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (!PrefabUtility.IsAnyPrefabInstanceRoot(tr.gameObject)) continue;
                        string srcPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(tr.gameObject);
                        if (string.IsNullOrEmpty(srcPath) || srcPath.EndsWith(".prefab")) continue;
                        string stem = Path.GetFileNameWithoutExtension(srcPath).ToLowerInvariant();
                        if (TileMap.Any(t => stem.Contains(t.key))) hits.Add((tr,stem));
                    }
                    // keep only outermost matches so destroying a parent can't orphan a queued child
                    hits = hits.Where(h => !hits.Any(o => o.tr != h.tr && h.tr.IsChildOf(o.tr))).ToList();
                    foreach (var (tr,stem) in hits)
                    {
                        var rule = TileMap.First(t => stem.Contains(t.key));
                        var models = rule.models;
                        if (city && stem == "bush") models = new[]{"planter_box_01"};
                        var pick = models[Math.Abs(tr.GetSiblingIndex()*31 + (int)(tr.localPosition.x*7)) % models.Length];
                        var msrc = LoadModel(pick);
                        if (msrc == null) continue;
                        var parent = tr.parent;
                        var lp = tr.localPosition; var lr = tr.localRotation;
                        Bounds oldB = VisualBounds(tr.gameObject);
                        UnityEngine.Object.DestroyImmediate(tr.gameObject);
                        var inst = (GameObject)PrefabUtility.InstantiatePrefab(msrc);
                        inst.name = pick;
                        inst.transform.SetParent(parent,false);
                        inst.transform.localPosition = lp;
                        inst.transform.localRotation = lr;
                        Place(inst, new Swap(pick, rule.fit, rule.lie), oldB, oldB.min.y, keepPos:true);
                        tileSwaps++;
                    }
                    if (tileSwaps > 0) PrefabUtility.SaveAsPrefabAsset(root, ap);
                    swaps += tileSwaps;
                    if (tileSwaps > 0) Log($"{Path.GetFileName(ap)}: {tileSwaps} swaps");
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            Log($"Tile instances swapped: {swaps}");
            return swaps;
        }

        // ---------------- scenes / rendering ----------------
        static void UpgradeScenes()
        {
            var sky = MakeSkyMaterial();
            foreach (var sc in new[]{"Menu","Game"})
            {
                string p = $"{Root}/Scenes/{sc}.unity";
                var scene = EditorSceneManager.OpenScene(p, OpenSceneMode.Single);
                if (sky != null) { RenderSettings.skybox = sky; RenderSettings.ambientMode = AmbientMode.Skybox; }
                var sun = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None)
                          .FirstOrDefault(l => l.type == LightType.Directional);
                if (sun != null)
                {
                    sun.intensity = Mathf.Max(sun.intensity, 1.2f);
                    sun.shadows = LightShadows.Soft;
                    sun.color = new Color(1f, 0.96f, 0.88f);
                    RenderSettings.sun = sun;
                }
                if (sc == "Game") DensifyZones();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            TuneVolume();
            Log("Scenes upgraded");
        }

        /// <summary>Raise per-zone decor spawn counts so the realistic world reads dense,
        /// not empty. Widens the scatter band a little too.</summary>
        static void DensifyZones()
        {
            var tm = UnityEngine.Object.FindFirstObjectByType<WarriorRun.World.TrackManager>();
            if (tm == null) { Log("TrackManager not found in Game scene"); return; }
            var so = new SerializedObject(tm);
            var zones = so.FindProperty("zones");
            if (zones == null || !zones.isArray) return;
            for (int i = 0; i < zones.arraySize; i++)
            {
                var z = zones.GetArrayElementAtIndex(i);
                var dc = z.FindPropertyRelative("decorCount");
                var xmax = z.FindPropertyRelative("decorXMax");
                var fc = z.FindPropertyRelative("featureCount");
                if (dc != null) dc.intValue = Mathf.CeilToInt(dc.intValue * 1.6f) + 4;
                if (fc != null && fc.intValue > 0) fc.intValue += 2;
                if (xmax != null) xmax.floatValue = Mathf.Min(xmax.floatValue + 2f, 16f);
                // city gets the Blender traffic cone mixed into its obstacle pool
                var zn = z.FindPropertyRelative("name");
                if (zn != null && zn.stringValue == "City")
                {
                    var cone = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/C_Cone.prefab");
                    var ops = z.FindPropertyRelative("obstaclePrefabs");
                    if (cone != null && ops != null && ops.isArray)
                        for (int k = 0; k < 2; k++)
                        {
                            ops.InsertArrayElementAtIndex(ops.arraySize);
                            ops.GetArrayElementAtIndex(ops.arraySize - 1).objectReferenceValue = cone;
                        }
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(tm);
            Log("Zones densified");
        }

        static Material MakeSkyMaterial()
        {
            var hdr = AssetDatabase.LoadAssetAtPath<Texture2D>($"{SkyDir}/kloppenheim_02_puresky.hdr");
            if (hdr == null) { Log("no hdri found"); return null; }
            var ti = AssetImporter.GetAtPath($"{SkyDir}/kloppenheim_02_puresky.hdr") as TextureImporter;
            if (ti != null && ti.maxTextureSize < 4096) { ti.maxTextureSize = 4096; ti.SaveAndReimport(); }
            string mp = $"{Root}/Materials/Sky_HDRI.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
            if (m == null) { m = new Material(Shader.Find("Skybox/Panoramic")){name="Sky_HDRI"}; AssetDatabase.CreateAsset(m,mp); }
            m.SetTexture("_MainTex", hdr);
            m.SetFloat("_Mapping", 1f);      // latitude-longitude layout
            m.SetFloat("_ImageType", 0f);
            m.SetFloat("_Exposure", 1.1f);
            m.SetFloat("_Rotation", 205f);
            EditorUtility.SetDirty(m);
            return m;
        }

        static void TuneVolume()
        {
            string vp = Root + "/WarriorRunVolume.asset";
            var prof = AssetDatabase.LoadAssetAtPath<VolumeProfile>(vp);
            if (prof == null) { Log("volume profile missing"); return; }
            if (!prof.TryGet(out ColorAdjustments ca)) ca = prof.Add<ColorAdjustments>();
            ca.postExposure.Override(0.25f); ca.contrast.Override(10f); ca.saturation.Override(6f);
            if (!prof.TryGet(out Bloom bl)) bl = prof.Add<Bloom>();
            bl.threshold.Override(1.05f); bl.intensity.Override(0.45f); bl.scatter.Override(0.6f);
            if (!prof.TryGet(out Vignette vg)) vg = prof.Add<Vignette>();
            vg.intensity.Override(0.16f);
            if (!prof.TryGet(out WhiteBalance wb)) wb = prof.Add<WhiteBalance>();
            wb.temperature.Override(6f);
            EditorUtility.SetDirty(prof);
        }

        static void EnableSSAO()
        {
            try
            {
                foreach (var r in new[]{"Mobile_Renderer","PC_Renderer"})
                {
                    var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>($"Assets/Settings/{r}.asset");
                    if (rd == null) continue;
                    if (rd.rendererFeatures.Any(f => f != null && f.GetType().Name.Contains("AmbientOcclusion"))) continue;
                    var t = typeof(UniversalRendererData).Assembly.GetType(
                        "UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion");
                    if (t == null) continue;
                    var f = (ScriptableRendererFeature)ScriptableObject.CreateInstance(t);
                    f.name = "ScreenSpaceAmbientOcclusion";
                    var s = t.GetField("m_Settings", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)
                         ?? t.GetField("settings", System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
                    if (s != null)
                    {
                        var st = s.GetValue(f);
                        st.GetType().GetField("Intensity")?.SetValue(st, 1.4f);
                        st.GetType().GetField("Radius")?.SetValue(st, 0.6f);
                        st.GetType().GetField("Downsample")?.SetValue(st, true);
                        s.SetValue(f, st);
                    }
                    AssetDatabase.AddObjectToAsset(f, rd);
                    rd.rendererFeatures.Add(f);
                    EditorUtility.SetDirty(rd);
                }
                // depth texture is required for SSAO
                foreach (var rp in new[]{"Mobile_RPAsset","PC_RPAsset"})
                {
                    var a = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>($"Assets/Settings/{rp}.asset");
                    if (a == null) continue;
                    var so = new SerializedObject(a);
                    var p = so.FindProperty("m_RequireDepthTexture");
                    if (p != null) { p.boolValue = true; so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(a); }
                }
                Log("SSAO enabled");
            }
            catch (Exception e) { Log("SSAO skipped: " + e.Message); }
        }

        // ---------------- helpers ----------------
        static GameObject LoadModel(string id)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelDir}/{id}/{id}.gltf");
            return go;
        }

        static Bounds VisualBounds(GameObject root)
        {
            var rs = root.GetComponentsInChildren<Renderer>(true)
                     .Where(r => !IsEye(r.transform) && !(r is ParticleSystemRenderer)).ToArray();
            if (rs.Length == 0) return new Bounds(root.transform.position, Vector3.one);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
        }

        static Bounds? ColliderBox(GameObject root)
        {
            var cs = root.GetComponentsInChildren<Collider>(true);
            // obstacles keep their kill volume on a trigger — prefer solid
            // colliders for decor, but the trigger box is the fit target
            // when it's the only collider the prefab has.
            var solid = cs.Where(c => !c.isTrigger).ToArray();
            var use = solid.Length > 0 ? solid : cs;
            if (use.Length == 0) return null;
            var b = use[0].bounds; foreach (var c in use) b.Encapsulate(c.bounds); return b;
        }

        static bool IsEye(Transform t)
        {
            for (var x = t; x != null; x = x.parent)
            {
                var n = x.name.ToLowerInvariant();
                if (n.Contains("eye") || n.Contains("pupil")) return true;
            }
            return false;
        }

        static void StripVisualChildren(GameObject root)
        {
            var kill = new List<GameObject>();
            foreach (Transform c in root.transform)
            {
                bool isModelInstance = PrefabUtility.IsAnyPrefabInstanceRoot(c.gameObject);
                bool hasVisual = c.GetComponentsInChildren<Renderer>(true).Length > 0;
                if (isModelInstance || hasVisual) kill.Add(c.gameObject);
            }
            foreach (var k in kill) UnityEngine.Object.DestroyImmediate(k);
        }

        /// <summary>Scale + ground the new model inside the reference bounds.</summary>
        static void Place(GameObject inst, Swap sw, Bounds target, float groundY, bool keepPos=false, Vector3 oldScale=default)
        {
            if (sw.lie != 0 || sw.yaw != 0)
                inst.transform.localRotation = inst.transform.localRotation * Quaternion.Euler(sw.lie, sw.yaw, 0);
            var nb = VisualBounds(inst);
            float ratio =
                sw.fit == 'H' ? SafeDiv(target.size.y, nb.size.y) :
                sw.fit == 'W' ? SafeDiv(Mathf.Max(target.size.x,target.size.z), Mathf.Max(nb.size.x,nb.size.z)) :
                Mathf.Min(SafeDiv(target.size.x,nb.size.x), SafeDiv(target.size.y,nb.size.y), SafeDiv(target.size.z,nb.size.z));
            if (ratio <= 0 || float.IsNaN(ratio) || float.IsInfinity(ratio)) ratio = 1f;
            inst.transform.localScale *= ratio;
            nb = VisualBounds(inst);
            // sit base where the old visual's base was; center horizontally on it
            var p = inst.transform.localPosition;
            float cx = keepPos ? p.x : p.x - (nb.center.x - inst.transform.position.x);
            float cz = keepPos ? p.z : p.z - (nb.center.z - inst.transform.position.z);
            inst.transform.localPosition = new Vector3(cx, p.y + (groundY - nb.min.y) + sw.lift, cz);
        }
        static float SafeDiv(float a,float b)=> Mathf.Abs(b)<1e-4f? 1f : a/b;

        /// <summary>Photorealistic props are dead props: delete leftover eye
        /// objects + the ObstacleEyes component; `still` also kills the idle
        /// ObstacleAnim so rocks and crates stop breathing and teetering.</summary>
        static void KillLife(GameObject root, bool still)
        {
            StripEyeComponents(root);
            // strays: builder eyes are direct children named EyeL/EyeR/Pupil
            var kill = new List<GameObject>();
            foreach (Transform c in root.transform)
                if (IsEye(c)) kill.Add(c.gameObject);
            foreach (var k in kill) UnityEngine.Object.DestroyImmediate(k);
            if (still)
                foreach (var a in root.GetComponentsInChildren<WarriorRun.World.ObstacleAnim>(true))
                    UnityEngine.Object.DestroyImmediate(a);
        }

        /// <summary>Destroy the eye transforms an ObstacleEyes component points at,
        /// then the component itself. Only touches real googly-eye rigs — a
        /// character's own "EyeL/EyeR" face cubes are safe (no component).</summary>
        static void StripEyeComponents(GameObject root)
        {
            foreach (var e in root.GetComponentsInChildren<WarriorRun.World.ObstacleEyes>(true))
            {
                foreach (var t in new[] { e.eyeL, e.eyeR, e.pupilL, e.pupilR })
                    if (t != null) UnityEngine.Object.DestroyImmediate(t.gameObject);
                UnityEngine.Object.DestroyImmediate(e);
            }
        }

        /// <summary>All prefabs (even ones PropMap doesn't touch) lose their
        /// googly-eye rigs.</summary>
        static void StripEyesEverywhere()
        {
            foreach (var p in Directory.GetFiles(Root + "/Prefabs", "*.prefab"))
            {
                var root = PrefabUtility.LoadPrefabContents(ToAssetPath(p));
                try
                {
                    if (root.GetComponentsInChildren<WarriorRun.World.ObstacleEyes>(true).Length == 0)
                        continue;
                    StripEyeComponents(root);
                    PrefabUtility.SaveAsPrefabAsset(root, ToAssetPath(p));
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
        }

        /// <summary>Traffic cone obstacle for the city zone: clone LowBarrier's
        /// trigger setup, drop the Blender cone in, shrink the kill box to the
        /// cone's real footprint, save as C_Cone.prefab.</summary>
        static void CreateConeObstacle()
        {
            var src = LoadModel("bl_traffic_cone");
            if (src == null) { Log("bl_traffic_cone missing — C_Cone skipped"); return; }
            string lp = Root + "/Prefabs/LowBarrier.prefab";
            string cp = Root + "/Prefabs/C_Cone.prefab";
            if (!File.Exists(lp)) return;
            var root = PrefabUtility.LoadPrefabContents(lp);
            try
            {
                root.name = "C_Cone";
                StripVisualChildren(root);
                KillLife(root, still: true);
                var col = root.GetComponentInChildren<BoxCollider>();
                if (col != null)
                {
                    col.size = new Vector3(0.9f, 0.95f, 0.9f);
                    col.center = new Vector3(0f, 0.48f, 0f);
                }
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
                inst.transform.SetParent(root.transform, false);
                Bounds box = col != null ? col.bounds
                    : new Bounds(new Vector3(0f, 0.45f, 0f), new Vector3(0.9f, 0.9f, 0.9f));
                Place(inst, new Swap("bl_traffic_cone", 'B'), box, 0f);
                PrefabUtility.SaveAsPrefabAsset(root, cp);
                Log("C_Cone.prefab created");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        static string ToAssetPath(string abs)
        {
            abs = abs.Replace('\\','/');
            int i = abs.IndexOf("/Assets/", StringComparison.Ordinal);
            return i >= 0 ? abs.Substring(i+1) : abs;
        }
    }
}

//
