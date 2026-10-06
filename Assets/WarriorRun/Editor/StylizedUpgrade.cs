using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;

namespace WarriorRun.EditorTools
{
    /// <summary>
    /// "Subway Surfers" restyle pass. Pairs with blender_lowpoly_assets.py, which
    /// re-authored every Art/Realistic/Models glTF as a chunky colorful low-poly
    /// prop in place (guids untouched, so all prefab/scene references survive).
    ///
    /// This pass finishes the look on the Unity side:
    ///   1. strips the PolyHaven/Nature3 PBR texture sets off the shared
    ///      materials and replaces them with saturated flat colors
    ///   2. restores the procedural skybox + trilight ambient (dropping the
    ///      9 MB HDRI from the build)
    ///   3. FlatColorBuildGuard re-asserts the palette at build preprocess, so
    ///      a mid-build material reimport can't ship stale colors
    ///
    /// Batch entry:  -executeMethod WarriorRun.EditorTools.StylizedUpgrade.RestyleAndBuildAndroid
    /// </summary>
    public static class StylizedUpgrade
    {
        const string Root = "Assets/WarriorRun";
        static readonly List<string> log = new();
        static void Log(string m) { log.Add(m); Debug.Log("[StylizedUpgrade] " + m); }

        static Color H(string h)
        {
            if (!h.StartsWith("#")) h = "#" + h;
            ColorUtility.TryParseHtmlString(h, out var c);
            return c;
        }

        // material name -> (flat color, smoothness). Covers every shared material
        // that previously carried a scanned/Quaternius texture set.
        internal static readonly Dictionary<string,(string hex,float smooth)> Flat = new()
        {
            // ground / terrain
            ["Grass"]       =("#6CC24A",0.12f), ["GrassDark"]  =("#57A83E",0.12f),
            ["PathDirt"]    =("#B98A5A",0.10f), ["PathSand"]   =("#E8C87E",0.10f),
            ["SandGround"]  =("#E0BE7C",0.08f), ["SandDune"]   =("#EBD09A",0.08f),
            ["CanyonBed"]   =("#D9A566",0.08f),
            ["RoadAsphalt"] =("#4A4F5C",0.18f), ["Sidewalk"]   =("#C9CDD4",0.22f),
            ["Curb"]        =("#9AA0A8",0.18f), ["Plaza"]      =("#B5BCC6",0.20f),
            ["Track"]       =("#D9BE8E",0.14f), ["TrackDark"]  =("#B7A077",0.12f),
            ["StoneDark"]   =("#6E6258",0.12f),
            ["MountainA"]   =("#C98A5A",0.12f), ["MountainB"]  =("#B7784E",0.12f),
            ["Rock"]        =("#A89E92",0.15f),
            ["Trunk"]       =("#8A5A32",0.10f),
            ["Wood"]        =("#C58A4A",0.25f), ["WoodDark"]   =("#9C6B38",0.25f),
            ["ObeliskSand"] =("#DCBB78",0.15f),
            ["PyramidSand"] =("#E3C078",0.14f), ["PyramidShade"]=("#C8A05C",0.12f),
            // Nat3_* mats (Quaternius texture swatches -> flat equivalents)
            ["Nat3_Grass_"]                =("#6CC24A",0.12f),
            ["Nat3_PineTree_Bark_"]        =("#7A5230",0.10f),
            ["Nat3_PineTree_Leaves_"]      =("#3E9142",0.12f),
            ["Nat3_PalmTree_Trunk_"]       =("#9A7B56",0.10f),
            ["Nat3_PalmTree_Leaves_"]      =("#4FAE7E",0.12f),
            ["Nat3_MapleTree_Bark_"]       =("#7A5230",0.10f),
            ["Nat3_MapleTree_Leaves_"]     =("#79D356",0.12f),
            ["Nat3_NormalTree_Bark_"]      =("#8A5A32",0.10f),
            ["Nat3_NormalTree_Leaves_"]    =("#57B845",0.12f),
            ["Nat3_BirchTree_Bark_"]       =("#D8D2C4",0.10f),
            ["Nat3_BirchTree_Leaves_"]     =("#A8D94E",0.12f),
            ["Nat3_Bush_Leaves_"]          =("#4FAE5F",0.12f),
            ["Nat3_Flowers_"]              =("#F2789F",0.12f),
            ["Nat3_Rock_"]                 =("#A89E92",0.15f),
            ["Nat3_Rock_Rocks_Dark_Green"] =("#6E8B52",0.15f),
            ["Nat3_Rock_Rocks_Desert"]     =("#D9A566",0.12f),
            ["Nat3_Rock_Rocks_Red_Desert"] =("#C07048",0.12f),
        };

        [MenuItem("WarriorRun/Stylized Restyle (flat colors)")]
        static void Menu() => Run();

        /// <summary>Batch entry: restyle, extra content (biomes/obstacles/beats),
        /// sanity-check prefab visuals, then the APK.</summary>
        public static void RestyleAndBuildAndroid()
        {
            Run();
            ExtraContent.Apply();
            ValidatePrefabs();
            WarriorRunBuilder.BuildAndroid();
        }

        /// <summary>
        /// Full pipeline: regenerate every prefab + scene from scratch (needed
        /// when builder-side content changes — e.g. turn-junction tiles), then
        /// restyle, re-apply extra content, validate and build the APK.
        ///   -executeMethod WarriorRun.EditorTools.StylizedUpgrade.FullRebuildAndroid
        /// </summary>
        [MenuItem("WarriorRun/Full Rebuild + Android APK")]
        public static void FullRebuildAndroid()
        {
            WarriorRunBuilder.Build();
            Run();
            ExtraContent.Apply();
            ValidatePrefabs();
            WarriorRunBuilder.BuildAndroid();
        }

        /// <summary>After the glTF re-export every prop prefab should still carry
        /// renderers with a sane world-space footprint. Logs any prefab whose
        /// visuals went missing or collapsed (broken instance overrides).</summary>
        static void ValidatePrefabs()
        {
            int bad = 0, checked_ = 0;
            foreach (var tp in Directory.GetFiles(Root + "/Prefabs", "*.prefab"))
            {
                string ap = tp.Replace('\\','/');
                var root = PrefabUtility.LoadPrefabContents(ap);
                try
                {
                    var rs = root.GetComponentsInChildren<Renderer>(true);
                    checked_++;
                    if (rs.Length == 0) { Log("NO RENDERERS: " + Path.GetFileName(ap)); bad++; continue; }
                    var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                    if (b.size.sqrMagnitude < 0.0001f || float.IsNaN(b.size.x))
                    { Log("DEGENERATE BOUNDS: " + Path.GetFileName(ap)); bad++; }
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            Log($"Prefab validation: {checked_} checked, {bad} bad");
        }

        public static void Run()
        {
            log.Clear();
            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                FlattenMaterials();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                FlattenMaterials();   // re-assert after pending imports flush — cheap and idempotent
                RestyleScenes();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Log("DONE");
            }
            catch (Exception e) { Log("FAILED: " + e); Debug.LogException(e); }
        }

        /// <summary>Shared by the editor pass and the build-preprocess guard:
        /// flat color + no texture refs on every stylized material.</summary>
        internal static int ApplyFlatColors()
        {
            int n = 0;
            foreach (var kv in Flat)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>($"{Root}/Materials/{kv.Key}.mat");
                if (m == null) continue;
                m.SetTexture("_BaseMap", null);
                m.SetTexture("_BumpMap", null);
                m.DisableKeyword("_NORMALMAP");
                m.SetColor("_BaseColor", H(kv.Value.hex));
                m.SetFloat("_Smoothness", kv.Value.smooth);
                EditorUtility.SetDirty(m);
                n++;
            }
            return n;
        }

        // ---------------- materials ----------------
        static void FlattenMaterials()
        {
            int n = ApplyFlatColors();
            // safety net: any other shared material still bound to a scan/Nature3
            // texture loses the texture but keeps its current base color
            foreach (var f in Directory.GetFiles(Root + "/Materials", "*.mat"))
            {
                string ap = f.Replace('\\','/');
                var m = AssetDatabase.LoadAssetAtPath<Material>(ap);
                if (m == null || !m.HasProperty("_BaseMap")) continue;
                var tex = m.GetTexture("_BaseMap");
                if (tex == null) continue;
                string tp = AssetDatabase.GetAssetPath(tex);
                if (tp.Contains("/Realistic/") || tp.Contains("/Nature3/"))
                {
                    m.SetTexture("_BaseMap", null);
                    m.SetTexture("_BumpMap", null);
                    m.DisableKeyword("_NORMALMAP");
                    EditorUtility.SetDirty(m);
                    Log("stripped stray tex: " + m.name);
                }
            }
            Log($"Materials flattened: {n}");
        }

        // ---------------- scenes ----------------
        static void RestyleScenes()
        {
            var sky = AssetDatabase.LoadAssetAtPath<Material>($"{Root}/Materials/Sky.mat");
            foreach (var sc in new[] { "Menu", "Game" })
            {
                string p = $"{Root}/Scenes/{sc}.unity";
                var scene = EditorSceneManager.OpenScene(p, OpenSceneMode.Single);
                if (sky != null) RenderSettings.skybox = sky;
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = H("A8D2E4");
                RenderSettings.ambientEquatorColor = H("F4E4C2");
                RenderSettings.ambientGroundColor = H("69795A");
                RenderSettings.ambientIntensity = 1.10f;
                var sun = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None)
                          .FirstOrDefault(l => l.type == LightType.Directional);
                if (sun != null)
                {
                    sun.intensity = Mathf.Max(sun.intensity, 1.3f);
                    sun.shadows = LightShadows.Soft;
                    sun.color = H("FFEDC9");
                    RenderSettings.sun = sun;
                }
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogStartDistance = 42f;
                RenderSettings.fogEndDistance = 150f;
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            Log("Scenes restyled (procedural sky + trilight ambient)");
        }
    }

    /// <summary>Last word on material colors before the build packages assets —
    /// reasserts the flat palette so a mid-build material reimport/normalize
    /// can't ship white surfaces.</summary>
    public class FlatColorBuildGuard : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;
        public void OnPreprocessBuild(BuildReport report)
        {
            int n = StylizedUpgrade.ApplyFlatColors();
            Debug.Log($"[StylizedUpgrade] build guard re-applied {n} flat colors");
        }
    }
}
