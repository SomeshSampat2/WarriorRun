using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Events;
using UnityEditor.Build;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using TMPro;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor.Animations;
using WarriorRun.Core;
using WarriorRun.World;
using WarriorRun.Player;
using WarriorRun.Input;
using WarriorRun.UI;

namespace WarriorRun.EditorTools
{
    /// <summary>
    /// One-shot procedural builder for Warrior Run. Invoked via pipeline eval:
    ///   return WarriorRun.EditorTools.WarriorRunBuilder.Build();
    /// Generates meshes, materials, sprites, audio, prefabs, both scenes and
    /// project settings — no imported art assets required.
    /// </summary>
    public static class WarriorRunBuilder
    {
        const string Root = "Assets/WarriorRun";
        static readonly Dictionary<string, Material> mats = new();
        static readonly Dictionary<string, GameObject> prefabs = new();
        static readonly List<string> logLines = new();

        // ---------- palette ----------
        static readonly Color ColSkyTop = Hex("#5FA8D3");
        static readonly Color ColHorizon = Hex("#F4E3C0");
        static readonly Color ColTrack = Hex("#9B8A76");
        static readonly Color ColTrackDark = Hex("#8A7A66");
        static readonly Color ColCurb = Hex("#6E5F4F");
        static readonly Color ColGrass = Hex("#7CB350");
        static readonly Color ColGrassDark = Hex("#689D42");
        static readonly Color ColTrunk = Hex("#7A5230");
        static readonly Color ColLeafA = Hex("#3E8948");
        static readonly Color ColLeafB = Hex("#57A04F");
        static readonly Color ColRock = Hex("#8D8D96");
        static readonly Color ColCoin = Hex("#FFC93C");
        static readonly Color ColWood = Hex("#9C6B3C");
        static readonly Color ColWoodDark = Hex("#7A5230");
        static readonly Color ColStoneDark = Hex("#5E5148");
        static readonly Color ColStripe = Hex("#E4572E");
        static readonly Color ColShirt = Hex("#FF6B4A");
        static readonly Color ColPants = Hex("#2E3A48");
        static readonly Color ColSkin = Hex("#F0C8A0");
        static readonly Color ColShoe = Hex("#F5F5F5");
        static readonly Color ColCap = Hex("#FF6B4A");
        static readonly Color ColUIPanel = Hex("#18242F");
        static readonly Color ColUIAccent = Hex("#FFB43A");
        static readonly Color ColUIText = Hex("#F5F7FA");
        static readonly Color ColMagic = Hex("#B47CFF");
        static readonly Color ColMagicDeep = Hex("#2A1B4E");
        static readonly Color ColStar = Hex("#FFE9A8");
        static readonly Color ColStarCyan = Hex("#9FE8FF");

        static TMP_FontAsset gameFont, titleFont;
        static Sprite roundRect, circle, coinIcon;
        static Sprite cardSpr, btnSpr, pillSpr, softPuff, scrimSpr, starSpr;
        static readonly Dictionary<string, AnimationClip> clips = new();
        static RuntimeAnimatorController runnerCtrl;
        static VolumeProfile volProfile;
        static GameObject coinBurstPrefab;
        static Material dustMat;
        const string ArtDir = Root + "/Art";
        const string CharGlb = ArtDir + "/CharNew/Knight.glb";

        /// <summary>Editor menu entry: WarriorRun ▸ Rebuild All regenerates scenes + assets.</summary>
        [MenuItem("WarriorRun/Rebuild All")]
        static void RebuildMenu() => Build();

        /// <summary>Batch-mode APK build: honors -buildOutput from `unity build -o`.</summary>
        public static void BuildAndroid()
        {
            // GLES3 only — Vulkan hits driver-specific flicker artifacts on
            // some real GPUs; GLES3 is universal and cheap enough here
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,
                new[] { UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 });
            string outPath = "Builds/Android/WarriorRun.apk";
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-buildOutput") outPath = args[i + 1];
            var scenes = new[] { Root + "/Scenes/Menu.unity", Root + "/Scenes/Game.unity" };
            var report = BuildPipeline.BuildPlayer(scenes, outPath, BuildTarget.Android, BuildOptions.None);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new Exception("APK build failed: " + report.summary.result + " errors=" + report.summary.totalErrors);
            Log("APK -> " + outPath + " (" + report.summary.totalSize + " bytes)");
        }

        /// <summary>Development APK — script errors and logs flow to logcat. Diagnostics only.</summary>
        public static void BuildAndroidDev()
        {
            var scenes = new[] { Root + "/Scenes/Menu.unity", Root + "/Scenes/Game.unity" };
            var report = BuildPipeline.BuildPlayer(scenes, "Builds/Android/WarriorRun-dev.apk",
                BuildTarget.Android, BuildOptions.Development);
            Log("DEV APK -> Builds/Android/WarriorRun-dev.apk result=" + report.summary.result);
        }

        /// <summary>Batch entry: full procedural rebuild, then the APK — one shot.</summary>
        public static void RebuildAndBuildAndroid()
        {
            Build();
            BuildAndroid();
        }

        public static string Build()
        {
            logLines.Clear();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                // pick up any art dropped in since the last refresh
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                EnsureFolders();
                CreateMeshes();
                CreateMaterials();
                GenerateSprites();
                GenerateSpritesV2();
                GenerateFont();
                GenerateTitleFont();
                GenerateAudio();
                ExtractClips();
                CreateCharMaterials();
                CreateAnimatorController();
                CreateFx();
                CreateVolumeProfile();
                // Commit + import everything generated above so prefab
                // serialization resolves live references, not pending imports.
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                CreatePrefabs();
                BuildMenuScene();
                BuildGameScene();
                ConfigureProject();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Log("DONE in " + sw.ElapsedMilliseconds + " ms");
            }
            catch (Exception e)
            {
                Log("FAILED: " + e);
            }
            return string.Join("\n", logLines);
        }

        static void Log(string m) { logLines.Add(m); Debug.Log("[WarriorRun] " + m); }

        static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

        // ================= folders =================

        static void EnsureFolders()
        {
            foreach (var f in new[] { "Materials", "Meshes", "Prefabs", "Scenes", "Sprites", "Fonts", "Resources", "Resources/Audio", "Animations" })
                EnsureFolder(Root + "/" + f);
            Log("Folders ready");
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        // ================= meshes =================

        static Mesh coneMesh, rockMesh, pyramidMesh;

        static void CreateMeshes()
        {
            coneMesh = SaveMesh(MakeCone(5, 1f, 1f), "Cone5", Root + "/Meshes/Cone5.asset");
            rockMesh = SaveMesh(MakeRock(1234), "Rock", Root + "/Meshes/Rock.asset");
            pyramidMesh = SaveMesh(MakeCone(4, 1f, 1f), "Pyramid4", Root + "/Meshes/Pyramid4.asset");
            Log("Meshes created");
        }

        /// <summary>Write the mesh to an asset (in place, preserving GUID) and return the asset reference.</summary>
        static Mesh SaveMesh(Mesh fresh, string name, string path)
        {
            var m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (m == null)
            {
                fresh.name = name;
                AssetDatabase.CreateAsset(fresh, path);
                return fresh;
            }
            m.Clear();
            m.vertices = fresh.vertices;
            m.triangles = fresh.triangles;
            m.RecalculateNormals();
            m.RecalculateBounds();
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Faceted n-gon pyramid cone, base radius r, height h, pivot at base center.</summary>
        static Mesh MakeCone(int sides, float r, float h)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();

            var apex = new Vector3(0, h, 0);
            var ring = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides + Mathf.PI / sides;
                ring[i] = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
            }

            // sides — duplicated verts for flat shading; (a, apex, b) faces outward
            for (int i = 0; i < sides; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % sides];
                int v = verts.Count;
                verts.Add(a); verts.Add(apex); verts.Add(b);
                tris.Add(v); tris.Add(v + 1); tris.Add(v + 2);
            }
            // base fan faces down
            for (int i = 1; i < sides - 1; i++)
            {
                int v = verts.Count;
                verts.Add(ring[0]); verts.Add(ring[i]); verts.Add(ring[i + 1]);
                tris.Add(v); tris.Add(v + 1); tris.Add(v + 2);
            }

            var m = new Mesh { name = "Cone5" };
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Jittered icosahedron — a believable low-poly rock.</summary>
        static Mesh MakeRock(int seed)
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var baseVerts = new List<Vector3>
            {
                new(-1,t,0), new(1,t,0), new(-1,-t,0), new(1,-t,0),
                new(0,-1,t), new(0,1,t), new(0,-1,-t), new(0,1,-t),
                new(t,0,-1), new(t,0,1), new(-t,0,-1), new(-t,0,1)
            };
            int[] faces =
            {
                0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11,
                1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9,
                4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1
            };

            var rng = new System.Random(seed);
            var jittered = new Vector3[baseVerts.Count];
            for (int i = 0; i < baseVerts.Count; i++)
            {
                var v = baseVerts[i].normalized;
                float s = 0.72f + (float)rng.NextDouble() * 0.45f;
                jittered[i] = new Vector3(v.x * s, v.y * s * 0.8f, v.z * s);
            }

            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int f = 0; f < faces.Length; f += 3)
            {
                verts.Add(jittered[faces[f]]);
                verts.Add(jittered[faces[f + 1]]);
                verts.Add(jittered[faces[f + 2]]);
                tris.Add(f); tris.Add(f + 1); tris.Add(f + 2);
            }

            var m = new Mesh { name = "Rock" };
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        static void SaveAsset(UnityEngine.Object o, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (existing != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(o, path);
        }

        // ================= materials =================

        static Material Mat(string name, Color color, float smooth = 0.25f, float metallic = 0f, bool emission = false)
        {
            string path = Root + "/Materials/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metallic);
            if (emission)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", color * 0.55f);
            }
            EditorUtility.SetDirty(m);
            mats[name] = m;
            return m;
        }

        static void CreateMaterials()
        {
            Mat("Track", ColTrack, 0.18f);
            Mat("TrackDark", ColTrackDark, 0.15f);
            Mat("Curb", ColCurb, 0.2f);
            Mat("Grass", ColGrass, 0.15f);
            Mat("GrassDark", ColGrassDark, 0.15f);
            Mat("Trunk", ColTrunk, 0.2f);
            Mat("LeafA", ColLeafA, 0.2f);
            Mat("LeafB", ColLeafB, 0.2f);
            Mat("Rock", ColRock, 0.3f);
            Mat("Coin", ColCoin, 0.75f, 0.6f, true);
            Mat("Wood", ColWood, 0.25f);
            Mat("WoodDark", ColWoodDark, 0.25f);
            Mat("StoneDark", ColStoneDark, 0.2f);
            Mat("Stripe", ColStripe, 0.3f, 0f, true);
            Mat("Shirt", ColShirt, 0.3f);
            Mat("Pants", ColPants, 0.3f);
            Mat("Skin", ColSkin, 0.35f);
            Mat("Shoe", ColShoe, 0.35f);
            Mat("Cap", ColCap, 0.3f);
            Mat("PathDirt", Hex("#6E5236"), 0.12f);
            Mat("PathSand", Hex("#CFA873"), 0.12f);
            Mat("SandGround", Hex("#B8935E"), 0.12f);
            Mat("CanyonBed", Hex("#C69C62"), 0.12f);
            // city zone — asphalt road, pale sidewalks, plaza bed, lane paint
            Mat("RoadAsphalt", Hex("#3B3F4A"), 0.10f);
            Mat("Sidewalk", Hex("#A6ACB4"), 0.16f);
            Mat("Plaza", Hex("#7D858E"), 0.14f);
            Mat("LanePaint", Hex("#E9E4D6"), 0.30f);
            Mat("PondBlue", Hex("#58B7C9"), 0.45f);
            // powerup pickups — bright emissive so they read at a glance
            Mat("MagnetRed", Hex("#D8372F"), 0.4f, 0.2f, true);
            Mat("MagnetTip", Hex("#EEF1F6"), 0.6f, 0.5f);
            Mat("BoostOrb", Hex("#3FD4FF"), 0.75f, 0.1f, true);
            Mat("BoostCore", Hex("#FFF3AE"), 0.7f, 0f, true);
            Mat("ShieldGold", Hex("#F0B93E"), 0.6f, 0.5f, true);

            // vehicles — glossy paint, glass, rubber, chrome
            Mat("CarPaint", Hex("#D43D2A"), 0.85f, 0.35f);
            Mat("CarTrim", Hex("#1B1E24"), 0.35f, 0.2f);
            Mat("CarGlass", Hex("#10161E"), 0.92f, 0.55f);
            Mat("TireRubber", Hex("#15171B"), 0.10f);
            Mat("Chrome", Hex("#C9D2DC"), 0.95f, 0.9f);
            var head = Mat("Headlight", Hex("#FFF3CF"), 0.9f, 0.1f, true);
            head.SetColor("_EmissionColor", Hex("#FFF3CF") * 2.4f);
            EditorUtility.SetDirty(head);
            var tail = Mat("Taillight", Hex("#FF2A1E"), 0.7f, 0.1f, true);
            tail.SetColor("_EmissionColor", Hex("#FF2016") * 2.0f);
            EditorUtility.SetDirty(tail);
            Mat("PlanePaint", Hex("#F1ECE0"), 0.50f, 0.15f);
            Mat("PlaneAccent", Hex("#C9332B"), 0.55f, 0.25f);
            Mat("PlaneGlass", Hex("#141B24"), 0.9f, 0.5f);
            Mat("NavRed", Hex("#FF3B30"), 0.5f, 0f, true);
            Mat("NavGreen", Hex("#34E07A"), 0.5f, 0f, true);
            // translucent disc that reads as prop blur at speed
            var blur = Mat("PropBlur", new Color(0.16f, 0.17f, 0.20f, 0.20f), 0.1f);
            blur.SetFloat("_Surface", 1f);
            blur.SetOverrideTag("RenderType", "Transparent");
            blur.renderQueue = (int)RenderQueue.Transparent;
            blur.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            blur.EnableKeyword("_ALPHABLEND_ON");
            EditorUtility.SetDirty(blur);
            // neon underglow — translucent + emissive, retinted per palette at runtime
            var ug = Mat("UnderGlow", new Color(1f, 0.6f, 0.2f, 0.30f), 0.9f);
            ug.SetFloat("_Surface", 1f);
            ug.SetOverrideTag("RenderType", "Transparent");
            ug.renderQueue = (int)RenderQueue.Transparent;
            ug.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            ug.EnableKeyword("_ALPHABLEND_ON");
            ug.EnableKeyword("_EMISSION");
            ug.SetColor("_EmissionColor", Hex("#FFB43A") * 1.6f);
            EditorUtility.SetDirty(ug);

            // desert / egypt dressing — sandstone steps, obelisks, dunes, gold pyramidion caps
            Mat("PyramidSand", Hex("#D9B26A"), 0.16f);
            Mat("PyramidShade", Hex("#C09655"), 0.14f);
            Mat("ObeliskSand", Hex("#CBA968"), 0.18f);
            Mat("SandDune", Hex("#DEB978"), 0.10f);
            Mat("GoldCap", Hex("#F2C14E"), 0.72f, 0.65f, true);
            // googly eyes for the living obstacles
            Mat("EyeWhite", Hex("#F7F4EA"), 0.55f);
            Mat("PupilDark", Hex("#12161C"), 0.45f);
            // far slab — retinted toward the active zone's horizon at runtime
            Mat("FarGround", Hex("#988B77"), 0.08f);

            // lagoon water-slide zone
            Mat("SlideFloor", Hex("#CFEFF2"), 0.55f);
            Mat("SlideWall", Hex("#F6FAF7"), 0.60f);
            Mat("WaterDeep", Hex("#2E86A8"), 0.70f);
            Mat("BuoyRed", Hex("#E4572E"), 0.50f);
            Mat("BuoyWhite", Hex("#F6F6F6"), 0.50f);
            Mat("Foam", Hex("#FFFFFF"), 0.6f, 0f, true);
            var wf = Mat("WaterFlow", new Color(0.24f, 0.72f, 0.84f, 0.45f), 0.85f);
            wf.SetFloat("_Surface", 1f);
            wf.SetOverrideTag("RenderType", "Transparent");
            wf.renderQueue = (int)RenderQueue.Transparent;
            wf.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            wf.EnableKeyword("_ALPHABLEND_ON");
            EditorUtility.SetDirty(wf);

            // skybox
            string skyPath = Root + "/Materials/Sky.mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            if (sky == null)
            {
                sky = new Material(Shader.Find("Skybox/Procedural")) { name = "Sky" };
                AssetDatabase.CreateAsset(sky, skyPath);
            }
            sky.SetFloat("_SunSize", 0.05f);
            sky.SetFloat("_SunSizeConvergence", 4f);
            sky.SetFloat("_AtmosphereThickness", 0.9f);
            sky.SetColor("_SkyTint", Hex("#7FB8D8"));
            sky.SetColor("_GroundColor", ColHorizon);
            sky.SetFloat("_Exposure", 1.25f);
            EditorUtility.SetDirty(sky);
            mats["Sky"] = sky;
            Log("Materials created");
        }

        // ================= sprites =================

        static void GenerateSprites()
        {
            WriteSprite(Root + "/Sprites/RoundRect.png", 64, 64, (x, y, w, h) =>
            {
                float r = 14f;
                float dx = Mathf.Max(0, Mathf.Max(r - x, x - (w - 1 - r)));
                float dy = Mathf.Max(0, Mathf.Max(r - y, y - (h - 1 - r)));
                bool inside = dx * dx + dy * dy <= r * r;
                return inside ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
            }, out roundRect);

            WriteSprite(Root + "/Sprites/Circle.png", 64, 64, (x, y, w, h) =>
            {
                float dx = x - (w - 1) / 2f, dy = y - (h - 1) / 2f;
                return dx * dx + dy * dy <= 30f * 30f
                    ? new Color32(255, 255, 255, 255)
                    : new Color32(255, 255, 255, 0);
            }, out circle);

            WriteSprite(Root + "/Sprites/CoinIcon.png", 64, 64, (x, y, w, h) =>
            {
                float dx = x - 31.5f, dy = y - 31.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d > 30f) return new Color32(0, 0, 0, 0);
                if (d > 24f) return (Color32)Hex("#B8860B");
                if (d > 19f) return (Color32)Hex("#FFD85C");
                return (Color32)ColCoin;
            }, out coinIcon);

            // app icon: amber rounded square with dark chevron runner
            WriteSprite(Root + "/Sprites/AppIcon.png", 192, 192, (x, y, w, h) =>
            {
                float r = 42f;
                float ddx = Mathf.Max(0, Mathf.Max(r - x, x - (w - 1 - r)));
                float ddy = Mathf.Max(0, Mathf.Max(r - y, y - (h - 1 - r)));
                if (ddx * ddx + ddy * ddy > r * r) return new Color32(0, 0, 0, 0);
                // chevron ">>"
                float fx = x / (float)w, fy = y / (float)h;
                float band = Mathf.Abs(fy - 0.5f);
                float edge = 0.5f - band;
                if (fx > edge - 0.13f && fx < edge - 0.02f && band < 0.34f) return (Color32)ColUIPanel;
                if (fx > edge + 0.08f && fx < edge + 0.19f && band < 0.34f) return (Color32)ColUIPanel;
                return (Color32)ColUIAccent;
            }, out _);
            Log("Sprites written");
        }

        delegate Color32 PixelFunc(int x, int y, int w, int h);

        static void WriteSprite(string path, int w, int h, PixelFunc fn, out Sprite sprite)
            => WriteSprite(path, w, h, fn, null, out sprite);

        static void WriteSprite(string path, int w, int h, PixelFunc fn, Vector4? border, out Sprite sprite)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = fn(x, y, w, h);
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.alphaIsTransparency = true;
            imp.mipmapEnabled = false;
            imp.filterMode = FilterMode.Bilinear;
            if (border.HasValue) imp.spriteBorder = border.Value;
            imp.SaveAndReimport();
            sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        // ================= font =================

        static void GenerateFont()
        {
            // TMP runtime resources (shaders, settings, LiberationSans SDF) ship
            // as a unitypackage inside com.unity.ugui — import it silently.
            const string pkg = "Library/PackageCache/com.unity.ugui@0727b404f3a0/Package Resources/TMP Essential Resources.unitypackage";
            if (AssetDatabase.LoadAssetAtPath<TMP_Settings>("Assets/TextMesh Pro/Resources/TMP Settings.asset") == null)
            {
                if (File.Exists(pkg))
                {
                    AssetDatabase.ImportPackage(pkg, false);
                    AssetDatabase.Refresh();
                    Log("TMP essentials imported");
                }
            }
            TMP_Settings.LoadDefaultSettings();

            // prefer a rounded system font; fall back to TMP default
            string[] candidates =
            {
                "/System/Library/Fonts/Supplemental/Arial Rounded Bold.ttf",
                "/System/Library/Fonts/SFNSRounded.ttf",
            };
            foreach (var src in candidates)
            {
                if (!File.Exists(src)) continue;
                string dst = Root + "/Fonts/GameFont.ttf";
                File.Copy(src, dst, true);
                AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceUpdate);
                var font = AssetDatabase.LoadAssetAtPath<Font>(dst);
                if (font == null) continue;

                string faPath = Root + "/Fonts/GameFont.asset";
                var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(faPath);
                if (existing != null) AssetDatabase.DeleteAsset(faPath);

                TMP_FontAsset fa = null;
                try { fa = TMP_FontAsset.CreateFontAsset(font); }
                catch (Exception e) { Log("CreateFontAsset failed: " + e.Message); }
                if (fa == null) continue;

                fa.name = "GameFont";
                AssetDatabase.CreateAsset(fa, faPath);
                try
                {
                    if (fa.material != null) AssetDatabase.AddObjectToAsset(fa.material, fa);
                    if (fa.atlasTexture != null) AssetDatabase.AddObjectToAsset(fa.atlasTexture, fa);
                }
                catch (Exception e) { Log("Font subasset warning: " + e.Message); }

                // make it the project-wide default so every TMP text is covered
                var settingsAsset = AssetDatabase.LoadAssetAtPath<TMP_Settings>("Assets/TextMesh Pro/Resources/TMP Settings.asset");
                if (settingsAsset != null)
                {
                    var so = new SerializedObject(settingsAsset);
                    var prop = so.FindProperty("m_defaultFontAsset");
                    if (prop != null) { prop.objectReferenceValue = fa; so.ApplyModifiedPropertiesWithoutUndo(); }
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(faPath);
                gameFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(faPath);
                if (gameFont != null) { Log("Font: " + Path.GetFileName(src)); return; }
            }
            gameFont = TMP_Settings.defaultFontAsset;
            Log("Font: default LiberationSans");
        }

        /// <summary>Chunky display font for the logo/headers — downloaded OFL font.</summary>
        static void GenerateTitleFont()
        {
            string ttf = Root + "/Fonts/LuckiestGuy-Regular.ttf";
            var font = AssetDatabase.LoadAssetAtPath<Font>(ttf);
            if (font == null) { Log("TitleFont: LuckiestGuy ttf missing — headers fall back to GameFont"); return; }

            string faPath = Root + "/Fonts/TitleFont.asset";
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(faPath);
            if (existing != null) AssetDatabase.DeleteAsset(faPath);

            TMP_FontAsset fa = null;
            try { fa = TMP_FontAsset.CreateFontAsset(font); }
            catch (Exception e) { Log("TitleFont CreateFontAsset failed: " + e.Message); }
            if (fa == null) return;

            fa.name = "TitleFont";
            AssetDatabase.CreateAsset(fa, faPath);
            try
            {
                if (fa.material != null) AssetDatabase.AddObjectToAsset(fa.material, fa);
                if (fa.atlasTexture != null) AssetDatabase.AddObjectToAsset(fa.atlasTexture, fa);
            }
            catch (Exception e) { Log("TitleFont subasset warning: " + e.Message); }
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(faPath);
            titleFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(faPath);
            Log("TitleFont: Luckiest Guy");
        }

        // ================= audio =================

        static void GenerateAudio()
        {
            string dir = Root + "/Resources/Audio";
            WriteWav(dir + "/sfx_coin.wav", SynthCoin());
            WriteWav(dir + "/sfx_jump.wav", SynthJump());
            WriteWav(dir + "/sfx_slide.wav", SynthSlide());
            WriteWav(dir + "/sfx_swipe.wav", SynthSwipe());
            WriteWav(dir + "/sfx_crash.wav", SynthCrash());
            WriteWav(dir + "/sfx_click.wav", SynthClick());
            WriteWav(dir + "/sfx_start.wav", SynthStart());
            WriteWav(dir + "/sfx_powerup.wav", SynthPowerUp());
            WriteWav(dir + "/sfx_shieldbreak.wav", SynthShieldBreak());
            WriteWav(dir + "/sfx_car.wav", SynthCar());
            WriteWav(dir + "/sfx_plane.wav", SynthPlane());
            WriteWav(dir + "/sfx_carloop.wav", LoopEngine(82f, 8f, 0.30f, 0.05f, 31));
            WriteWav(dir + "/sfx_planeloop.wav", LoopPlane());
            // real track wins: only synthesize the loop when no mp3 is present
            if (!File.Exists(dir + "/music_loop.mp3"))
                WriteWav(dir + "/music_loop.wav", SynthMusic());
            else if (File.Exists(dir + "/music_loop.wav"))
                File.Delete(dir + "/music_loop.wav");
            AssetDatabase.Refresh();
            // stream the music clips — full-length tracks are too big to decode to RAM
            foreach (var f in Directory.GetFiles(dir))
            {
                var n = Path.GetFileName(f);
                if (!n.StartsWith("music_loop.") && !n.StartsWith("beat_")) continue;
                if (n.EndsWith(".meta")) continue;
                var imp = AssetImporter.GetAtPath(dir + "/" + n) as AudioImporter;
                if (imp == null) continue;
                var s = imp.defaultSampleSettings;
                s.loadType = AudioClipLoadType.Streaming;
                imp.defaultSampleSettings = s;
                imp.SaveAndReimport();
            }
            Log("Audio generated");
        }

        const int SR = 44100;

        static void WriteWav(string path, float[] samples)
        {
            using var fs = new FileStream(path, FileMode.Create);
            using var bw = new BinaryWriter(fs);
            int dataLen = samples.Length * 2;
            bw.Write("RIFF".ToCharArray());
            bw.Write(36 + dataLen);
            bw.Write("WAVEfmt ".ToCharArray());
            bw.Write(16); bw.Write((short)1); bw.Write((short)1);
            bw.Write(SR); bw.Write(SR * 2); bw.Write((short)2); bw.Write((short)16);
            bw.Write("data".ToCharArray());
            bw.Write(dataLen);
            foreach (var s in samples)
                bw.Write((short)Mathf.Clamp(s * 32767f, -32768f, 32767f));
        }

        static float[] Sine(float f0, float f1, float dur, float vol = 0.6f, float attack = 0.005f)
        {
            int n = (int)(SR * dur);
            var s = new float[n];
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float f = Mathf.Lerp(f0, f1, t);
                phase += f / SR;
                float env = Mathf.Min(1f, i / (SR * attack)) * Mathf.Pow(1f - t, 1.6f);
                s[i] = Mathf.Sin(phase * Mathf.PI * 2f) * vol * env;
            }
            return s;
        }

        static float[] Noise(float dur, float vol, float cutoff0, float cutoff1, int seed = 7)
        {
            int n = (int)(SR * dur);
            var s = new float[n];
            var rng = new System.Random(seed);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float cutoff = Mathf.Lerp(cutoff0, cutoff1, t);
                float raw = (float)(rng.NextDouble() * 2 - 1);
                lp = Mathf.Lerp(lp, raw, cutoff);
                s[i] = lp * vol * (1f - t) * (1f - t);
            }
            return s;
        }

        static float[] Mix(params float[][] parts)
        {
            int n = 0;
            foreach (var p in parts) n = Mathf.Max(n, p.Length);
            var s = new float[n];
            foreach (var p in parts)
                for (int i = 0; i < p.Length; i++) s[i] += p[i];
            return s;
        }

        static float[] SynthCoin() => Mix(Sine(1318, 1318, 0.08f, 0.5f), Delay(Sine(1760, 1760, 0.1f, 0.5f), 0.07f), Delay(Sine(2349, 2349, 0.11f, 0.35f), 0.14f));
        static float[] SynthJump() => Mix(Sine(320, 760, 0.2f, 0.45f), Delay(Noise(0.12f, 0.25f, 0.15f, 0.02f), 0.02f));
        static float[] SynthSlide() => Noise(0.28f, 0.4f, 0.35f, 0.08f);
        static float[] SynthSwipe() => Noise(0.09f, 0.3f, 0.5f, 0.15f);
        static float[] SynthCrash() => Mix(Noise(0.45f, 0.55f, 0.6f, 0.05f), Sine(140, 45, 0.35f, 0.55f));
        static float[] SynthClick() => Sine(1200, 900, 0.05f, 0.35f);
        static float[] SynthStart() => Mix(Sine(660, 660, 0.1f, 0.4f), Delay(Sine(880, 880, 0.1f, 0.4f), 0.1f), Delay(Sine(1320, 1320, 0.16f, 0.45f), 0.2f));
        static float[] SynthPowerUp() => Mix(Sine(523, 523, 0.08f, 0.4f), Delay(Sine(659, 659, 0.08f, 0.4f), 0.08f), Delay(Sine(784, 784, 0.08f, 0.4f), 0.16f), Delay(Sine(1046, 1568, 0.22f, 0.45f), 0.24f));
        static float[] SynthShieldBreak() => Mix(Noise(0.3f, 0.45f, 0.5f, 0.1f), Sine(900, 300, 0.25f, 0.35f));
        static float[] SynthCar() => Mix(
            Sine(95, 430, 0.62f, 0.45f),                       // rev climbing
            Sine(190, 860, 0.58f, 0.20f),                      // octave harmonic
            Noise(0.42f, 0.28f, 0.55f, 0.10f),                 // tire squeal hiss
            Delay(Sine(60, 210, 0.55f, 0.38f), 0.04f));        // low rumble
        static float[] SynthPlane() => Mix(
            Sine(55, 175, 0.85f, 0.5f),                        // prop spin-up
            Delay(Noise(0.7f, 0.30f, 0.15f, 0.55f), 0.08f),    // rising wind
            Delay(Sine(110, 330, 0.6f, 0.22f), 0.15f));

        /// <summary>Seamless engine loop: integer-cycle harmonics + chug AM +
        /// low rumble noise, tail crossfaded into the head.</summary>
        static float[] LoopEngine(float baseHz, float chugHz, float noiseVol, float noiseCut, int seed)
        {
            int n = SR; // 1s buffer — baseHz & chugHz stay integer-cycle
            var s = new float[n];
            var rng = new System.Random(seed);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SR;
                float ph = 2f * Mathf.PI * baseHz * t;
                float v = Mathf.Sin(ph) * 0.5f + Mathf.Sin(ph * 2f) * 0.28f + Mathf.Sin(ph * 3f) * 0.12f;
                v *= 0.72f + 0.28f * Mathf.Sin(2f * Mathf.PI * chugHz * t); // firing chug
                float raw = (float)(rng.NextDouble() * 2 - 1);
                lp = Mathf.Lerp(lp, raw, noiseCut);
                s[i] = (v + lp * noiseVol) * 0.8f;
            }
            return SeamLoop(s, 0.06f);
        }

        /// <summary>Propeller drone: low hum chopped by 2-blade passes + wind wash.</summary>
        static float[] LoopPlane()
        {
            int n = SR;
            var s = new float[n];
            var rng = new System.Random(97);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SR;
                float ph = 2f * Mathf.PI * 60f * t;
                float v = Mathf.Sin(ph) * 0.42f + Mathf.Sin(ph * 2f) * 0.30f + Mathf.Sin(ph * 3f) * 0.10f;
                v *= 0.60f + 0.40f * Mathf.Sin(2f * Mathf.PI * 24f * t);    // blade-pass chop
                float raw = (float)(rng.NextDouble() * 2 - 1);
                lp = Mathf.Lerp(lp, raw, 0.30f);
                s[i] = (v + lp * 0.45f) * 0.85f;
            }
            return SeamLoop(s, 0.07f);
        }

        /// <summary>Crossfade the tail into the head so the loop wrap is inaudible.</summary>
        static float[] SeamLoop(float[] s, float fadeSec)
        {
            int k = Mathf.Min((int)(SR * fadeSec), s.Length / 4);
            var o = new float[s.Length - k];
            Array.Copy(s, o, o.Length);
            for (int i = 0; i < k; i++)
            {
                float t = (i + 1) / (float)(k + 1);
                o[i] = Mathf.Lerp(s[s.Length - k + i], o[i], t);
            }
            return o;
        }

        static float[] Delay(float[] s, float sec)
        {
            int d = (int)(SR * sec);
            var o = new float[s.Length + d];
            Array.Copy(s, 0, o, d, s.Length);
            return o;
        }

        /// <summary>Energetic 8-bar loop: pluck lead melody, saw bass, 4-floor kick, snare, hats. 128 BPM.</summary>
        static float[] SynthMusic()
        {
            const float bpm = 128f;
            float beat = 60f / bpm;
            float bar = beat * 4f;
            int total = (int)(SR * bar * 8f);
            var mix = new float[total];
            var rng = new System.Random(99);

            float Note(int midi) => 440f * Mathf.Pow(2f, (midi - 69) / 12f);
            float Saw(float t, float f) { float p = t * f % 1f; return p * 2f - 1f; }
            float Pluck(float t, float f) =>
                (Mathf.Sin(2f * Mathf.PI * f * t) + 0.3f * Mathf.Sin(2f * Mathf.PI * 3f * f * t) +
                 0.12f * Mathf.Sin(2f * Mathf.PI * 5f * f * t)) * Mathf.Exp(-t * 8f);

            // progression: Am7 - F - C - G, twice (last bar lifts an octave)
            int[][] chords =
            {
                new[] { 57, 60, 64, 67 }, new[] { 53, 57, 60, 65 },
                new[] { 48, 52, 55, 59 }, new[] { 55, 59, 62, 65 },
                new[] { 57, 60, 64, 67 }, new[] { 53, 57, 60, 65 },
                new[] { 48, 52, 55, 59 }, new[] { 55, 59, 62, 67 },
            };
            int[] bassRoots = { 33, 29, 36, 31, 33, 29, 36, 31 };
            // lead melody — 8th notes, pentatonic runs per bar
            int[][] melody =
            {
                new[] { 69, 72, 76, 72, 74, 76, 79, 76 },
                new[] { 77, 76, 72, 69, 72, 74, 76, 74 },
                new[] { 72, 76, 79, 76, 74, 72, 67, 64 },
                new[] { 67, 71, 74, 79, 74, 71, 67, 62 },
                new[] { 69, 72, 76, 72, 74, 76, 79, 81 },
                new[] { 81, 79, 76, 74, 72, 74, 76, 77 },
                new[] { 76, 79, 81, 79, 76, 74, 72, 71 },
                new[] { 74, 79, 83, 81, 79, 76, 74, 71 },
            };

            // pads: soft detuned chords per bar
            for (int b = 0; b < 8; b++)
            {
                int start = (int)(b * bar * SR);
                int len = (int)(bar * SR);
                foreach (var m in chords[b])
                    for (int i = 0; i < len && start + i < total; i++)
                    {
                        float t = i / (float)SR;
                        float env = Mathf.Min(1f, t * 4f) * Mathf.Min(1f, (len - i) / (SR * 0.35f));
                        mix[start + i] += (Mathf.Sin(2 * Mathf.PI * Note(m) * t) * 0.6f +
                                           Mathf.Sin(2 * Mathf.PI * Note(m) * 1.005f * t) * 0.4f) * 0.045f * env;
                    }
            }

            // lead pluck melody — 8th notes with occasional 16th pickup
            for (int b = 0; b < 8; b++)
                for (int n = 0; n < 8; n++)
                {
                    int start = (int)((b * bar + n * beat * 0.5f) * SR);
                    int len = (int)(beat * 0.5f * SR);
                    float f = Note(melody[b][n]);
                    for (int i = 0; i < len && start + i < total; i++)
                        mix[start + i] += Pluck(i / (float)SR, f) * 0.30f;
                }

            // bass: driving 8ths, root + fifth
            int[] bassPat = { 0, 0, 7, 0, 12, 0, 7, 0 };
            for (int b = 0; b < 8 * 8; b++)
            {
                float midi = bassRoots[b / 8] + bassPat[b % 8];
                int start = (int)(b * beat * 0.5f * SR);
                int len = (int)(beat * 0.45f * SR);
                float f = Note((int)midi);
                for (int i = 0; i < len && start + i < total; i++)
                {
                    float t = i / (float)SR;
                    float env = Mathf.Pow(1f - i / (float)len, 0.9f);
                    mix[start + i] += (Saw(t, f) * 0.6f + Mathf.Sin(2 * Mathf.PI * f * t) * 0.4f) * 0.22f * env;
                }
            }

            // drums: 4-on-floor kick, snare on 2 & 4, hats on 8th offbeats
            for (int b = 0; b < 32; b++)
            {
                int start = (int)(b * beat * SR);
                int len = (int)(0.14f * SR);
                for (int i = 0; i < len && start + i < total; i++)
                {
                    float t = i / (float)SR;
                    float f = Mathf.Lerp(140f, 42f, i / (float)len);
                    mix[start + i] += Mathf.Sin(2 * Mathf.PI * f * t) * 0.55f * Mathf.Pow(1f - i / (float)len, 2f);
                }
                // snare on beats 2 and 4
                if (b % 4 == 1 || b % 4 == 3)
                {
                    int slen = (int)(0.16f * SR);
                    for (int i = 0; i < slen && start + i < total; i++)
                    {
                        float t = i / (float)SR;
                        float nz = (float)(rng.NextDouble() * 2 - 1) * Mathf.Exp(-t * 22f);
                        float tone = Mathf.Sin(2 * Mathf.PI * 185f * t) * Mathf.Exp(-t * 30f);
                        mix[start + i] += (nz * 0.55f + tone * 0.45f) * 0.22f;
                    }
                }
                // hat on the offbeat 8th
                int hstart = start + (int)(beat * 0.5f * SR);
                int hlen = (int)(0.035f * SR);
                for (int i = 0; i < hlen && hstart + i < total; i++)
                    mix[hstart + i] += (float)(rng.NextDouble() * 2 - 1) * 0.09f * (1f - i / (float)hlen);
            }

            // open-hat sweep into each new bar pair for drive
            for (int b = 0; b < 8; b += 2)
            {
                int start = (int)((b * bar + bar - beat * 0.5f) * SR);
                int len = (int)(0.3f * SR);
                for (int i = 0; i < len && start + i < total; i++)
                    mix[start + i] += (float)(rng.NextDouble() * 2 - 1) * 0.05f * (i / (float)len);
            }

            // master normalize
            float peak = 0.001f;
            foreach (var v in mix) peak = Mathf.Max(peak, Mathf.Abs(v));
            for (int i = 0; i < mix.Length; i++) mix[i] = mix[i] / peak * 0.82f;
            return mix;
        }

        // ================= prefabs =================

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
                var col = go.GetComponent<Collider>();
                if (col != null) UnityEngine.Object.DestroyImmediate(col);
            }
            return go;
        }

        static GameObject MeshGO(Mesh mesh, string name, Material mat, Transform parent, Vector3 pos, Vector3 scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        static void CreatePrefabs()
        {
            prefabs["Tile"] = MakeTilePrefab();
            prefabs["Tile_Vault"] = MakeVaultTile();
            prefabs["Tile_Forest_A"] = MakeForestTile(41, "Tile_Forest_A");
            prefabs["Tile_Forest_B"] = MakeForestTile(97, "Tile_Forest_B");
            prefabs["Tile_Canyon_A"] = MakeCanyonTile(61, "Tile_Canyon_A");
            prefabs["Tile_Canyon_B"] = MakeCanyonTile(113, "Tile_Canyon_B");
            prefabs["Tile_City_A"] = MakeCityTile(73, "Tile_City_A");
            prefabs["Tile_City_B"] = MakeCityTile(149, "Tile_City_B");
            prefabs["LowBarrier"] = MakeLowBarrier();
            prefabs["HighBarrier"] = MakeHighBarrier();
            prefabs["Wall"] = MakeWall();
            prefabs["Spike"] = MakeSpikeTrap();
            prefabs["Coin"] = MakeCoin();
            // ruins decor visible beyond the walls
            prefabs["D_Pillar"] = MakeDecor("pillar_decorated", "D_Pillar", 0f, new Vector3(1.35f, 1.35f, 1.35f));
            prefabs["D_Pillar2"] = MakeDecor("pillar", "D_Pillar2", 0f, new Vector3(1.2f, 1.5f, 1.2f));
            prefabs["D_Column"] = MakeDecor("column", "D_Column", 0f, new Vector3(1.6f, 1.8f, 1.6f));
            prefabs["D_Rubble"] = MakeDecor("rubble_large", "D_Rubble", 0f, new Vector3(0.8f, 0.8f, 0.8f));
            prefabs["D_Barrel"] = MakeDecor("barrel_large", "D_Barrel", 0f, new Vector3(0.9f, 0.9f, 0.9f));
            prefabs["D_Keg"] = MakeDecor("keg_decorated", "D_Keg");
            prefabs["D_Chest"] = MakeDecor("chest_gold", "D_Chest");
            // vault props
            prefabs["D_Crates"] = MakeDecor("crates_stacked", "D_Crates", 0f, new Vector3(0.9f, 0.9f, 0.9f));
            prefabs["D_Box"] = MakeDecor("box_large", "D_Box", 0f, new Vector3(0.85f, 0.85f, 0.85f));
            prefabs["D_CoinPile"] = MakeCoinPile();
            // forest props (Kenney nature kit — detailed trunks/canopies)
            prefabs["K_Oak"] = MakeDecorK("tree_detailed", "K_Oak", new Vector3(1.7f, 1.7f, 1.7f), sway: 1.8f);
            prefabs["K_Tall"] = MakeDecorK("tree_tall", "K_Tall", new Vector3(1.6f, 1.6f, 1.6f), sway: 1.8f);
            prefabs["K_Pine"] = MakeDecorK("tree_pineTallA_detailed", "K_Pine", new Vector3(1.8f, 1.8f, 1.8f), sway: 1.6f);
            prefabs["K_Bush"] = MakeDecorK("plant_bushDetailed", "K_Bush", new Vector3(1.2f, 1.2f, 1.2f), sway: 3.2f);
            prefabs["K_Grass"] = MakeDecorK("grass_leafsLarge", "K_Grass", new Vector3(1.1f, 1.1f, 1.1f), sway: 3.6f);
            prefabs["K_Flower"] = MakeDecorK("flower_redA", "K_Flower", new Vector3(1.3f, 1.3f, 1.3f), sway: 3.6f);
            prefabs["K_Mush"] = MakeDecorK("mushroom_redGroup", "K_Mush", new Vector3(1.2f, 1.2f, 1.2f), sway: 2.4f);
            prefabs["K_Log"] = MakeDecorK("log_stack", "K_Log", new Vector3(1.2f, 1.2f, 1.2f));
            // canyon props — modular cliff pieces read as real rock formations
            prefabs["K_Cliff"] = MakeDecorK("cliff_large_rock", "K_Cliff", new Vector3(2f, 2f, 2f));
            prefabs["K_CliffTop"] = MakeDecorK("cliff_top_rock", "K_CliffTop", new Vector3(1.8f, 1.8f, 1.8f));
            prefabs["K_Cave"] = MakeDecorK("cliff_cave_rock", "K_Cave", new Vector3(1.8f, 1.8f, 1.8f));
            prefabs["K_Cactus"] = MakeDecorK("cactus_tall", "K_Cactus", new Vector3(1.4f, 1.4f, 1.4f), sway: 3.4f);
            prefabs["K_CactusS"] = MakeDecorK("cactus_short", "K_CactusS", new Vector3(1.3f, 1.3f, 1.3f), sway: 3.4f);
            prefabs["K_Camp"] = MakeDecorK("campfire_stones", "K_Camp", new Vector3(1.4f, 1.4f, 1.4f));
            // egypt/desert dressing — stepped pyramids, obelisks, ruined columns, dunes
            prefabs["D_Pyramid"] = MakePyramidPrefab("D_Pyramid", 3.4f, 2.6f, 4, true);
            prefabs["D_PyramidBig"] = MakePyramidPrefab("D_PyramidBig", 6.4f, 5.0f, 5, true);
            prefabs["D_Obelisk"] = MakeObeliskPrefab();
            prefabs["D_RuinCol"] = MakeRuinColPrefab();
            prefabs["D_Dune"] = MakeDunePrefab();
            // zone-flavoured obstacles — logs/rocks/cacti for the open biomes
            prefabs["N_LogBar"] = MakeLogBarrier();
            prefabs["N_RockJump"] = MakeRockJump();
            prefabs["N_RockBlock"] = MakeRockBlock();
            prefabs["N_CactusBlock"] = MakeCactusBlock();
            prefabs["N_Stump"] = MakeStumpBlock();
            prefabs["N_DeadFall"] = MakeDeadFall();
            // city obstacles — bench to jump, dumpster/crates/parked car to dodge
            prefabs["C_Bench"] = MakeBenchBlock();
            prefabs["C_Dumpster"] = MakeDumpsterBlock();
            prefabs["C_Crates"] = MakeCrateStack();
            prefabs["C_CarBlock"] = MakeCarBlock();
            // city sidewalk decor (zone channel — sits on the walk)
            prefabs["C_Hydrant"] = MakeDecorC("firehydrant", "C_Hydrant", 0.55f);
            prefabs["C_TrashA"] = MakeDecorC("trash_A", "C_TrashA", 0.30f);
            prefabs["C_TrashB"] = MakeDecorC("trash_B", "C_TrashB", 0.28f);
            prefabs["C_Bush"] = MakeDecorC("bush", "C_Bush", 0.85f, sway: 3.2f);
            prefabs["C_Box"] = MakeDecorC("box_A", "C_Box", 0.45f);
            prefabs["C_Tower"] = MakeDecorC("watertower", "C_Tower", 6.5f);
            // Quaternius decor for forest / canyon / lagoon zone channels
            prefabs["Q_Bush"] = MakeDecorQ("Bush_Large_Flowers", "Q_Bush", 1.0f, sway: 3.2f);
            prefabs["Q_BushS"] = MakeDecorQ("Bush_Small_Flowers", "Q_BushS", 0.7f, sway: 3.4f);
            prefabs["Q_Flower"] = MakeDecorQ("Flower_3_Clump", "Q_Flower", 0.5f, sway: 3.6f);
            prefabs["Q_GrassL"] = MakeDecorQ("Grass_Large_Extruded", "Q_GrassL", 0.6f, sway: 3.6f);
            prefabs["Q_Plant"] = MakeDecorQ("Plant_Flowers", "Q_Plant", 0.7f, sway: 3.4f);
            prefabs["Q_RockA"] = MakeDecorQ("Rock_2", "Q_RockA", 0.9f);
            prefabs["Q_RockB"] = MakeDecorQ("Rock_4", "Q_RockB", 1.4f);
            prefabs["Q_Dead"] = MakeDecorQ("DeadTree_6", "Q_Dead", 3.6f, sway: 1.4f);
            prefabs["Q_Dead2"] = MakeDecorQ("DeadTree_9", "Q_Dead2", 3.0f, sway: 1.4f);
            prefabs["Q_RockD"] = MakeDecorQ("Rock_3", "Q_RockD", 1.2f, "Rocks_Desert");
            prefabs["Q_RockD2"] = MakeDecorQ("Rock_5", "Q_RockD2", 0.9f, "Rocks_Red_Desert");
            prefabs["Q_PalmA"] = MakeDecorQ("PalmTree_2", "Q_PalmA", 4.6f, sway: 2.6f);
            prefabs["Q_PalmB"] = MakeDecorQ("PalmTree_4", "Q_PalmB", 5.4f, sway: 2.6f);
            prefabs["Q_Log"] = MakeDecorK("log_large", "Q_Log", new Vector3(1.4f, 1.4f, 1.4f));
            prefabs["Q_Stump"] = MakeDecorQ("Rock_1", "Q_Stump", 0.85f);
            // remastered dungeon dressing for temple/vault variety
            prefabs["D2_Coins"] = MakeDecor2("coin_stack_large", "D2_Coins", 0f, new Vector3(1.6f, 1.6f, 1.6f));
            prefabs["D2_Trunk"] = MakeDecor2("trunk_large_A", "D2_Trunk", 0f, new Vector3(1.5f, 1.5f, 1.5f));
            prefabs["D2_Shelf"] = MakeDecor2("shelf_small_candles", "D2_Shelf", 0f, new Vector3(1.5f, 1.5f, 1.5f));
            prefabs["D2_Table"] = MakeDecor2("table_medium_decorated_A", "D2_Table", 0f, new Vector3(1.5f, 1.5f, 1.5f));
            prefabs["WF_BannerShield"] = MakeDecor2("banner_shield_red", "WF_BannerShield", 3.0f, sway: 2.6f);
            prefabs["WF_BannerThin"] = MakeDecor2("banner_thin_yellow", "WF_BannerThin", 3.1f, sway: 2.6f);
            // route powerups
            prefabs["PU_Magnet"] = MakePowerUpMagnet();
            prefabs["PU_Shield"] = MakePowerUpShield();
            prefabs["PU_Boost"] = MakePowerUpBoost();
            prefabs["PU_Car"] = MakePowerUpCar();
            prefabs["PU_Plane"] = MakePowerUpPlane();
            // lagoon water-slide zone — flume tile, water obstacles, palm decor
            prefabs["Tile_Water"] = MakeWaterTile();
            prefabs["W_Wave"] = MakeWaveBarrier();
            prefabs["W_Buoy"] = MakeBuoyBlock();
            prefabs["W_Geyser"] = MakeGeyserBlock();
            prefabs["W_Palm1"] = MakeDecorN("PalmTree_1", "W_Palm1", 4.6f, sway: 2.6f);
            prefabs["W_Palm2"] = MakeDecorN("PalmTree_2", "W_Palm2", 5.4f, sway: 2.6f);
            prefabs["W_Palm3"] = MakeDecorKFit("tree_palmTall", "W_Palm3", 5.0f, sway: 2.6f);
            prefabs["W_Rock"] = MakeDecorN("Rock_3", "W_Rock", 1.5f);
            prefabs["W_Bush"] = MakeDecorN("Bush_1", "W_Bush", 1.3f, sway: 3.2f);
            prefabs["W_Flowers"] = MakeDecorN("Flowers", "W_Flowers", 1.1f, sway: 3.4f);
            // wall-hung features (child Y baked into prefab)
            prefabs["WF_Torch"] = MakeTorchFeature();
            prefabs["WF_Banner"] = MakeBannerFeature();
            prefabs["WF_BannerBig"] = MakeBannerTriple();
            prefabs["WF_Crest"] = MakeCrestFeature();
            prefabs["Player"] = MakePlayerV2();
            Log("Prefabs created");
        }

        static GameObject SavePrefab(GameObject root, string name)
        {
            string path = Root + "/Prefabs/" + name + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            // Re-import so the in-memory asset resolves fresh references
            // (asset refs written mid-build otherwise stay stale until reload)
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        // ---------- dungeon model helpers ----------

        /// <summary>Instantiate a Nature-pack FBX under parent at localPos.</summary>
        static GameObject NModel(string name, Transform parent, Vector3 localPos, float rotY = 0f)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(ArtDir + "/Nature/" + name + ".fbx");
            if (src == null) { Log("MISSING nature " + name); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0f, rotY, 0f);
            StripColliders(go);
            FixDungeonMats(go);
            return go;
        }

        /// <summary>Instantiate a Nature2 (Kenney) GLB under parent at localPos.</summary>
        static GameObject KModel(string name, Transform parent, Vector3 localPos, float rotY = 0f, Vector3? scale = null)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(ArtDir + "/Nature2/" + name + ".glb");
            if (src == null) { Log("MISSING kenney " + name); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0f, rotY, 0f);
            if (scale.HasValue) go.transform.localScale = scale.Value;
            StripColliders(go);
            FixDungeonMats(go);
            return go;
        }

        /// <summary>Instantiate a City-pack glTF (KayKit City Builder Bits) under parent.</summary>
        static GameObject CModel(string name, Transform parent, Vector3 localPos, float rotY = 0f, Vector3? scale = null)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(ArtDir + "/City/" + name + ".gltf");
            if (src == null) { Log("MISSING city " + name); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0f, rotY, 0f);
            if (scale.HasValue) go.transform.localScale = scale.Value;
            StripColliders(go);
            FixDungeonMats(go);
            return go;
        }

        /// <summary>Instantiate a Nature3 FBX (Quaternius Ultimate Stylized Nature) under parent; wires the pack's textures by material name.</summary>
        static GameObject QModel(string name, Transform parent, Vector3 localPos, float rotY = 0f, Vector3? scale = null, string rockTex = null)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(ArtDir + "/Nature3/" + name + ".fbx");
            if (src == null) { Log("MISSING nature3 " + name); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0f, rotY, 0f);
            if (scale.HasValue) go.transform.localScale = scale.Value;
            StripColliders(go);
            ApplyNatureMats(go, rockTex);
            return go;
        }

        /// <summary>Instantiate a Dungeon/CharNew GLB under parent at localPos; optionally fit to a size.</summary>
        static GameObject DModel(string name, Transform parent, Vector3 localPos, float rotY = 0f, Vector3? scale = null, string folder = "Dungeon")
        {
            // "D2:name" shorthand reaches into the Dungeon2 (KayKit Remastered) pack
            if (name.StartsWith("D2:")) { folder = "Dungeon2"; name = name.Substring(3); }
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(ArtDir + "/" + folder + "/" + name + ".glb");
            if (src == null) { Log("MISSING model " + name); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0f, rotY, 0f);
            if (scale.HasValue) go.transform.localScale = scale.Value;
            StripColliders(go);
            return go;
        }

        /// <summary>Scale a placed model so its local-space bounds hit a target size (X and/or Z).</summary>
        static void FitModel(GameObject go, float? targetX = null, float? targetZ = null, float? targetY = null)
        {
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return;
            var b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            var s = go.transform.localScale;
            var size = b.size;
            if (targetX.HasValue && size.x > 0.01f) s = new Vector3(s.x * targetX.Value / size.x, s.y, s.z);
            b = ComputeLocalBounds(go); // recompute after x scale for z fit
            if (targetZ.HasValue && b.size.z > 0.01f) s = new Vector3(s.x, s.y, s.z * targetZ.Value / b.size.z);
            if (targetY.HasValue && b.size.y > 0.01f) s = new Vector3(s.x, s.y * targetY.Value / b.size.y, s.z);
            go.transform.localScale = s;
        }

        static Bounds ComputeLocalBounds(GameObject go)
        {
            var rends = go.GetComponentsInChildren<Renderer>();
            var b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            return b;
        }

        static void StripColliders(GameObject go)
        {
            foreach (var c in go.GetComponentsInChildren<Collider>())
                UnityEngine.Object.DestroyImmediate(c);
        }

        /// <summary>Tune shared glTF-imported materials once: keep them matte & rich.</summary>
        static readonly HashSet<Material> fixedMats = new();
        static void FixDungeonMats(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || fixedMats.Contains(m)) continue;
                    fixedMats.Add(m);
                    if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.3f);
                    if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.05f);
                    if (m.HasProperty("_BumpScale")) m.SetFloat("_BumpScale", 0.8f);
                    // saturate imported foliage so trees/bushes don't read dull
                    if (m.HasProperty("_BaseColor") &&
                        (m.name.Contains("Green") || m.name.Contains("Leaf") || m.name.Contains("Leaves") ||
                         m.name.Contains("Grass") || m.name.Contains("Foliage") || m.name.Contains("leafs")))
                    {
                        Color.RGBToHSV(m.GetColor("_BaseColor"), out float h, out float s, out float v);
                        m.SetColor("_BaseColor", Color.HSVToRGB(h, Mathf.Min(1f, s * 1.3f), Mathf.Min(1f, v * 1.08f)));
                        EditorUtility.SetDirty(m);
                    }
                }
        }

        /// <summary>
        /// Quaternius FBX materials carry names ("BirchTree_Leaves", "Rock") but no
        /// textures — rebuild them as URP Lit materials driven by the pack's PNGs.
        /// rockTex lets zones re-skin rocks (Rocks_Desert / Rocks_Red_Desert).
        /// </summary>
        static readonly Dictionary<string, Material> natMats = new();
        static void ApplyNatureMats(GameObject go, string rockTex = null)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var sm = r.sharedMaterials;
                for (int i = 0; i < sm.Length; i++)
                {
                    if (sm[i] == null) continue;
                    string n = sm[i].name;
                    int sp = n.IndexOf(' ');            // strip " (Instance)" etc.
                    if (sp > 0) n = n.Substring(0, sp);
                    sm[i] = NatureMat(n, n.StartsWith("Rock") ? rockTex : null);
                }
                r.sharedMaterials = sm;
            }
        }

        static Material NatureMat(string matName, string texOverride = null)
        {
            string key = matName + "|" + (texOverride ?? "");
            if (natMats.TryGetValue(key, out var cached) && cached != null) return cached;
            Texture2D tex = null;
            foreach (var cand in new[] { texOverride, matName + ".png", matName + "s.png" })
            {
                if (cand == null) continue;
                string p = cand.EndsWith(".png") ? cand : cand + ".png";
                tex = AssetDatabase.LoadAssetAtPath<Texture2D>(ArtDir + "/Nature3/Textures/" + p);
                if (tex != null) break;
            }
            string matPath = Root + "/Materials/Nat3_" + key.Replace('|', '_').Replace(".png", "") + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Nat3_" + matName };
                AssetDatabase.CreateAsset(m, matPath);
            }
            if (tex != null) { m.SetTexture("_BaseMap", tex); m.SetColor("_BaseColor", Color.white); }
            else // sensible flat fallback per family
                m.SetColor("_BaseColor", matName.Contains("Bark") || matName.Contains("Trunk") ? Hex("#7A5230")
                    : matName.Contains("Rock") ? Hex("#8D8D96") : Hex("#57A04F"));
            m.SetFloat("_Smoothness", 0.12f);
            m.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(m);
            natMats[key] = m;
            return m;
        }

        /// <summary>Uniform-scale a placed model so its world Z extent hits target — safe for yaw-rotated pieces.</summary>
        static void UniformFitZ(GameObject m, float targetZ)
        {
            if (m == null) return;
            var b = ComputeLocalBounds(m);
            if (b.size.z < 0.01f) return;
            m.transform.localScale *= targetZ / b.size.z;
        }

        /// <summary>Shift a placed model so its rendered base sits at groundY (local space of the prefab root).</summary>
        static void SnapToGround(GameObject m, float groundY)
        {
            if (m == null) return;
            var b = ComputeLocalBounds(m);
            var p = m.transform.localPosition;
            p.y += groundY - b.min.y;
            m.transform.localPosition = p;
        }

        /// <summary>Snap base to groundY AND recenter horizontally — fixes corner-pivot models (e.g. barrier_half spanning x 0..2).</summary>
        static void NormalizePivot(GameObject m, float groundY = 0f)
        {
            if (m == null) return;
            var b = ComputeLocalBounds(m);
            var p = m.transform.localPosition;
            m.transform.localPosition = new Vector3(p.x - b.center.x, p.y + groundY - b.min.y, p.z - b.center.z);
        }

        // wall variety pattern per 4m slot — keeps the corridor interesting
        // (D2: prefix = KayKit Dungeon Remastered pack in Art/Dungeon2)
        static readonly string[] WallPatternL = { "wall", "wall_pillar", "D2:wall_window_open", "wall", "wall_doorway", "wall_arched" };
        static readonly string[] WallPatternR = { "wall_arched", "wall", "wall_cracked", "D2:wall_archedwindow_open", "wall_pillar", "wall" };
        // clean flagstones only — clutter stays off the running lanes
        static readonly string[] StonePool = { "floor_tile_large", "floor_tile_large", "floor_tile_large" };
        static readonly string[] RocksPool = { "floor_tile_large", "floor_tile_large", "floor_tile_large" };
        static readonly string[] VaultWallL = { "wall_pillar", "wall_arched", "D2:wall_archedwindow_gated", "wall_doorway", "wall_pillar", "D2:wall_window_open" };
        static readonly string[] VaultWallR = { "D2:wall_window_open", "wall_pillar", "wall_doorway", "D2:wall_archedwindow_open", "wall_arched", "wall_pillar" };
        static readonly string[] ForestTrees = { "tree_detailed", "tree_oak", "tree_tall", "tree_fat", "tree_blocks", "tree_pineTallA_detailed", "tree_pineTallB_detailed", "tree_pineTallC_detailed", "tree_simple", "tree_palmTall" };
        static readonly string[] ForestEdge = { "plant_bush", "plant_bushDetailed", "plant_bushLarge", "plant_bushSmall", "grass_large", "grass_leafsLarge", "flower_redA", "flower_purpleA", "flower_yellowA", "mushroom_redGroup", "mushroom_tanGroup", "mushroom_redTall", "log", "log_stack" };
        static readonly string[] CanyonCliffs = { "cliff_block_rock", "cliff_half_rock", "cliff_large_rock", "cliff_corner_rock", "cliff_diagonal_rock", "cliff_top_rock", "cliff_cave_rock" };
        static readonly string[] CanyonProps = { "cactus_tall", "cactus_short", "cliff_blockCave_rock", "campfire_stones", "campfire_logs", "grass_large", "mushroom_tan", "path_stone" };
        // Quaternius Ultimate Stylized Nature (Art/Nature3) — full-bodied trees, not cones
        static readonly string[] QTrees = { "BirchTree_1", "BirchTree_2", "BirchTree_3", "MapleTree_1", "MapleTree_2", "MapleTree_3", "NormalTree_1", "NormalTree_2", "NormalTree_3", "NormalTree_4", "PineTree_1", "PineTree_2", "PineTree_3" };
        static readonly string[] QPines = { "PineTree_1", "PineTree_2", "PineTree_3", "PineTree_4", "PineTree_5", "BirchTree_4", "BirchTree_5", "MapleTree_4", "MapleTree_5", "NormalTree_5" };
        static readonly string[] QUnder = { "Bush_Small", "Bush_Small_Flowers", "Bush", "Bush_Flowers", "Grass_Large", "Grass_Large_Extruded", "Grass_Small", "Flower_1_Clump", "Flower_2_Clump", "Flower_3_Clump", "Flower_4_Clump", "Flower_5_Clump", "Plant_1", "Plant_2", "Plant_Flowers" };
        static readonly string[] QRocks = { "Rock_1", "Rock_2", "Rock_3", "Rock_4", "Rock_5" };
        static readonly string[] QDead = { "DeadTree_1", "DeadTree_2", "DeadTree_3", "DeadTree_4", "DeadTree_5", "DeadTree_6", "DeadTree_7", "DeadTree_8", "DeadTree_9", "DeadTree_10" };
        static readonly string[] QPalm = { "PalmTree_1", "PalmTree_2", "PalmTree_3", "PalmTree_4", "PalmTree_5" };
        // KayKit City Builder Bits (Art/City)
        static readonly string[] CBuildings = { "building_A", "building_B", "building_C", "building_D", "building_E", "building_F", "building_G", "building_H" };
        static readonly string[] CBuildingsNB = { "building_A_withoutBase", "building_C_withoutBase", "building_E_withoutBase", "building_G_withoutBase" };
        static readonly string[] CCars = { "car_hatchback", "car_sedan", "car_taxi", "car_stationwagon", "car_police" };
        static readonly string[] CSidewalk = { "firehydrant", "trash_A", "trash_B", "bush", "box_A", "bench" };

        static void WallRow(Transform root, string[] patL, string[] patR)
        {
            for (int zi = 0; zi < 6; zi++)
            {
                var wl = DModel(patL[zi], root, new Vector3(-4.45f, 0f, zi * 4f + 2f), 90f);
                var wr = DModel(patR[zi], root, new Vector3(4.45f, 0f, zi * 4f + 2f), -90f);
                // remastered pieces: uniform-fit the 4m slot + rest the base on the floor
                if (patL[zi].StartsWith("D2:") && wl != null) { UniformFitZ(wl, 4.0f); SnapToGround(wl, 0f); }
                if (patR[zi].StartsWith("D2:") && wr != null) { UniformFitZ(wr, 4.0f); SnapToGround(wr, 0f); }
                if (wl != null) FixDungeonMats(wl);
                if (wr != null) FixDungeonMats(wr);
            }
        }

        /// <summary>Shared floor grid: 2x6 rows of 4m tiles + undercroft. Pass groundMat for a plain painted path instead of GLB tiles.</summary>
        static void TileFloor(Transform root, string[] pool, System.Random rng, bool dirtTint, Material groundMat = null, float groundW = 9.4f, Material underMat = null)
        {
            var ground = Prim(PrimitiveType.Cube, "Ground", groundMat != null ? groundMat : mats["Track"], root.transform,
                new Vector3(0, -0.28f, 12), new Vector3(groundW, 0.6f, 24f), keepCollider: true);
            var mr = ground.GetComponent<MeshRenderer>();
            mr.enabled = groundMat != null;
            if (groundMat == null)
                for (int zi = 0; zi < 6; zi++)
                    for (int xi = 0; xi < 2; xi++)
                    {
                        var f = DModel(pool[rng.Next(pool.Length)], root.transform,
                            new Vector3(xi == 0 ? -2f : 2f, 0f, zi * 4f + 2f));
                        if (f != null) { FitModel(f, targetX: 4.0f); FitModel(f, targetZ: 4.0f); FixDungeonMats(f); }
                    }
            Prim(PrimitiveType.Cube, "UnderBed", underMat != null ? underMat : mats["StoneDark"], root.transform,
                new Vector3(0, -0.75f, 12), new Vector3(56f, 0.5f, 24f), keepCollider: true); // collider lets props raycast-snap onto the bed
            // backdrop colonnade — colossal pillars and broken wall arcs looming outside the corridor
            var rng2 = new System.Random(71);
            for (int zi = 0; zi < 6; zi++)
                foreach (var side in new[] { -1f, 1f })
                {
                    if (rng2.NextDouble() < 0.8)
                    {
                        var c = DModel(rng2.NextDouble() < 0.5 ? "column" : "pillar_decorated", root.transform,
                            new Vector3(side * (8.6f + (float)rng2.NextDouble() * 4f), 0f, zi * 4f + (float)rng2.NextDouble() * 2f), rng2.Next(4) * 90);
                        if (c != null) { FitModel(c, targetY: 7.5f + (float)rng2.NextDouble() * 4f); SnapToGround(c, -0.5f); }
                    }
                    if (rng2.NextDouble() < 0.35)
                    {
                        var w = DModel(rng2.NextDouble() < 0.5 ? "wall_arched" : "wall_cracked", root.transform,
                            new Vector3(side * (12.5f + (float)rng2.NextDouble() * 4f), 0f, zi * 4f + 2f + (float)rng2.NextDouble() * 1.5f), side < 0 ? 90f : -90f);
                        if (w != null) { FitModel(w, targetY: 9f + (float)rng2.NextDouble() * 4f); SnapToGround(w, -0.5f); }
                    }
                }
        }

        static GameObject MakeTilePrefab() // TEMPLE corridor
        {
            var root = new GameObject("TrackTile");
            TileFloor(root.transform, StonePool, new System.Random(11), false);
            WallRow(root.transform, WallPatternL, WallPatternR);
            return SavePrefab(root, "Tile_Temple");
        }

        static GameObject MakeVaultTile() // grand treasure hall — pillars + arches
        {
            var root = new GameObject("TrackTile");
            TileFloor(root.transform, RocksPool, new System.Random(23), false);
            WallRow(root.transform, VaultWallL, VaultWallR);
            return SavePrefab(root, "Tile_Vault");
        }

        static GameObject MakeForestTile(int seed, string name) // OPEN forest — dense stylized woodland
        {
            var root = new GameObject("TrackTile");
            var rng = new System.Random(seed);
            // wide meadow + raised dirt path — both keep colliders so props/pickups snap to the real surface
            Prim(PrimitiveType.Cube, "Meadow", mats["Grass"], root.transform,
                new Vector3(0, -0.28f, 12), new Vector3(60f, 0.6f, 24f), keepCollider: true);
            Prim(PrimitiveType.Cube, "Path", mats["PathDirt"], root.transform,
                new Vector3(0, -0.10f, 12), new Vector3(9.4f, 0.28f, 24f), keepCollider: true);
            // pebbles along the path edge
            for (int zi = 0; zi < 12; zi++)
                foreach (var side in new[] { -1f, 1f })
                    if (rng.NextDouble() < 0.7)
                    {
                        var s = QModel(QRocks[rng.Next(QRocks.Length)], root.transform,
                            new Vector3(side * (4.5f + (float)rng.NextDouble() * 0.5f), 0f, zi * 2f + (float)rng.NextDouble()), rng.Next(360));
                        if (s != null) { FitModel(s, targetY: 0.20f + (float)rng.NextDouble() * 0.18f); SnapToGround(s, 0.04f); }
                    }
            // canopy walls — three staggered rows per side for real depth
            for (int zi = 0; zi < 13; zi++)
                foreach (var side in new[] { -1f, 1f })
                {
                    var t = QModel(QTrees[rng.Next(QTrees.Length)], root.transform,
                        new Vector3(side * (5.5f + (float)rng.NextDouble() * 1.4f), 0f, zi * 1.9f + (float)rng.NextDouble()), rng.Next(360));
                    if (t != null) { FitModel(t, targetY: 4.2f + (float)rng.NextDouble() * 2.6f); SnapToGround(t, 0.02f); }
                }
            for (int zi = 0; zi < 8; zi++)
                foreach (var side in new[] { -1f, 1f })
                {
                    var t = QModel(rng.NextDouble() < 0.5 ? QPines[rng.Next(QPines.Length)] : QTrees[rng.Next(QTrees.Length)], root.transform,
                        new Vector3(side * (8.2f + (float)rng.NextDouble() * 2.4f), 0f, zi * 3f + 1.2f + (float)rng.NextDouble() * 1.4f), rng.Next(360));
                    if (t != null) { FitModel(t, targetY: 6f + (float)rng.NextDouble() * 3.5f); SnapToGround(t, 0.02f); }
                    if (rng.NextDouble() < 0.55)
                    {
                        var t2 = QModel(QTrees[rng.Next(QTrees.Length)], root.transform,
                            new Vector3(side * (11.5f + (float)rng.NextDouble() * 3.5f), 0f, zi * 3f + (float)rng.NextDouble() * 2f), rng.Next(360));
                        if (t2 != null) { FitModel(t2, targetY: 8f + (float)rng.NextDouble() * 3.5f); SnapToGround(t2, 0.02f); }
                    }
                }
            // underbrush hugging the path edge
            for (int zi = 0; zi < 16; zi++)
                foreach (var side in new[] { -1f, 1f })
                    if (rng.NextDouble() < 0.62)
                    {
                        var u = QModel(QUnder[rng.Next(QUnder.Length)], root.transform,
                            new Vector3(side * (4.2f + (float)rng.NextDouble() * 1.0f), 0f, zi * 1.5f + (float)rng.NextDouble()), rng.Next(360));
                        if (u != null) { FitModel(u, targetY: 0.5f + (float)rng.NextDouble() * 0.55f); SnapToGround(u, 0.04f); }
                    }
            // occasional fallen log + big rock deeper in the brush
            for (int zi = 0; zi < 3; zi++)
                foreach (var side in new[] { -1f, 1f })
                    if (rng.NextDouble() < 0.5)
                    {
                        var p = KModel(rng.NextDouble() < 0.5 ? "log_large" : "log_stack", root.transform,
                            new Vector3(side * (5.6f + (float)rng.NextDouble() * 1.6f), 0f, zi * 7f + (float)rng.NextDouble() * 4f), rng.Next(360));
                        if (p != null) { FitModel(p, targetY: 0.7f + (float)rng.NextDouble() * 0.5f); SnapToGround(p, 0.02f); }
                    }
            // horizon wall — colossal treeline closing the skyline so no void shows through fog
            for (int zi = 0; zi < 4; zi++)
                foreach (var side in new[] { -1f, 1f })
                    if (rng.NextDouble() < 0.85)
                    {
                        var h = QModel(rng.NextDouble() < 0.5 ? QPines[rng.Next(QPines.Length)] : QTrees[rng.Next(QTrees.Length)], root.transform,
                            new Vector3(side * (15.5f + (float)rng.NextDouble() * 7f), 0f, zi * 6f + (float)rng.NextDouble() * 3f), rng.Next(360));
                        if (h != null) { FitModel(h, targetY: 11f + (float)rng.NextDouble() * 5f); SnapToGround(h, -0.3f); AddSway(h, 0.5f, 0.5f); }
                    }
            return SavePrefab(root, name);
        }

        static GameObject MakeCanyonTile(int seed, string name) // DESERT — sandy run, low rims, egypt ruins, pyramid skyline
        {
            var root = new GameObject("TrackTile");
            var rng = new System.Random(seed);
            // wide desert bed + raised sandy run — both collidable for prop snapping
            Prim(PrimitiveType.Cube, "DesertBed", mats["CanyonBed"], root.transform,
                new Vector3(0, -0.28f, 12), new Vector3(64f, 0.6f, 24f), keepCollider: true);
            Prim(PrimitiveType.Cube, "Path", mats["PathSand"], root.transform,
                new Vector3(0, -0.10f, 12), new Vector3(9.4f, 0.28f, 24f), keepCollider: true);
            // sun-bleached stones along the run edge
            for (int zi = 0; zi < 10; zi++)
                foreach (var side in new[] { -1f, 1f })
                    if (rng.NextDouble() < 0.6)
                    {
                        var s = QModel(QRocks[rng.Next(QRocks.Length)], root.transform,
                            new Vector3(side * (4.4f + (float)rng.NextDouble() * 0.6f), 0f, zi * 2.4f + (float)rng.NextDouble() * 1.5f), rng.Next(360), rockTex: "Rocks_Desert");
                        if (s != null) { FitModel(s, targetY: 0.25f + (float)rng.NextDouble() * 0.35f); SnapToGround(s, 0.04f); }
                    }
            // low canyon rims hugging the run — frame the corridor, leave the skyline open
            for (int zi = 0; zi < 13; zi++)
                foreach (var side in new[] { -1f, 1f })
                {
                    var r = KModel(CanyonCliffs[rng.Next(CanyonCliffs.Length)], root.transform,
                        new Vector3(side * (5.1f + (float)rng.NextDouble() * 1.2f), 0f, zi * 1.9f + (float)rng.NextDouble() * 0.9f), rng.Next(4) * 90);
                    if (r != null) { FitModel(r, targetY: 2.6f + (float)rng.NextDouble() * 1.8f); SnapToGround(r, 0.0f); }
                }
            // mid-field ruins — pyramids, obelisks, broken columns, cacti, boulders, camp leftovers
            for (int zi = 0; zi < 11; zi++)
                foreach (var side in new[] { -1f, 1f })
                {
                    float z = zi * 2.2f + (float)rng.NextDouble();
                    double roll = rng.NextDouble();
                    if (roll < 0.24)
                    {
                        BuildPyramid(root.transform,
                            new Vector3(side * (7.2f + (float)rng.NextDouble() * 4.6f), 0f, z),
                            2.4f + (float)rng.NextDouble() * 1.8f, 1.8f + (float)rng.NextDouble() * 1.3f,
                            3 + rng.Next(2), rng.NextDouble() < 0.6f, (float)rng.NextDouble() * 90f);
                    }
                    else if (roll < 0.46)
                    {
                        BuildObelisk(root.transform,
                            new Vector3(side * (5.9f + (float)rng.NextDouble() * 3.4f), 0f, z),
                            2.4f + (float)rng.NextDouble() * 1.6f, (float)rng.NextDouble() * 360f);
                    }
                    else if (roll < 0.64)
                    {
                        BuildRuinCol(root.transform,
                            new Vector3(side * (5.9f + (float)rng.NextDouble() * 3.4f), 0f, z),
                            0.8f + (float)rng.NextDouble() * 0.7f, (float)rng.NextDouble() * 360f);
                    }
                    else if (roll < 0.80)
                    {
                        var c = KModel(rng.NextDouble() < 0.55 ? "cactus_tall" : "cactus_short", root.transform,
                            new Vector3(side * (5.7f + (float)rng.NextDouble() * 3f), 0f, z), rng.Next(360));
                        if (c != null) { FitModel(c, targetY: 0.9f + (float)rng.NextDouble() * 1.4f); SnapToGround(c, 0.02f); AddSway(c, 3.2f, 1.6f); }
                    }
                    else if (roll < 0.92)
                    {
                        var k = QModel(QRocks[rng.Next(QRocks.Length)], root.transform,
                            new Vector3(side * (5.8f + (float)rng.NextDouble() * 4f), 0f, z), rng.Next(360), rockTex: "Rocks_Desert");
                        if (k != null) { FitModel(k, targetY: 0.6f + (float)rng.NextDouble() * 1.1f); SnapToGround(k, 0.02f); }
                    }
                    else
                    {
                        var f = KModel(rng.NextDouble() < 0.5 ? "campfire_stones" : "campfire_logs", root.transform,
                            new Vector3(side * (6.4f + (float)rng.NextDouble() * 2.2f), 0f, z), rng.Next(360));
                        if (f != null) { FitModel(f, targetY: 0.4f + (float)rng.NextDouble() * 0.3f); SnapToGround(f, 0.02f); }
                    }
                }
            // dunes softening the mid-ground bed
            for (int zi = 0; zi < 6; zi++)
                foreach (var side in new[] { -1f, 1f })
                    if (rng.NextDouble() < 0.6)
                        BuildDune(root.transform,
                            new Vector3(side * (9.5f + (float)rng.NextDouble() * 6.5f), 0f, zi * 4f + (float)rng.NextDouble() * 2.5f),
                            2f + (float)rng.NextDouble() * 1.8f, (float)rng.NextDouble() * 360f);
            // monument row — stepped pyramids and mesas marching behind the rims
            for (int zi = 0; zi < 4; zi++)
                foreach (var side in new[] { -1f, 1f })
                {
                    if (rng.NextDouble() < 0.8)
                        BuildPyramid(root.transform,
                            new Vector3(side * (13.5f + (float)rng.NextDouble() * 5f), 0f, zi * 6f + (float)rng.NextDouble() * 3f),
                            6f + (float)rng.NextDouble() * 4f, 4.5f + (float)rng.NextDouble() * 3f,
                            4 + rng.Next(2), true, rng.Next(4) * 45f);
                    if (rng.NextDouble() < 0.55)
                    {
                        var r2 = KModel(CanyonCliffs[rng.Next(CanyonCliffs.Length)], root.transform,
                            new Vector3(side * (12.5f + (float)rng.NextDouble() * 3.5f), 0f, zi * 6f + 1.5f + (float)rng.NextDouble() * 1.4f), rng.Next(4) * 90);
                        if (r2 != null) { FitModel(r2, targetY: 7.5f + (float)rng.NextDouble() * 3.5f); SnapToGround(r2, 0.0f); }
                    }
                }
            // horizon mega-pyramids — huge smooth shells that read through the fog
            for (int zi = 0; zi < 3; zi++)
                foreach (var side in new[] { -1f, 1f })
                    if (rng.NextDouble() < 0.75)
                        BuildPyramid(root.transform,
                            new Vector3(side * (21f + (float)rng.NextDouble() * 9f), 0f, zi * 8f + (float)rng.NextDouble() * 5f),
                            11f + (float)rng.NextDouble() * 7f, 8f + (float)rng.NextDouble() * 6f,
                            1, rng.NextDouble() < 0.5f, rng.Next(4) * 45f);
            return SavePrefab(root, name);
        }

        static GameObject MakeCityTile(int seed, string name) // CITY — asphalt road, sidewalks, buildings both sides
        {
            var root = new GameObject("TrackTile");
            var rng = new System.Random(seed);
            // plaza bed under everything + raised asphalt road + sidewalks (all collide so props snap)
            Prim(PrimitiveType.Cube, "Plaza", mats["Plaza"], root.transform,
                new Vector3(0, -0.45f, 12), new Vector3(64f, 0.6f, 24f), keepCollider: true);
            Prim(PrimitiveType.Cube, "Road", mats["RoadAsphalt"], root.transform,
                new Vector3(0, -0.08f, 12), new Vector3(9.6f, 0.2f, 24f), keepCollider: true);
            Prim(PrimitiveType.Cube, "WalkL", mats["Sidewalk"], root.transform,
                new Vector3(-5.55f, 0.02f, 12), new Vector3(2.1f, 0.24f, 24f), keepCollider: true);
            Prim(PrimitiveType.Cube, "WalkR", mats["Sidewalk"], root.transform,
                new Vector3(5.55f, 0.02f, 12), new Vector3(2.1f, 0.24f, 24f), keepCollider: true);
            // lane dashes + solid edge lines
            for (int zi = 0; zi < 8; zi++)
                foreach (var x in new[] { -1.1f, 1.1f })
                    Prim(PrimitiveType.Cube, "Dash", mats["LanePaint"], root.transform,
                        new Vector3(x, 0.035f, zi * 3f + 1.4f), new Vector3(0.14f, 0.012f, 1.3f));
            foreach (var x in new[] { -4.42f, 4.42f })
                Prim(PrimitiveType.Cube, "EdgeLine", mats["LanePaint"], root.transform,
                    new Vector3(x, 0.032f, 12), new Vector3(0.12f, 0.012f, 24f));
            // building rows facing the road; a second, taller skyline row behind
            for (int zi = 0; zi < 7; zi++)
                foreach (var side in new[] { -1f, 1f })
                {
                    if (rng.NextDouble() < 0.85)
                    {
                        var b = CModel(CBuildings[rng.Next(CBuildings.Length)], root.transform,
                            new Vector3(side * (8.3f + (float)rng.NextDouble() * 1.6f), 0f, zi * 3.4f + (float)rng.NextDouble() * 1.4f), side < 0 ? 90f : -90f);
                        if (b != null) { FitModel(b, targetY: 5.2f + (float)rng.NextDouble() * 3.2f); SnapToGround(b, 0.02f); }
                    }
                    if (rng.NextDouble() < 0.5)
                    {
                        var b2 = CModel(CBuildings[rng.Next(CBuildings.Length)], root.transform,
                            new Vector3(side * (12f + (float)rng.NextDouble() * 2.6f), 0f, zi * 3.4f + 1.6f + (float)rng.NextDouble() * 1.4f), side < 0 ? 90f : -90f);
                        if (b2 != null) { FitModel(b2, targetY: 7.5f + (float)rng.NextDouble() * 4f); SnapToGround(b2, 0.02f); }
                    }
                    if (rng.NextDouble() < 0.4) // third skyline row — towers fading into the haze
                    {
                        var b3 = CModel(CBuildingsNB[rng.Next(CBuildingsNB.Length)], root.transform,
                            new Vector3(side * (16.5f + (float)rng.NextDouble() * 5f), 0f, zi * 3.4f + 0.8f + (float)rng.NextDouble() * 1.6f), side < 0 ? 90f : -90f);
                        if (b3 != null) { FitModel(b3, targetY: 10f + (float)rng.NextDouble() * 5f); SnapToGround(b3, -0.4f); }
                    }
                }
            // streetlights along the sidewalk inner edge, alternating sides
            for (int zi = 0; zi < 5; zi++)
            {
                float side = zi % 2 == 0 ? -1f : 1f;
                var l = CModel("streetlight", root.transform,
                    new Vector3(side * 4.95f, 0f, zi * 5f + 0.8f), side < 0 ? 180f : 0f);
                if (l != null) { FitModel(l, targetY: 3.1f); SnapToGround(l, 0.14f); }
            }
            // parked cars hugging the curb
            for (int zi = 0; zi < 4; zi++)
                if (rng.NextDouble() < 0.55)
                {
                    float side = rng.NextDouble() < 0.5 ? -1f : 1f;
                    var c = CModel(CCars[rng.Next(CCars.Length)], root.transform,
                        new Vector3(side * 4.15f, 0f, zi * 5.6f + 1.5f + (float)rng.NextDouble() * 2f), rng.NextDouble() < 0.5 ? 0f : 180f);
                    if (c != null) { FitModel(c, targetY: 1.05f); SnapToGround(c, 0.02f); }
                }
            // small sidewalk props baked in
            for (int zi = 0; zi < 6; zi++)
                foreach (var side in new[] { -1f, 1f })
                    if (rng.NextDouble() < 0.4)
                    {
                        var p = CModel(CSidewalk[rng.Next(CSidewalk.Length)], root.transform,
                            new Vector3(side * (5.1f + (float)rng.NextDouble() * 1.2f), 0f, zi * 4f + (float)rng.NextDouble() * 2f), rng.Next(360));
                        if (p != null) { FitModel(p, targetY: 0.5f + (float)rng.NextDouble() * 0.7f); SnapToGround(p, 0.14f); }
                    }
            return SavePrefab(root, name);
        }

        static GameObject MakeLowBarrier()
        {
            var root = new GameObject("LowBarrier");
            var m = DModel("barrier_half", root.transform, Vector3.zero);
            if (m != null) { FixDungeonMats(m); FitModel(m, targetX: 2.2f); }
            AddTrigger(root, new Vector3(0, 0.55f, 0), new Vector3(2.2f, 1.1f, 0.6f));
            root.AddComponent<Obstacle>().kind = ObstacleKind.LowBarrier;
            return SavePrefab(root, "LowBarrier");
        }

        static GameObject MakeSpikeTrap()
        {
            var root = new GameObject("SpikeTrap");
            var m = DModel("floor_tile_big_spikes", root.transform, new Vector3(0, -0.1f, 0));
            if (m != null)
            {
                FixDungeonMats(m);
                m.transform.localScale = new Vector3(0.58f, 0.62f, 0.32f); // lane-wide spike strip
            }
            AddTrigger(root, new Vector3(0, 0.55f, 0), new Vector3(2.25f, 1.1f, 1.3f));
            root.AddComponent<Obstacle>().kind = ObstacleKind.LowBarrier;
            return SavePrefab(root, "SpikeTrap");
        }

        static GameObject MakeHighBarrier()
        {
            var root = new GameObject("HighBarrier");
            // long table across the lane — slide underneath the tabletop
            var m = DModel("table_long", root.transform, Vector3.zero, 90f);
            if (m != null) { FixDungeonMats(m); m.transform.localScale = new Vector3(0.55f, 1f, 1f); }
            // kill zone only in the tabletop band — sliding clears it, a clean jump tops it
            AddTrigger(root, new Vector3(0, 0.92f, 0), new Vector3(2.2f, 0.4f, 1.6f));
            root.AddComponent<Obstacle>().kind = ObstacleKind.HighBarrier;
            return SavePrefab(root, "HighBarrier");
        }

        static GameObject MakeWall()
        {
            var root = new GameObject("WallBlock");
            var m = DModel("crates_stacked", root.transform, Vector3.zero);
            if (m != null) { FixDungeonMats(m); FitModel(m, targetX: 2.0f); }
            AddTrigger(root, new Vector3(0, 1.1f, 0), new Vector3(2.0f, 2.2f, 1.9f));
            root.AddComponent<Obstacle>().kind = ObstacleKind.WallBlock;
            return SavePrefab(root, "WallBlock");
        }

        static void AddTrigger(GameObject root, Vector3 center, Vector3 size)
        {
            var col = root.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.center = center;
            col.size = size;
        }

        static GameObject MakeCoin()
        {
            var root = new GameObject("Coin");
            var m = DModel("coin", root.transform, Vector3.zero, 0f, new Vector3(2.4f, 2.4f, 2.4f));
            if (m != null)
            {
                FixDungeonMats(m);
                m.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // stand the coin upright
                // centre the rendered coin on the prefab origin — the model pivot
                // sits at its base, which is what buried coins below the track
                m.transform.position -= ComputeLocalBounds(m).center;
            }
            var col = root.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = 0.55f;
            var coin = root.AddComponent<Coin>();
            coin.burstPrefab = coinBurstPrefab;
            return SavePrefab(root, "Coin");
        }

        // ---------- dungeon decor prefabs ----------

        /// <summary>Gentle idle sway baked onto a decor model — vegetation flutters, banners wave.</summary>
        static void AddSway(GameObject model, float deg, float freq = 1.1f, Vector3? axis = null)
        {
            if (model == null || deg <= 0f) return;
            var a = model.AddComponent<ObstacleAnim>();
            a.swayDeg = deg;
            a.swayFreq = freq;
            if (axis.HasValue) a.swayAxis = axis.Value;
        }

        static GameObject MakeDecor(string model, string name, float y = 0f, Vector3? scale = null, float rotY = 0f, float sway = 0f)
        {
            var root = new GameObject(name);
            var m = DModel(model, root.transform, new Vector3(0, y, 0), rotY, scale);
            if (m != null) { FixDungeonMats(m); AddSway(m, sway, 1.8f); }
            return SavePrefab(root, name);
        }

        static GameObject MakeLogBarrier() // fallen log across the lane — jump it
        {
            var root = new GameObject("LogBarrier");
            var m = KModel("log_large", root.transform, new Vector3(0, 0.4f, 0), 90f);
            if (m != null) { FitModel(m, targetX: 2.3f); FitModel(m, targetY: 0.9f); }
            AddTrigger(root, new Vector3(0, 0.55f, 0), new Vector3(2.2f, 1.1f, 0.9f));
            root.AddComponent<Obstacle>().kind = ObstacleKind.LowBarrier;
            return SavePrefab(root, "LogBarrier");
        }

        static GameObject MakeRockJump() // knee-high boulder — jump it
        {
            var root = new GameObject("RockJump");
            var m = KModel("cliff_half_rock", root.transform, Vector3.zero);
            if (m != null) { FitModel(m, targetX: 2.1f); FitModel(m, targetY: 1.1f); }
            AddTrigger(root, new Vector3(0, 0.55f, 0), new Vector3(2.1f, 1.1f, 1.1f));
            root.AddComponent<Obstacle>().kind = ObstacleKind.LowBarrier;
            return SavePrefab(root, "RockJump");
        }

        static GameObject MakeRockBlock() // tall boulder — dodge it
        {
            var root = new GameObject("RockBlock");
            var m = KModel("cliff_block_rock", root.transform, Vector3.zero);
            if (m != null) { FitModel(m, targetX: 2f); FitModel(m, targetY: 2.4f); }
            AddTrigger(root, new Vector3(0, 1.1f, 0), new Vector3(2f, 2.2f, 1.3f));
            root.AddComponent<Obstacle>().kind = ObstacleKind.WallBlock;
            return SavePrefab(root, "RockBlock");
        }

        static GameObject MakeCactusBlock() // tall cactus — dodge it
        {
            var root = new GameObject("CactusBlock");
            var m = KModel("cactus_tall", root.transform, Vector3.zero);
            if (m != null) { FitModel(m, targetX: 1.9f); FitModel(m, targetY: 2.5f); }
            AddTrigger(root, new Vector3(0, 1.15f, 0), new Vector3(1.9f, 2.3f, 1f));
            root.AddComponent<Obstacle>().kind = ObstacleKind.WallBlock;
            return SavePrefab(root, "CactusBlock");
        }

        // ---------- powerup pickups ----------

        static GameObject MakePowerUpMagnet() // classic red horseshoe magnet
        {
            var root = new GameObject("PU_Magnet");
            Prim(PrimitiveType.Cube, "Arch", mats["MagnetRed"], root.transform, new Vector3(0, 0.4f, 0), new Vector3(0.62f, 0.16f, 0.18f));
            Prim(PrimitiveType.Cube, "ArmL", mats["MagnetRed"], root.transform, new Vector3(-0.23f, 0.12f, 0), new Vector3(0.16f, 0.42f, 0.18f));
            Prim(PrimitiveType.Cube, "ArmR", mats["MagnetRed"], root.transform, new Vector3(0.23f, 0.12f, 0), new Vector3(0.16f, 0.42f, 0.18f));
            Prim(PrimitiveType.Cube, "TipL", mats["MagnetTip"], root.transform, new Vector3(-0.23f, -0.13f, 0), new Vector3(0.2f, 0.12f, 0.22f));
            Prim(PrimitiveType.Cube, "TipR", mats["MagnetTip"], root.transform, new Vector3(0.23f, -0.13f, 0), new Vector3(0.2f, 0.12f, 0.22f));
            FinishPowerUp(root, PowerUpKind.Magnet);
            return SavePrefab(root, "PU_Magnet");
        }

        static GameObject MakePowerUpShield() // golden sword-and-shield crest
        {
            var root = new GameObject("PU_Shield");
            var m = DModel("sword_shield_gold", root.transform, Vector3.zero, 0f, new Vector3(1.5f, 1.5f, 1.5f));
            if (m != null)
            {
                FixDungeonMats(m);
                m.transform.position -= ComputeLocalBounds(m).center; // centre on origin
            }
            FinishPowerUp(root, PowerUpKind.Shield);
            return SavePrefab(root, "PU_Shield");
        }

        static GameObject MakePowerUpBoost() // glowing energy orb with a lightning core
        {
            var root = new GameObject("PU_Boost");
            Prim(PrimitiveType.Sphere, "Orb", mats["BoostOrb"], root.transform, Vector3.zero, new Vector3(0.6f, 0.6f, 0.6f));
            var bolt = Prim(PrimitiveType.Cube, "Bolt", mats["BoostCore"], root.transform, new Vector3(0.04f, 0f, 0.18f), new Vector3(0.09f, 0.5f, 0.09f));
            bolt.transform.localRotation = Quaternion.Euler(0f, 0f, -22f);
            FinishPowerUp(root, PowerUpKind.Boost);
            return SavePrefab(root, "PU_Boost");
        }

        static void FinishPowerUp(GameObject root, PowerUpKind kind)
        {
            var col = root.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = 0.7f;
            var pu = root.AddComponent<PowerUp>();
            pu.kind = kind;
            pu.burstPrefab = coinBurstPrefab;
            // vehicle pickups carry a random paint palette — fresh coat per spawn,
            // and the mounted vehicle inherits the exact colors you saw
            if (kind == PowerUpKind.Car || kind == PowerUpKind.Plane)
                root.AddComponent<VehicleTint>();
        }

        // ---------- special vehicles ----------

        /// <summary>
        /// Detailed GT car from primitives: sculpted hull, glasshouse, GT wing,
        /// splitter/diffuser, projector lights, dual chrome exhausts and four
        /// wheels under Wheel_* pivots (spun by SpecialVehicleController).
        /// Forward is +Z, ground plane is y=0.
        /// </summary>
        static void BuildCarBody(Transform parent)
        {
            var paint = mats["CarPaint"];
            var dark = mats["CarTrim"];
            var glass = mats["CarGlass"];
            var tire = mats["TireRubber"];
            var chrome = mats["Chrome"];

            // monocoque hull + sloped hood and rear deck
            Prim(PrimitiveType.Cube, "Hull", paint, parent, new Vector3(0f, 0.50f, 0f), new Vector3(1.78f, 0.42f, 4.0f));
            var hood = Prim(PrimitiveType.Cube, "Hood", paint, parent, new Vector3(0f, 0.70f, 1.45f), new Vector3(1.66f, 0.12f, 1.50f));
            hood.transform.localRotation = Quaternion.Euler(-7f, 0f, 0f);
            var deck = Prim(PrimitiveType.Cube, "Deck", paint, parent, new Vector3(0f, 0.72f, -1.55f), new Vector3(1.66f, 0.12f, 1.30f));
            deck.transform.localRotation = Quaternion.Euler(4f, 0f, 0f);

            // glasshouse: windshield, roof, rear glass, side windows
            var shield = Prim(PrimitiveType.Cube, "Windshield", glass, parent, new Vector3(0f, 0.93f, 0.52f), new Vector3(1.34f, 0.05f, 0.88f));
            shield.transform.localRotation = Quaternion.Euler(-32f, 0f, 0f);
            Prim(PrimitiveType.Cube, "Roof", paint, parent, new Vector3(0f, 1.10f, -0.30f), new Vector3(1.36f, 0.09f, 1.05f));
            var rearGlass = Prim(PrimitiveType.Cube, "RearGlass", glass, parent, new Vector3(0f, 0.99f, -1.10f), new Vector3(1.30f, 0.05f, 0.72f));
            rearGlass.transform.localRotation = Quaternion.Euler(38f, 0f, 0f);
            Prim(PrimitiveType.Cube, "WinL", glass, parent, new Vector3(-0.68f, 0.95f, -0.28f), new Vector3(0.05f, 0.30f, 0.92f));
            Prim(PrimitiveType.Cube, "WinR", glass, parent, new Vector3(0.68f, 0.95f, -0.28f), new Vector3(0.05f, 0.30f, 0.92f));

            // aero trim: splitter, grille, side skirts, diffuser, mirrors
            Prim(PrimitiveType.Cube, "Splitter", dark, parent, new Vector3(0f, 0.26f, 2.02f), new Vector3(1.86f, 0.14f, 0.30f));
            Prim(PrimitiveType.Cube, "Grille", dark, parent, new Vector3(0f, 0.46f, 2.03f), new Vector3(1.10f, 0.16f, 0.08f));
            Prim(PrimitiveType.Cube, "SkirtL", dark, parent, new Vector3(-0.92f, 0.24f, 0f), new Vector3(0.14f, 0.16f, 2.70f));
            Prim(PrimitiveType.Cube, "SkirtR", dark, parent, new Vector3(0.92f, 0.24f, 0f), new Vector3(0.14f, 0.16f, 2.70f));
            Prim(PrimitiveType.Cube, "Diffuser", dark, parent, new Vector3(0f, 0.26f, -2.02f), new Vector3(1.70f, 0.20f, 0.26f));
            Prim(PrimitiveType.Cube, "MirrorL", paint, parent, new Vector3(-0.98f, 0.92f, 0.42f), new Vector3(0.16f, 0.09f, 0.14f));
            Prim(PrimitiveType.Cube, "MirrorR", paint, parent, new Vector3(0.98f, 0.92f, 0.42f), new Vector3(0.16f, 0.09f, 0.14f));

            // lights: angled projectors up front, full-width bar at the rear
            var hl = Prim(PrimitiveType.Cube, "HeadL", mats["Headlight"], parent, new Vector3(-0.58f, 0.66f, 1.98f), new Vector3(0.44f, 0.12f, 0.10f));
            hl.transform.localRotation = Quaternion.Euler(0f, 14f, 0f);
            var hr = Prim(PrimitiveType.Cube, "HeadR", mats["Headlight"], parent, new Vector3(0.58f, 0.66f, 1.98f), new Vector3(0.44f, 0.12f, 0.10f));
            hr.transform.localRotation = Quaternion.Euler(0f, -14f, 0f);
            Prim(PrimitiveType.Cube, "TailBar", mats["Taillight"], parent, new Vector3(0f, 0.66f, -2.01f), new Vector3(1.60f, 0.10f, 0.08f));

            // GT wing on twin pylons
            Prim(PrimitiveType.Cube, "WingPylonL", dark, parent, new Vector3(-0.52f, 0.95f, -1.82f), new Vector3(0.08f, 0.34f, 0.12f));
            Prim(PrimitiveType.Cube, "WingPylonR", dark, parent, new Vector3(0.52f, 0.95f, -1.82f), new Vector3(0.08f, 0.34f, 0.12f));
            var wing = Prim(PrimitiveType.Cube, "Wing", dark, parent, new Vector3(0f, 1.16f, -1.88f), new Vector3(1.72f, 0.07f, 0.46f));
            wing.transform.localRotation = Quaternion.Euler(-8f, 0f, 0f);

            // dual chrome exhausts
            var ex1 = Prim(PrimitiveType.Cylinder, "ExhaustL", chrome, parent, new Vector3(-0.34f, 0.34f, -2.06f), new Vector3(0.16f, 0.12f, 0.16f));
            ex1.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var ex2 = Prim(PrimitiveType.Cylinder, "ExhaustR", chrome, parent, new Vector3(0.34f, 0.34f, -2.06f), new Vector3(0.16f, 0.12f, 0.16f));
            ex2.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // neon underglow sheet under the chassis — tinted per spawn
            Prim(PrimitiveType.Cube, "UnderGlow", mats["UnderGlow"], parent, new Vector3(0f, 0.07f, 0f), new Vector3(2.9f, 0.02f, 4.8f));

            // wheels — pivot GO is the spun axle (local X), tire + chrome rim
            MakeWheel(parent, "Wheel_FL", new Vector3(-0.84f, 0.36f, 1.32f), tire, chrome);
            MakeWheel(parent, "Wheel_FR", new Vector3(0.84f, 0.36f, 1.32f), tire, chrome);
            MakeWheel(parent, "Wheel_RL", new Vector3(-0.84f, 0.36f, -1.32f), tire, chrome);
            MakeWheel(parent, "Wheel_RR", new Vector3(0.84f, 0.36f, -1.32f), tire, chrome);
        }

        static void MakeWheel(Transform parent, string name, Vector3 pos, Material tire, Material rim)
        {
            var pivot = new GameObject(name);
            pivot.transform.SetParent(parent, false);
            pivot.transform.localPosition = pos;
            var t = Prim(PrimitiveType.Cylinder, "Tire", tire, pivot.transform, Vector3.zero, new Vector3(0.72f, 0.15f, 0.72f));
            t.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            float outward = pos.x > 0f ? 0.02f : -0.02f;
            var r = Prim(PrimitiveType.Cylinder, "Rim", rim, pivot.transform, new Vector3(outward, 0f, 0f), new Vector3(0.40f, 0.17f, 0.40f));
            r.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        }

        /// <summary>
        /// Prop-driven sport plane: capsule fuselage, cowl + 3-blade propeller
        /// (PropSpin + translucent blur disc), low wing with accent tips and
        /// nav lights, tailplane + fin, canopy, fixed gear and exhaust stubs.
        /// Forward is +Z, ground plane is y=0.
        /// </summary>
        static void BuildPlaneBody(Transform parent)
        {
            var paint = mats["PlanePaint"];
            var accent = mats["PlaneAccent"];
            var dark = mats["CarTrim"];
            var glass = mats["PlaneGlass"];

            // fuselage running along +Z
            var fus = Prim(PrimitiveType.Capsule, "Fuselage", paint, parent, new Vector3(0f, 0.78f, 0f), new Vector3(1.0f, 2.1f, 1.0f));
            fus.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // cowl + spinner + 3-blade propeller on a PropSpin pivot
            var cowl = Prim(PrimitiveType.Cylinder, "Cowl", accent, parent, new Vector3(0f, 0.78f, 1.92f), new Vector3(0.86f, 0.22f, 0.86f));
            cowl.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var prop = new GameObject("Prop");
            prop.transform.SetParent(parent, false);
            prop.transform.localPosition = new Vector3(0f, 0.78f, 2.28f);
            var spin = prop.AddComponent<PropSpin>();
            spin.axis = Vector3.forward;
            spin.degreesPerSecond = 1500f;
            Prim(PrimitiveType.Sphere, "Spinner", accent, prop.transform, Vector3.zero, new Vector3(0.34f, 0.34f, 0.42f));
            for (int i = 0; i < 3; i++)
            {
                var arm = new GameObject("BladeArm" + i);
                arm.transform.SetParent(prop.transform, false);
                arm.transform.localRotation = Quaternion.Euler(0f, 0f, i * 120f);
                Prim(PrimitiveType.Cube, "Blade", dark, arm.transform, new Vector3(0f, 0.60f, 0f), new Vector3(0.17f, 1.02f, 0.06f));
                Prim(PrimitiveType.Cube, "BladeTip", accent, arm.transform, new Vector3(0f, 1.02f, 0f), new Vector3(0.18f, 0.18f, 0.07f));
            }
            var disc = Prim(PrimitiveType.Cylinder, "PropBlur", mats["PropBlur"], prop.transform, Vector3.zero, new Vector3(2.3f, 0.003f, 2.3f));
            disc.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // teardrop canopy over the cockpit
            Prim(PrimitiveType.Sphere, "Canopy", glass, parent, new Vector3(0f, 1.18f, 0.55f), new Vector3(0.62f, 0.46f, 1.15f));

            // low wing with accent tip blocks
            Prim(PrimitiveType.Cube, "WingL", paint, parent, new Vector3(-1.30f, 0.62f, 0.55f), new Vector3(2.55f, 0.11f, 1.05f));
            Prim(PrimitiveType.Cube, "WingR", paint, parent, new Vector3(1.30f, 0.62f, 0.55f), new Vector3(2.55f, 0.11f, 1.05f));
            Prim(PrimitiveType.Cube, "TipL", accent, parent, new Vector3(-2.52f, 0.62f, 0.55f), new Vector3(0.48f, 0.13f, 1.05f));
            Prim(PrimitiveType.Cube, "TipR", accent, parent, new Vector3(2.52f, 0.62f, 0.55f), new Vector3(0.48f, 0.13f, 1.05f));

            // tailplane + vertical fin
            Prim(PrimitiveType.Cube, "HStabL", paint, parent, new Vector3(-0.48f, 0.94f, -1.72f), new Vector3(0.95f, 0.09f, 0.60f));
            Prim(PrimitiveType.Cube, "HStabR", paint, parent, new Vector3(0.48f, 0.94f, -1.72f), new Vector3(0.95f, 0.09f, 0.60f));
            Prim(PrimitiveType.Cube, "Fin", accent, parent, new Vector3(0f, 1.28f, -1.78f), new Vector3(0.09f, 0.78f, 0.70f));

            // nav lights — red port / green starboard / white tail
            Prim(PrimitiveType.Cube, "NavL", mats["NavRed"], parent, new Vector3(-2.74f, 0.62f, 0.55f), new Vector3(0.10f, 0.10f, 0.14f));
            Prim(PrimitiveType.Cube, "NavR", mats["NavGreen"], parent, new Vector3(2.74f, 0.62f, 0.55f), new Vector3(0.10f, 0.10f, 0.14f));
            Prim(PrimitiveType.Cube, "NavTail", mats["Headlight"], parent, new Vector3(0f, 1.30f, -2.14f), new Vector3(0.08f, 0.08f, 0.08f));

            // fixed tricycle-ish gear — main pair + tail wheel
            foreach (var sx in new[] { -1f, 1f })
            {
                var strut = Prim(PrimitiveType.Cube, "GearStrut", dark, parent, new Vector3(sx * 0.55f, 0.36f, 0.78f), new Vector3(0.07f, 0.55f, 0.10f));
                strut.transform.localRotation = Quaternion.Euler(0f, 0f, sx * -12f);
                var w = Prim(PrimitiveType.Cylinder, "GearWheel", mats["TireRubber"], parent, new Vector3(sx * 0.66f, 0.17f, 0.78f), new Vector3(0.34f, 0.06f, 0.34f));
                w.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }
            Prim(PrimitiveType.Sphere, "TailWheel", dark, parent, new Vector3(0f, 0.16f, -1.92f), new Vector3(0.16f, 0.16f, 0.16f));

            // exhaust stubs angled under the cowl
            var exa = Prim(PrimitiveType.Cylinder, "ExhaustStubL", dark, parent, new Vector3(-0.40f, 0.50f, 1.86f), new Vector3(0.10f, 0.16f, 0.10f));
            exa.transform.localRotation = Quaternion.Euler(65f, 0f, 0f);
            var exb = Prim(PrimitiveType.Cylinder, "ExhaustStubR", dark, parent, new Vector3(0.40f, 0.50f, 1.86f), new Vector3(0.10f, 0.16f, 0.10f));
            exb.transform.localRotation = Quaternion.Euler(65f, 0f, 0f);

            // glow sheet under the belly — tinted per spawn
            Prim(PrimitiveType.Cube, "UnderGlow", mats["UnderGlow"], parent, new Vector3(0f, 0.10f, 0.1f), new Vector3(2.4f, 0.02f, 4.7f));
        }

        // ---------- vehicle FX ----------

        /// <summary>Stretched streaks blasting past the runner — sells raw speed.</summary>
        static ParticleSystem SetupSpeedLines(Transform parent)
        {
            var go = new GameObject("SpeedLines");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 1.7f, 2f);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.startLifetime = 0.45f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1.4f, 1.3f, 1.1f, 0.55f), new Color(0.65f, 1.0f, 1.4f, 0.55f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 220;

            var em = ps.emission;
            em.rateOverTime = 120f;

            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(11f, 5f, 16f);

            // particles stream backwards in world space — the camera chases through them
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0f);
            vel.y = new ParticleSystem.MinMaxCurve(0f);
            vel.z = new ParticleSystem.MinMaxCurve(-44f);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.8f, 0.35f), new GradientAlphaKey(0f, 1f) });
            col.color = g;

            var rd = ps.GetComponent<ParticleSystemRenderer>();
            rd.material = dustMat;
            rd.renderMode = ParticleSystemRenderMode.Stretch;
            rd.velocityScale = 0.04f;
            rd.lengthScale = 0.4f;
            rd.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return ps;
        }

        /// <summary>Gray puffs trailing from the dual exhausts.</summary>
        static ParticleSystem SetupExhaust(Transform parent)
        {
            var go = new GameObject("ExhaustPuffs");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0.34f, -2.15f);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.26f);
            main.startColor = new Color(0.36f, 0.35f, 0.34f, 0.5f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 90;

            var em = ps.emission;
            em.rateOverTime = 30f;

            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = 0.12f;

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.y = new ParticleSystem.MinMaxCurve(0.9f);
            vel.z = new ParticleSystem.MinMaxCurve(-5f);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(new Color(0.4f, 0.39f, 0.38f), 0f), new GradientColorKey(new Color(0.55f, 0.55f, 0.55f), 1f) },
                new[] { new GradientAlphaKey(0.5f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.0f));

            var rd = ps.GetComponent<ParticleSystemRenderer>();
            rd.material = dustMat;
            rd.renderMode = ParticleSystemRenderMode.Billboard;
            rd.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return ps;
        }

        // ---------- vehicle pickups ----------

        static GameObject MakePowerUpCar() // miniature of the GT car
        {
            var root = new GameObject("PU_Car");
            var mesh = new GameObject("Mesh");
            mesh.transform.SetParent(root.transform, false);
            BuildCarBody(mesh.transform);
            mesh.transform.localScale = Vector3.one * 0.40f;
            mesh.transform.localPosition = new Vector3(0f, -0.24f, 0f); // centre on the pickup pivot
            FinishPowerUp(root, PowerUpKind.Car);
            return SavePrefab(root, "PU_Car");
        }

        static GameObject MakePowerUpPlane() // miniature of the sport plane — its prop spins too
        {
            var root = new GameObject("PU_Plane");
            var mesh = new GameObject("Mesh");
            mesh.transform.SetParent(root.transform, false);
            BuildPlaneBody(mesh.transform);
            mesh.transform.localScale = Vector3.one * 0.32f; // ~1.7m wingspan pickup
            mesh.transform.localPosition = new Vector3(0f, -0.24f, 0f);
            FinishPowerUp(root, PowerUpKind.Plane);
            return SavePrefab(root, "PU_Plane");
        }

        // ---- lagoon water-slide zone ----

        static GameObject MakeWaterTile() // pale flume channel with a flowing water film
        {
            var root = new GameObject("TrackTile");
            // slide channel floor — the runner's surface (collider kept)
            Prim(PrimitiveType.Cube, "SlideFloor", mats["SlideFloor"], root.transform, new Vector3(0, -0.28f, 12), new Vector3(9.4f, 0.6f, 24f), keepCollider: true);
            // shallow translucent water film across the whole channel
            Prim(PrimitiveType.Cube, "WaterFilm", mats["WaterFlow"], root.transform, new Vector3(0, 0.035f, 12), new Vector3(8.0f, 0.04f, 24f));
            // deeper runnels marking each lane
            foreach (var x in new[] { -2.2f, 0f, 2.2f })
                Prim(PrimitiveType.Cube, "Runnel", mats["WaterDeep"], root.transform, new Vector3(x, 0.05f, 12), new Vector3(0.55f, 0.02f, 24f));
            // flume walls — the water-slide half-pipe sides
            Prim(PrimitiveType.Cube, "FlumeL", mats["SlideWall"], root.transform, new Vector3(-4.6f, 0.55f, 12), new Vector3(0.55f, 1.5f, 24f));
            Prim(PrimitiveType.Cube, "FlumeR", mats["SlideWall"], root.transform, new Vector3(4.6f, 0.55f, 12), new Vector3(0.55f, 1.5f, 24f));
            // rounded lips along the wall tops
            var lipL = Prim(PrimitiveType.Cylinder, "LipL", mats["SlideWall"], root.transform, new Vector3(-4.35f, 1.3f, 12), new Vector3(0.55f, 12f, 0.55f));
            lipL.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var lipR = Prim(PrimitiveType.Cylinder, "LipR", mats["SlideWall"], root.transform, new Vector3(4.35f, 1.3f, 12), new Vector3(0.55f, 12f, 0.55f));
            lipR.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            // sandy bed under and around the flume — collidable so beach props snap to it
            Prim(PrimitiveType.Cube, "UnderBed", mats["SandGround"], root.transform, new Vector3(0, -0.75f, 12), new Vector3(64f, 0.5f, 24f), keepCollider: true);
            // baked tropical edge — dense palm clusters + rocks + flowers on the sand
            var rng = new System.Random(97);
            for (int zi = 0; zi < 9; zi++)
                foreach (var side in new[] { -1f, 1f })
                {
                    if (rng.NextDouble() < 0.8)
                    {
                        var p = QModel(QPalm[rng.Next(QPalm.Length)], root.transform,
                            new Vector3(side * (5.8f + (float)rng.NextDouble() * 2.6f), 0f, zi * 2.7f + (float)rng.NextDouble() * 1.4f), rng.Next(360));
                        if (p != null) { FitModel(p, targetY: 3.4f + (float)rng.NextDouble() * 2.2f); SnapToGround(p, -0.5f); }
                    }
                    if (rng.NextDouble() < 0.5)
                    {
                        var p2 = QModel(QPalm[rng.Next(QPalm.Length)], root.transform,
                            new Vector3(side * (9.5f + (float)rng.NextDouble() * 3f), 0f, zi * 2.7f + 1.3f + (float)rng.NextDouble()), rng.Next(360));
                        if (p2 != null) { FitModel(p2, targetY: 4.6f + (float)rng.NextDouble() * 2.4f); SnapToGround(p2, -0.5f); }
                    }
                }
            for (int zi = 0; zi < 10; zi++)
                foreach (var side in new[] { -1f, 1f })
                    if (rng.NextDouble() < 0.5)
                    {
                        var b = QModel(new[] { "Bush", "Bush_Flowers", "Plant_Flowers", "Flower_2_Clump", "Flower_4_Clump", "Rock_2", "Rock_4" }[rng.Next(7)], root.transform,
                            new Vector3(side * (5.4f + (float)rng.NextDouble() * 4f), 0f, zi * 2.4f + (float)rng.NextDouble()), rng.Next(360));
                        if (b != null) { FitModel(b, targetY: 0.4f + (float)rng.NextDouble() * 0.8f); SnapToGround(b, -0.5f); }
                    }
            // distant beach skyline — giant palms + dune mounds softening the horizon
            for (int zi = 0; zi < 4; zi++)
                foreach (var side in new[] { -1f, 1f })
                {
                    if (rng.NextDouble() < 0.8)
                    {
                        var h = QModel(QPalm[rng.Next(QPalm.Length)], root.transform,
                            new Vector3(side * (13.5f + (float)rng.NextDouble() * 6f), 0f, zi * 6f + (float)rng.NextDouble() * 3f), rng.Next(360));
                        if (h != null) { FitModel(h, targetY: 7f + (float)rng.NextDouble() * 3.5f); SnapToGround(h, -0.6f); AddSway(h, 1.2f, 0.7f); }
                    }
                    if (rng.NextDouble() < 0.55)
                        BuildDune(root.transform,
                            new Vector3(side * (11f + (float)rng.NextDouble() * 7f), -0.45f, zi * 6f + 3f + (float)rng.NextDouble() * 2f),
                            2.4f + (float)rng.NextDouble() * 2f, (float)rng.NextDouble() * 360f);
                }
            return SavePrefab(root, "Tile_Water");
        }

        static GameObject MakeWaveBarrier() // curling wave crest — jump it
        {
            var root = new GameObject("WaveBarrier");
            var body = new GameObject("Body"); // visuals rock here; trigger stays on root
            body.transform.SetParent(root.transform, false);
            var crest = Prim(PrimitiveType.Cylinder, "Crest", mats["WaterDeep"], body.transform, new Vector3(0, 0.55f, 0), new Vector3(1.15f, 1.15f, 1.15f));
            crest.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            // foam along the crest lip
            Prim(PrimitiveType.Sphere, "Foam1", mats["Foam"], body.transform, new Vector3(-0.85f, 1.10f, 0), new Vector3(0.5f, 0.3f, 0.9f));
            Prim(PrimitiveType.Sphere, "Foam2", mats["Foam"], body.transform, new Vector3(-0.3f, 1.18f, 0), new Vector3(0.6f, 0.35f, 1.0f));
            Prim(PrimitiveType.Sphere, "Foam3", mats["Foam"], body.transform, new Vector3(0.3f, 1.14f, 0), new Vector3(0.55f, 0.32f, 0.95f));
            Prim(PrimitiveType.Sphere, "Foam4", mats["Foam"], body.transform, new Vector3(0.85f, 1.05f, 0), new Vector3(0.45f, 0.28f, 0.85f));
            AddTrigger(root, new Vector3(0, 0.55f, 0), new Vector3(2.2f, 1.1f, 1.0f));
            root.AddComponent<Obstacle>().kind = ObstacleKind.LowBarrier;
            var a = Animate(root, body.transform); // the crest rolls and surges
            a.squashAmp = 0.12f; a.squashFreq = 2.2f; a.swayDeg = 5f; a.swayFreq = 2.2f;
            return SavePrefab(root, "W_Wave");
        }

        static GameObject MakeBuoyBlock() // striped channel-marker buoy — dodge it
        {
            var root = new GameObject("BuoyBlock");
            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);
            Prim(PrimitiveType.Cylinder, "Base", mats["BuoyRed"], body.transform, new Vector3(0, 0.10f, 0), new Vector3(1.4f, 0.10f, 1.4f));
            Prim(PrimitiveType.Cylinder, "Band1", mats["BuoyWhite"], body.transform, new Vector3(0, 0.55f, 0), new Vector3(0.95f, 0.35f, 0.95f));
            Prim(PrimitiveType.Cylinder, "Band2", mats["BuoyRed"], body.transform, new Vector3(0, 1.05f, 0), new Vector3(0.85f, 0.35f, 0.85f));
            Prim(PrimitiveType.Cylinder, "Band3", mats["BuoyWhite"], body.transform, new Vector3(0, 1.50f, 0), new Vector3(0.7f, 0.3f, 0.7f));
            Prim(PrimitiveType.Cylinder, "Neck", mats["BuoyRed"], body.transform, new Vector3(0, 1.80f, 0), new Vector3(0.42f, 0.35f, 0.42f));
            MeshGO(coneMesh, "Cap", mats["BuoyRed"], body.transform, new Vector3(0, 1.95f, 0), new Vector3(0.35f, 0.5f, 0.35f));
            AddTrigger(root, new Vector3(0, 1.10f, 0), new Vector3(1.5f, 2.2f, 1.5f));
            root.AddComponent<Obstacle>().kind = ObstacleKind.WallBlock;
            var a = Animate(root, body.transform); // riding the swell
            a.bobAmp = 0.10f; a.bobFreq = 2.0f; a.swayDeg = 8f; a.swayFreq = 1.8f; a.rockDeg = 6f; a.rockFreq = 1.4f;
            return SavePrefab(root, "W_Buoy");
        }

        static GameObject MakeGeyserBlock() // erupting water jet — dodge it
        {
            var root = new GameObject("GeyserBlock");
            var jet = Prim(PrimitiveType.Cylinder, "Jet", mats["WaterFlow"], root.transform, new Vector3(0, 1.2f, 0), new Vector3(1.0f, 1.15f, 1.0f));
            var splash = Prim(PrimitiveType.Sphere, "Splash", mats["Foam"], root.transform, new Vector3(0, 0.16f, 0), new Vector3(1.9f, 0.32f, 1.9f));
            var cap = Prim(PrimitiveType.Sphere, "CapFoam", mats["Foam"], root.transform, new Vector3(0, 2.4f, 0), new Vector3(1.35f, 0.4f, 1.35f));
            Prim(PrimitiveType.Sphere, "CapFoam2", mats["Foam"], root.transform, new Vector3(0.25f, 2.65f, 0), new Vector3(0.7f, 0.3f, 0.7f));
            AddTrigger(root, new Vector3(0, 1.15f, 0), new Vector3(1.6f, 2.3f, 1.6f));
            root.AddComponent<Obstacle>().kind = ObstacleKind.WallBlock;
            var a = Animate(root, jet.transform); // the column pumps and surges
            a.squashAmp = 0.22f; a.squashFreq = 1.6f;
            var a2 = Animate(root, cap.transform); // crown of foam bobs with it
            a2.bobAmp = 0.22f; a2.bobFreq = 1.6f;
            var a3 = Animate(root, splash.transform); // base wash breathes
            a3.squashAmp = 0.14f; a3.squashFreq = 1.6f;
            return SavePrefab(root, "W_Geyser");
        }

        static GameObject MakeDecorN(string model, string name, float targetY, float sway = 0f)
        {
            var root = new GameObject(name);
            var m = NModel(model, root.transform, Vector3.zero);
            if (m != null)
            {
                if (targetY > 0f) FitModel(m, targetY: targetY);
                AddSway(m, sway, 0.9f);
            }
            return SavePrefab(root, name);
        }

        static GameObject MakeDecorQ(string model, string name, float targetY, string rockTex = null, float sway = 0f)
        {
            var root = new GameObject(name);
            var m = QModel(model, root.transform, Vector3.zero, 0f, rockTex: rockTex);
            if (m != null)
            {
                if (targetY > 0f) FitModel(m, targetY: targetY);
                AddSway(m, sway);
            }
            return SavePrefab(root, name);
        }

        static GameObject MakeDecorC(string model, string name, float targetY, float sway = 0f)
        {
            var root = new GameObject(name);
            var m = CModel(model, root.transform, Vector3.zero);
            if (m != null)
            {
                if (targetY > 0f) FitModel(m, targetY: targetY);
                AddSway(m, sway, 1.4f);
            }
            return SavePrefab(root, name);
        }

        static GameObject MakeDecor2(string model, string name, float y = 0f, Vector3? scale = null, float sway = 0f)
        {
            var root = new GameObject(name);
            var m = DModel("D2:" + model, root.transform, new Vector3(0f, y, 0f), 0f, scale);
            AddSway(m, sway, 1.8f);
            return SavePrefab(root, name);
        }

        // city obstacles — bench to jump, dumpster/crates/parked car to dodge
        static GameObject MakeBenchBlock()
        {
            var root = new GameObject("C_Bench");
            var m = CModel("bench", root.transform, Vector3.zero, 0f);
            if (m != null) FitModel(m, targetY: 1.35f);
            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.55f, 0f);
            box.size = new Vector3(2.0f, 1.1f, 1.0f);
            return SavePrefab(root, "C_Bench");
        }

        static GameObject MakeDumpsterBlock()
        {
            var root = new GameObject("C_Dumpster");
            var m = CModel("dumpster", root.transform, Vector3.zero, 0f);
            if (m != null) FitModel(m, targetY: 1.6f);
            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.75f, 0f);
            box.size = new Vector3(2.1f, 1.45f, 1.3f);
            return SavePrefab(root, "C_Dumpster");
        }

        static GameObject MakeCrateStack()
        {
            var root = new GameObject("C_Crates");
            var a = CModel("box_A", root.transform, new Vector3(-0.3f, 0f, 0f), 15f);
            var bb = CModel("box_B", root.transform, new Vector3(0.35f, 0f, 0.15f), -20f);
            var c = CModel("box_A", root.transform, new Vector3(0f, 1.05f, -0.3f), 60f);
            if (a != null) FitModel(a, targetY: 0.75f);
            if (bb != null) FitModel(bb, targetY: 0.9f);
            if (c != null) FitModel(c, targetY: 0.75f);
            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.85f, 0f);
            box.size = new Vector3(1.9f, 1.65f, 1.7f);
            return SavePrefab(root, "C_Crates");
        }

        static GameObject MakeCarBlock()
        {
            var root = new GameObject("C_CarBlock");
            var m = CModel("car_hatchback", root.transform, Vector3.zero, 90f); // sideways across the lane
            if (m != null) FitModel(m, targetY: 1.0f);
            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.55f, 0f);
            box.size = new Vector3(2.2f, 1.05f, 1.5f);

            return SavePrefab(root, "C_CarBlock");
        }

        // forest obstacle: mossy boulder — solid lane blocker
        static GameObject MakeStumpBlock()
        {
            var root = new GameObject("N_Stump");
            var m = QModel("Rock_1", root.transform, Vector3.zero, 0f, rockTex: "Rocks_Dark_Green");
            if (m != null) FitModel(m, targetY: 1.15f);
            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.6f, 0f);
            box.size = new Vector3(1.4f, 1.15f, 1.4f);
            return SavePrefab(root, "N_Stump");
        }

        // forest obstacle: fallen dead tree across the lane — jump it
        static GameObject MakeDeadFall()
        {
            var root = new GameObject("N_DeadFall");
            var m = QModel("DeadTree_9", root.transform, new Vector3(0f, 0.85f, 0f), 0f);
            if (m != null) { FitModel(m, targetY: 2.6f); m.transform.localRotation = Quaternion.Euler(0f, 0f, 86f); }
            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.85f, 0f);
            box.size = new Vector3(2.1f, 1.15f, 0.9f);

            return SavePrefab(root, "N_DeadFall");
        }

        static GameObject MakeDecorKFit(string model, string name, float targetY, float sway = 0f)
        {
            var root = new GameObject(name);
            var m = KModel(model, root.transform, Vector3.zero);
            if (m != null)
            {
                if (targetY > 0f) FitModel(m, targetY: targetY);
                AddSway(m, sway, 0.9f);
            }
            return SavePrefab(root, name);
        }

        // ---- vehicle glow: point light + orbiting aura sparks, tinted at runtime ----

        static void AttachGlow(Transform rig)
        {
            var lg = new GameObject("GlowLight");
            lg.transform.SetParent(rig, false);
            lg.transform.localPosition = new Vector3(0f, 0.85f, 0f);
            var l = lg.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = Hex("#FFB43A");
            l.intensity = 1.9f;
            l.range = 6.5f;
            l.shadows = LightShadows.None;
            SetupAura(rig);
        }

        static ParticleSystem SetupAura(Transform parent) // soft sparks swirling around the vehicle
        {
            var go = new GameObject("AuraSparks");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0.8f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.duration = 2f;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.7f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.13f);
            main.startColor = new Color(1f, 0.7f, 0.2f, 0.5f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 70;
            var em = ps.emission;
            em.rateOverTime = 22f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(2.6f, 1.5f, 4.6f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.5f;
            noise.frequency = 0.4f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
            var rd = ps.GetComponent<ParticleSystemRenderer>();
            rd.renderMode = ParticleSystemRenderMode.Billboard;
            rd.material = dustMat;
            return ps;
        }

        static GameObject MakeDecorK(string model, string name, Vector3? scale = null, float sway = 0f)
        {
            var root = new GameObject(name);
            var m = KModel(model, root.transform, Vector3.zero);
            if (m != null)
            {
                if (scale.HasValue) m.transform.localScale = scale.Value;
                AddSway(m, sway);
            }
            return SavePrefab(root, name);
        }

        static GameObject MakeCoinPile()
        {
            var root = new GameObject("CoinPile");
            var rng = new System.Random(7);
            for (int i = 0; i < 8; i++)
            {
                var c = DModel("coin", root.transform,
                    new Vector3((float)rng.NextDouble() * 1.7f - 0.85f, 0.05f + (i > 4 ? 0.12f : 0f), (float)rng.NextDouble() * 1.4f - 0.7f),
                    rng.Next(360), new Vector3(3f, 3f, 3f));
                if (c != null) FixDungeonMats(c);
            }
            return SavePrefab(root, "CoinPile");
        }

        // ---- egypt / desert dressing ----

        /// <summary>
        /// Low-poly pyramid: stacked sandstone steps (steps&gt;1) or a smooth shell
        /// (steps==1) with an optional gold pyramidion and a dark entrance slot.
        /// Used both for decor prefabs and baked tile silhouettes.
        /// </summary>
        static void BuildPyramid(Transform parent, Vector3 pos, float baseSize, float height, int steps, bool goldCap, float rotY = 0f)
        {
            var root = new GameObject("Pyramid");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = pos;
            root.transform.localRotation = Quaternion.Euler(0f, rotY, 0f);

            if (steps <= 1)
            {
                // smooth faces; cap base width matches the shell's cross-section at 82% height
                MeshGO(pyramidMesh, "Body", mats["PyramidSand"], root.transform, Vector3.zero,
                    new Vector3(baseSize * 0.7071f, height, baseSize * 0.7071f));
                if (goldCap)
                    MeshGO(pyramidMesh, "Cap", mats["GoldCap"], root.transform,
                        new Vector3(0f, height * 0.82f, 0f),
                        new Vector3(baseSize * 0.7071f * 0.18f, height * 0.18f, baseSize * 0.7071f * 0.18f));
                return;
            }

            float stepH = height / (steps + 1.6f);
            float topW = baseSize * 0.30f;
            for (int i = 0; i < steps; i++)
            {
                float w = Mathf.Lerp(baseSize, topW, i / (float)(steps - 1));
                Prim(PrimitiveType.Cube, "Step" + i, i % 2 == 0 ? mats["PyramidSand"] : mats["PyramidShade"],
                    root.transform, new Vector3(0f, stepH * (i + 0.5f), 0f), new Vector3(w, stepH + 0.02f, w));
            }
            if (goldCap)
                MeshGO(pyramidMesh, "Cap", mats["GoldCap"], root.transform,
                    new Vector3(0f, steps * stepH - 0.01f, 0f),
                    new Vector3(topW * 0.7071f, height - steps * stepH, topW * 0.7071f));
            // entrance slot on the front face
            Prim(PrimitiveType.Cube, "Door", mats["StoneDark"], root.transform,
                new Vector3(0f, stepH * 0.75f, -baseSize * 0.5f - 0.02f),
                new Vector3(baseSize * 0.16f, stepH * 1.3f, 0.06f));
        }

        /// <summary>Tapered sandstone obelisk: plinth, two-stage shaft, gold tip, glyph strip.</summary>
        static void BuildObelisk(Transform parent, Vector3 pos, float targetH, float rotY = 0f)
        {
            var g = new GameObject("Obelisk");
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localRotation = Quaternion.Euler(0f, rotY, 0f);
            Prim(PrimitiveType.Cube, "Base", mats["ObeliskSand"], g.transform, new Vector3(0f, 0.22f, 0f), new Vector3(1.05f, 0.44f, 1.05f));
            Prim(PrimitiveType.Cube, "Plinth", mats["PyramidShade"], g.transform, new Vector3(0f, 0.52f, 0f), new Vector3(0.86f, 0.18f, 0.86f));
            Prim(PrimitiveType.Cube, "ShaftA", mats["ObeliskSand"], g.transform, new Vector3(0f, 1.55f, 0f), new Vector3(0.62f, 1.9f, 0.62f));
            Prim(PrimitiveType.Cube, "ShaftB", mats["ObeliskSand"], g.transform, new Vector3(0f, 2.78f, 0f), new Vector3(0.5f, 0.55f, 0.5f));
            MeshGO(pyramidMesh, "Tip", mats["GoldCap"], g.transform, new Vector3(0f, 3.02f, 0f), new Vector3(0.36f, 0.55f, 0.36f));
            Prim(PrimitiveType.Cube, "Glyph", mats["StoneDark"], g.transform, new Vector3(0f, 1.55f, -0.32f), new Vector3(0.18f, 1.5f, 0.03f));
            g.transform.localScale = Vector3.one * (targetH / 3.58f);
        }

        /// <summary>Broken sandstone column — stump, tilted capstone, fallen drum beside it.</summary>
        static void BuildRuinCol(Transform parent, Vector3 pos, float scale, float rotY = 0f)
        {
            var g = new GameObject("RuinCol");
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localRotation = Quaternion.Euler(0f, rotY, 0f);
            Prim(PrimitiveType.Cube, "Base", mats["ObeliskSand"], g.transform, new Vector3(0f, 0.16f, 0f), new Vector3(1.25f, 0.32f, 1.25f));
            Prim(PrimitiveType.Cylinder, "Shaft", mats["PyramidSand"], g.transform, new Vector3(0f, 1.15f, 0f), new Vector3(0.58f, 0.85f, 0.58f));
            var cap = Prim(PrimitiveType.Cube, "CapRemain", mats["PyramidShade"], g.transform, new Vector3(0.05f, 2.02f, 0f), new Vector3(0.66f, 0.18f, 0.66f));
            cap.transform.localRotation = Quaternion.Euler(0f, 0f, 6f);
            var drum = Prim(PrimitiveType.Cylinder, "FallenDrum", mats["PyramidShade"], g.transform, new Vector3(0.8f, 0.28f, 0.35f), new Vector3(0.55f, 0.35f, 0.55f));
            drum.transform.localRotation = Quaternion.Euler(0f, 20f, 88f);
            g.transform.localScale = Vector3.one * scale;
        }

        /// <summary>Soft half-buried sand humps — texture for the flat desert bed.</summary>
        static void BuildDune(Transform parent, Vector3 pos, float r, float rotY = 0f)
        {
            var g = new GameObject("Dune");
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localRotation = Quaternion.Euler(0f, rotY, 0f);
            Prim(PrimitiveType.Sphere, "Hump", mats["SandDune"], g.transform, new Vector3(0f, -0.1f, 0f), new Vector3(r * 1.7f, r * 0.55f, r * 1.25f));
            Prim(PrimitiveType.Sphere, "Hump2", mats["SandDune"], g.transform, new Vector3(r * 0.55f, -0.16f, r * 0.3f), new Vector3(r, r * 0.34f, r * 0.8f));
        }

        static GameObject MakePyramidPrefab(string name, float baseSize, float height, int steps, bool cap)
        {
            var root = new GameObject(name);
            BuildPyramid(root.transform, Vector3.zero, baseSize, height, steps, cap);
            return SavePrefab(root, name);
        }

        static GameObject MakeObeliskPrefab()
        {
            var root = new GameObject("D_Obelisk");
            BuildObelisk(root.transform, Vector3.zero, 3.4f);
            return SavePrefab(root, "D_Obelisk");
        }

        static GameObject MakeRuinColPrefab()
        {
            var root = new GameObject("D_RuinCol");
            BuildRuinCol(root.transform, Vector3.zero, 1f);
            return SavePrefab(root, "D_RuinCol");
        }

        static GameObject MakeDunePrefab()
        {
            var root = new GameObject("D_Dune");
            BuildDune(root.transform, Vector3.zero, 2.6f);
            return SavePrefab(root, "D_Dune");
        }

        /// <summary>Attach an idle animation channel set to a spawned obstacle prefab.</summary>
        static ObstacleAnim Animate(GameObject root, Transform target = null)
        {
            var a = root.AddComponent<ObstacleAnim>();
            a.target = target;
            return a;
        }

        static GameObject MakeTorchFeature()
        {
            var root = new GameObject("WallFeature_Torch");
            var m = DModel("torch_mounted", root.transform, new Vector3(0, 2.55f, 0));
            if (m != null) FixDungeonMats(m);
            // flickering warm light
            var lg = new GameObject("FlameLight");
            lg.transform.SetParent(root.transform, false);
            lg.transform.localPosition = new Vector3(0, 3.0f, 0.45f);
            var l = lg.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = Hex("#FF9E4A");
            l.intensity = 1.5f;
            l.range = 5.5f;
            l.shadows = LightShadows.None;
            lg.AddComponent<TorchFlicker>();
            return SavePrefab(root, "WallFeature_Torch");
        }

        static GameObject MakeBannerFeature() => MakeDecor("banner_patternA_red", "WallFeature_Banner", 3.15f, sway: 2.6f);
        static GameObject MakeCrestFeature() => MakeDecor("sword_shield_gold", "WallFeature_Crest", 3.0f);
        static GameObject MakeBannerTriple() => MakeDecor("banner_triple_red", "WallFeature_BannerBig", 3.5f, sway: 2.6f);

        static GameObject MakePlayer()
        {
            var root = new GameObject("Player");

            var cc = root.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0, 0.9f, 0);
            cc.stepOffset = 0.4f;
            cc.slopeLimit = 50f;
            cc.skinWidth = 0.04f;

            root.AddComponent<PlayerController>();
            var anim = root.AddComponent<RunnerAnimator>();

            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0, 0.85f, 0); // hip height

            // torso (offset up from hip pivot)
            Prim(PrimitiveType.Cube, "Torso", mats["Shirt"], body.transform, new Vector3(0, 0.32f, 0), new Vector3(0.62f, 0.6f, 0.36f));
            // shorts/belt
            Prim(PrimitiveType.Cube, "Belt", mats["Pants"], body.transform, new Vector3(0, 0.03f, 0), new Vector3(0.6f, 0.12f, 0.34f));

            // head pivot
            var headP = new GameObject("HeadP");
            headP.transform.SetParent(body.transform, false);
            headP.transform.localPosition = new Vector3(0, 0.72f, 0);
            Prim(PrimitiveType.Cube, "Head", mats["Skin"], headP.transform, new Vector3(0, 0.22f, 0), new Vector3(0.42f, 0.42f, 0.4f));
            Prim(PrimitiveType.Cube, "Cap", mats["Cap"], headP.transform, new Vector3(0, 0.44f, 0.02f), new Vector3(0.44f, 0.12f, 0.42f));
            Prim(PrimitiveType.Cube, "Brim", mats["Cap"], headP.transform, new Vector3(0, 0.4f, 0.26f), new Vector3(0.42f, 0.05f, 0.2f));
            // eyes
            Prim(PrimitiveType.Cube, "EyeL", mats["Pants"], headP.transform, new Vector3(-0.1f, 0.24f, 0.205f), new Vector3(0.06f, 0.08f, 0.02f));
            Prim(PrimitiveType.Cube, "EyeR", mats["Pants"], headP.transform, new Vector3(0.1f, 0.24f, 0.205f), new Vector3(0.06f, 0.08f, 0.02f));

            // arm pivots at shoulders
            var armL = LimbPivot("ArmL", body.transform, new Vector3(-0.41f, 0.58f, 0));
            Prim(PrimitiveType.Cube, "Arm", mats["Shirt"], armL, new Vector3(0, -0.26f, 0), new Vector3(0.18f, 0.52f, 0.18f));
            Prim(PrimitiveType.Cube, "Hand", mats["Skin"], armL, new Vector3(0, -0.56f, 0), new Vector3(0.16f, 0.12f, 0.16f));
            var armR = LimbPivot("ArmR", body.transform, new Vector3(0.41f, 0.58f, 0));
            Prim(PrimitiveType.Cube, "Arm", mats["Shirt"], armR, new Vector3(0, -0.26f, 0), new Vector3(0.18f, 0.52f, 0.18f));
            Prim(PrimitiveType.Cube, "Hand", mats["Skin"], armR, new Vector3(0, -0.56f, 0), new Vector3(0.16f, 0.12f, 0.16f));

            // leg pivots at hips
            var legL = LimbPivot("LegL", body.transform, new Vector3(-0.17f, -0.03f, 0));
            Prim(PrimitiveType.Cube, "Leg", mats["Pants"], legL, new Vector3(0, -0.35f, 0), new Vector3(0.22f, 0.62f, 0.22f));
            Prim(PrimitiveType.Cube, "Shoe", mats["Shoe"], legL, new Vector3(0, -0.72f, 0.05f), new Vector3(0.24f, 0.14f, 0.34f));
            var legR = LimbPivot("LegR", body.transform, new Vector3(0.17f, -0.03f, 0));
            Prim(PrimitiveType.Cube, "Leg", mats["Pants"], legR, new Vector3(0, -0.35f, 0), new Vector3(0.22f, 0.62f, 0.22f));
            Prim(PrimitiveType.Cube, "Shoe", mats["Shoe"], legR, new Vector3(0, -0.72f, 0.05f), new Vector3(0.24f, 0.14f, 0.34f));

            anim.body = body.transform;
            anim.head = headP.transform;
            anim.armL = armL; anim.armR = armR;
            anim.legL = legL; anim.legR = legR;

            return SavePrefab(root, "Player");
        }

        static Transform LimbPivot(string name, Transform parent, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            return go.transform;
        }

        // ================= scenes =================

        static void ApplySkyAndFog()
        {
            RenderSettings.skybox = mats["Sky"];
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Hex("#D7E6E2");
            RenderSettings.fogStartDistance = 42f;
            RenderSettings.fogEndDistance = 150f;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Hex("#A8D2E4");
            RenderSettings.ambientEquatorColor = Hex("#F4E4C2");
            RenderSettings.ambientGroundColor = Hex("#69795A");
            RenderSettings.ambientIntensity = 1.08f;
        }

        static Light MakeLight(Transform parent)
        {
            var go = new GameObject("Sun");
            go.transform.SetParent(parent, false);
            go.transform.rotation = Quaternion.Euler(42f, -155f, 0f);
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = Hex("#FFEDC9");
            l.intensity = 1.35f;
            l.shadows = LightShadows.Soft;
            l.shadowStrength = 0.8f;
            go.transform.rotation = Quaternion.Euler(38f, -148f, 0f);
            RenderSettings.sun = l;
            return l;
        }

        static void MakeTileRow(Transform parent, int count, bool decor)
        {
            var tile = prefabs["Tile"];
            var decorPrefabs = new[] { prefabs["D_Pillar"], prefabs["D_Pillar2"], prefabs["D_Column"], prefabs["D_Rubble"], prefabs["D_Barrel"], prefabs["D_Chest"] };
            var rng = new System.Random(42);
            for (int i = 0; i < count; i++)
            {
                var t = (GameObject)PrefabUtility.InstantiatePrefab(tile);
                t.transform.position = new Vector3(0, 0, i * 24f);
                t.transform.SetParent(parent, true);
                if (decor)
                {
                    for (int d = 0; d < 4; d++)
                    {
                        var dp = decorPrefabs[rng.Next(decorPrefabs.Length)];
                        var inst = (GameObject)PrefabUtility.InstantiatePrefab(dp);
                        float side = rng.Next(2) == 0 ? -1f : 1f;
                        inst.transform.SetPositionAndRotation(
                            new Vector3(side * (5.6f + (float)rng.NextDouble() * 3f), 0, i * 24f + (float)rng.NextDouble() * 24f),
                            Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0));
                        inst.transform.localScale = Vector3.one * (0.8f + (float)rng.NextDouble() * 0.8f);
                        inst.transform.SetParent(parent, true);
                    }
                }
            }
        }

        static void BuildMenuScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ApplySkyAndFog();
            var world = new GameObject("World");
            MakeLight(world.transform);
            MakeTileRow(world.transform, 6, true);
            MakeBackdrop(world.transform);
            AddGlobalVolume();

            // showcase knight — cycles Idle / Cheer / PickUp / Interact
            var knight = MakeMenuKnight();
            knight.transform.position = new Vector3(0.1f, 0f, 7.4f);
            knight.transform.rotation = Quaternion.Euler(0f, 168f, 0f);
            knight.transform.SetParent(world.transform, true);
            knight.name = "MenuKnight";

            // temple dressing around the hero
            var chest = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["D_Chest"]);
            chest.transform.SetPositionAndRotation(new Vector3(-2.6f, 0f, 8.4f), Quaternion.Euler(0f, 30f, 0f));
            chest.transform.SetParent(world.transform, true);
            var crest = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["WF_Crest"]);
            crest.transform.SetPositionAndRotation(new Vector3(3.78f, 0f, 7.8f), Quaternion.Euler(0f, -90f, 0f));
            crest.transform.SetParent(world.transform, true);
            var bn = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["WF_BannerBig"]);
            bn.transform.SetPositionAndRotation(new Vector3(-3.78f, 0f, 13.6f), Quaternion.Euler(0f, 90f, 0f));
            bn.transform.SetParent(world.transform, true);
            var torch = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["WF_Torch"]);
            torch.transform.SetPositionAndRotation(new Vector3(3.78f, 0f, 5.6f), Quaternion.Euler(0f, -90f, 0f));
            torch.transform.SetParent(world.transform, true);
            var torch2 = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["WF_Torch"]);
            torch2.transform.SetPositionAndRotation(new Vector3(-3.78f, 0f, 6.8f), Quaternion.Euler(0f, 90f, 0f));
            torch2.transform.SetParent(world.transform, true);

            // scattered spinning coins by the chest — the menu feels alive
            var rng2 = new System.Random(7);
            for (int i = 0; i < 6; i++)
            {
                var coin = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["Coin"]);
                float cx = -2.6f + (float)rng2.NextDouble() * 2.4f - 0.4f;
                float cz = 7.4f + (float)rng2.NextDouble() * 2.6f;
                coin.transform.SetPositionAndRotation(new Vector3(cx, 0.55f + (float)rng2.NextDouble() * 0.5f, cz), Quaternion.identity);
                coin.transform.SetParent(world.transform, true);
            }

            // extra torches framing the floating logo
            var torch3 = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["WF_Torch"]);
            torch3.transform.SetPositionAndRotation(new Vector3(3.78f, 0f, 10.4f), Quaternion.Euler(0f, -90f, 0f));
            torch3.transform.SetParent(world.transform, true);
            var torch4 = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["WF_Torch"]);
            torch4.transform.SetPositionAndRotation(new Vector3(-3.78f, 0f, 11.6f), Quaternion.Euler(0f, 90f, 0f));
            torch4.transform.SetParent(world.transform, true);

            // warm rim light behind the knight for separation
            var rimGo = new GameObject("RimLight");
            rimGo.transform.SetParent(world.transform, false);
            rimGo.transform.position = new Vector3(-2f, 3.2f, 9.5f);
            var rim = rimGo.AddComponent<Light>();
            rim.type = LightType.Point;
            rim.color = Hex("#FFB26B");
            rim.intensity = 1.6f;
            rim.range = 9f;

            // floating extruded 3D logo above the corridor
            BuildTitle3D(world.transform);

            // violet glow where the logo hangs — sells the magic
            var mg = new GameObject("MagicLight");
            mg.transform.SetParent(world.transform, false);
            mg.transform.position = new Vector3(-0.4f, 5.0f, 9.2f);
            var ml = mg.AddComponent<Light>();
            ml.type = LightType.Point;
            ml.color = ColMagic;
            ml.intensity = 2.6f;
            ml.range = 14f;

            // golden dust motes drifting through the air
            MakeAmbientMotes(world.transform);

            // camera: low 3/4 hero shot framed to catch the floating logo, slow drift
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 50f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 220f;
            cam.clearFlags = CameraClearFlags.Skybox;
            PostCam(camGo);
            camGo.transform.position = new Vector3(1.6f, 2.25f, 1.0f);
            camGo.transform.rotation = Quaternion.LookRotation((new Vector3(-0.2f, 1.75f, 8.6f) - camGo.transform.position).normalized);
            camGo.AddComponent<MenuDiorama>();
            camGo.AddComponent<AudioListener>();

            // canvas
            BuildMenuCanvas();

            // event system
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            EditorSceneManager.SaveScene(scene, Root + "/Scenes/Menu.unity");
            Log("Menu scene saved");
        }

        static void BuildGameScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ApplySkyAndFog();
            var world = new GameObject("World");
            MakeLight(world.transform);
            MakeBackdrop(world.transform);
            AddGlobalVolume();
            MakeAmbientMotes(world.transform); // firefly motes drift ahead of the runner

            // far ground plane under everything (horizon blend — TrackManager tints it per zone)
            Prim(PrimitiveType.Cube, "FarGround", mats["FarGround"], world.transform, new Vector3(0, -0.62f, 60), new Vector3(240f, 0.2f, 400f));

            // managers
            var gmGo = new GameObject("GameManager", typeof(GameManager));
            var inputGo = new GameObject("RunnerInput", typeof(RunnerInput));
            var tmGo = new GameObject("TrackManager", typeof(TrackManager));
            var tm = tmGo.GetComponent<TrackManager>();
            tm.tilePrefab = prefabs["Tile"];
            tm.lowBarrierPrefab = prefabs["LowBarrier"];
            tm.highBarrierPrefab = prefabs["HighBarrier"];
            tm.wallBlockPrefab = prefabs["Wall"];
            tm.spikePrefab = prefabs["Spike"];
            tm.coinPrefab = prefabs["Coin"];
            tm.powerUpPrefabs = new[] { prefabs["PU_Magnet"], prefabs["PU_Shield"], prefabs["PU_Boost"], prefabs["PU_Car"], prefabs["PU_Plane"] };
            tm.decorPrefabs = new[] { prefabs["D_Pillar"], prefabs["D_Pillar2"], prefabs["D_Column"], prefabs["D_Rubble"], prefabs["D_Barrel"], prefabs["D_Keg"], prefabs["D_Chest"] };
            tm.wallFeaturePrefabs = new[] { prefabs["WF_Torch"], prefabs["WF_Banner"], prefabs["WF_BannerBig"], prefabs["WF_Crest"] };
            tm.zones = new[]
            {
                new TrackManager.Zone // castle temple — corridor + colonnade horizon baked in
                {
                    name = "Temple", tilePrefab = prefabs["Tile"],
                    decorPrefabs = new[] { prefabs["D_Pillar"], prefabs["D_Pillar2"], prefabs["D_Column"], prefabs["D_Rubble"], prefabs["D_Barrel"], prefabs["D_Chest"], prefabs["D_Keg"], prefabs["D2_Trunk"], prefabs["D2_Table"] },
                    featurePrefabs = new[] { prefabs["WF_Torch"], prefabs["WF_Banner"], prefabs["WF_BannerBig"], prefabs["WF_Crest"], prefabs["WF_BannerShield"], prefabs["WF_BannerThin"] },
                    decorXMin = 5.9f, decorXMax = 15f, decorCount = 9, featureCount = 4,
                    fogColor = Hex("#DCC9A8"),
                },
                new TrackManager.Zone // dense stylized woodland — Quaternius trees baked into two tile variants
                {
                    name = "Forest",
                    tilePrefab = prefabs["Tile_Forest_A"],
                    tilePrefabs = new[] { prefabs["Tile_Forest_A"], prefabs["Tile_Forest_B"] },
                    decorPrefabs = new[] { prefabs["Q_Bush"], prefabs["Q_BushS"], prefabs["Q_Flower"], prefabs["Q_GrassL"], prefabs["Q_Plant"], prefabs["Q_RockA"], prefabs["Q_RockB"], prefabs["Q_Log"], prefabs["Q_Stump"], prefabs["K_Mush"] },
                    obstaclePrefabs = new[] { prefabs["N_LogBar"], prefabs["N_Stump"], prefabs["N_DeadFall"], prefabs["N_RockJump"], prefabs["N_LogBar"] },
                    featurePrefabs = null,
                    decorXMin = 5.6f, decorXMax = 15f, decorCount = 12, featureCount = 0,
                    fogColor = Hex("#A8CFA0"),
                },
                new TrackManager.Zone // treasure vault — plus remastered dungeon clutter
                {
                    name = "Vault", tilePrefab = prefabs["Tile_Vault"],
                    decorPrefabs = new[] { prefabs["D_Chest"], prefabs["D_CoinPile"], prefabs["D_Keg"], prefabs["D_Barrel"], prefabs["D_Crates"], prefabs["D_Column"], prefabs["D2_Coins"], prefabs["D2_Trunk"], prefabs["D2_Shelf"], prefabs["D2_Table"] },
                    featurePrefabs = new[] { prefabs["WF_Crest"], prefabs["WF_Torch"], prefabs["WF_BannerBig"], prefabs["WF_BannerShield"] },
                    decorXMin = 5.9f, decorXMax = 15f, decorCount = 11, featureCount = 4,
                    fogColor = Hex("#E3C188"),
                },
                new TrackManager.Zone // sun-bleached desert — pyramids, obelisks, ruins and dunes
                {
                    name = "Canyon",
                    tilePrefab = prefabs["Tile_Canyon_A"],
                    tilePrefabs = new[] { prefabs["Tile_Canyon_A"], prefabs["Tile_Canyon_B"] },
                    decorPrefabs = new[] { prefabs["D_Pyramid"], prefabs["D_PyramidBig"], prefabs["D_Obelisk"], prefabs["D_RuinCol"], prefabs["D_Dune"], prefabs["K_Cactus"], prefabs["K_CactusS"], prefabs["K_Camp"], prefabs["Q_RockD"], prefabs["Q_RockD2"], prefabs["K_Cliff"], prefabs["K_CliffTop"] },
                    obstaclePrefabs = new[] { prefabs["N_RockJump"], prefabs["N_CactusBlock"], prefabs["N_RockBlock"], prefabs["N_RockJump"], prefabs["N_CactusBlock"] },
                    featurePrefabs = null,
                    decorXMin = 5.8f, decorXMax = 17f, decorCount = 12, featureCount = 0,
                    fogColor = Hex("#E8BE8E"),
                },
                new TrackManager.Zone // downtown strip — buildings baked in, props on the sidewalks
                {
                    name = "City",
                    tilePrefab = prefabs["Tile_City_A"],
                    tilePrefabs = new[] { prefabs["Tile_City_A"], prefabs["Tile_City_B"] },
                    decorPrefabs = new[] { prefabs["C_Hydrant"], prefabs["C_TrashA"], prefabs["C_TrashB"], prefabs["C_Bush"], prefabs["C_Box"], prefabs["C_Tower"] },
                    obstaclePrefabs = new[] { prefabs["C_Bench"], prefabs["C_Dumpster"], prefabs["C_Crates"], prefabs["C_CarBlock"], prefabs["C_Bench"] },
                    featurePrefabs = null,
                    decorXMin = 5.0f, decorXMax = 7.2f, decorCount = 8, featureCount = 0,
                    fogColor = Hex("#B7CBE0"),
                },
                new TrackManager.Zone // lagoon flume — tropical beach props snap onto the sand bed
                {
                    name = "Lagoon", tilePrefab = prefabs["Tile_Water"],
                    decorPrefabs = new[] { prefabs["Q_PalmA"], prefabs["Q_PalmB"], prefabs["W_Rock"], prefabs["W_Bush"], prefabs["W_Flowers"], prefabs["Q_RockA"], prefabs["D_Dune"] },
                    obstaclePrefabs = new[] { prefabs["W_Wave"], prefabs["W_Buoy"], prefabs["W_Geyser"], prefabs["W_Wave"] },
                    featurePrefabs = null,
                    decorXMin = 5.6f, decorXMax = 14f, decorCount = 11, featureCount = 0,
                    fogColor = Hex("#BFE9EE"),
                },
            };

            // sky coins that only appear while the plane is airborne
            var skyGo = new GameObject("SkyCoins");
            var sky = skyGo.AddComponent<SkyCoinSpawner>();
            sky.coinPrefab = prefabs["Coin"];

            // player
            var player = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["Player"]);
            player.transform.position = Vector3.zero;
            player.name = "Player";

            // camera
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 58f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 220f;
            cam.clearFlags = CameraClearFlags.Skybox;
            PostCam(camGo);
            camGo.AddComponent<RunnerCamera>();
            camGo.AddComponent<AudioListener>();

            BuildGameCanvas();
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            EditorSceneManager.SaveScene(scene, Root + "/Scenes/Game.unity");
            Log("Game scene saved");
        }

        // ================= UI helpers =================

        static RectTransform RT(GameObject go, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        static TMP_Text Text(string content, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size,
            float fontSize, TextAlignmentOptions align, Color color, FontStyles style = FontStyles.Bold, float spacing = 0f,
            TMP_FontAsset font = null)
        {
            var go = new GameObject("Text_" + (content.Length > 10 ? content.Substring(0, 10) : content), typeof(RectTransform));
            RT(go, parent, anchorMin, anchorMax, pivot, pos, size);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = content;
            t.fontSize = fontSize;
            t.alignment = align;
            t.color = color;
            t.fontStyle = style;
            t.characterSpacing = spacing;
            var f = font != null ? font : gameFont;
            if (f != null) t.font = f;
            return t;
        }

        static Image Panel(Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size, Color color, string name = "Panel", bool card = false)
        {
            var go = new GameObject(name, typeof(RectTransform));
            RT(go, parent, anchorMin, anchorMax, new Vector2(0.5f, 0.5f), pos, size);
            var img = go.AddComponent<Image>();
            img.color = color;
            img.sprite = card && cardSpr != null ? cardSpr : null;
            if (img.sprite != null) { img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 1.4f; }
            return img;
        }

        static Button MakeButton(string label, Transform parent, Vector2 pos, Vector2 size, Color bg, Color fg, UnityEngine.Events.UnityAction onClick, float fontSize = 40f, TMP_FontAsset font = null)
        {
            var go = new GameObject("Btn_" + label, typeof(RectTransform));
            RT(go, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size);
            var img = go.AddComponent<Image>();
            img.color = bg;
            img.sprite = btnSpr != null ? btnSpr : roundRect;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 1.4f;
            var btn = go.AddComponent<Button>();
            var sh = go.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.3f);
            sh.effectDistance = new Vector2(0f, -6f);
            var colors = btn.colors;
            colors.highlightedColor = bg * 1.1f;
            colors.pressedColor = bg * 0.85f;
            colors.fadeDuration = 0.08f;
            btn.colors = colors;
            Text(label, go.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero,
                fontSize, TextAlignmentOptions.Center, fg, FontStyles.Bold, 2f, font);
            if (onClick != null) UnityEventTools.AddPersistentListener(btn.onClick, onClick);
            return btn;
        }

        static Canvas MakeCanvas(string name, out RectTransform root)
        {
            var go = new GameObject(name);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            root = (RectTransform)go.transform;
            return canvas;
        }

        static void Stretch(GameObject go)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        // ================= menu canvas =================

        static void BuildMenuCanvas()
        {
            MakeCanvas("MenuCanvas", out var root);
            var ui = root.gameObject.AddComponent<MenuUI>();

            // twinkling star field across the upper half of the screen
            var rng = new System.Random(13);
            for (int i = 0; i < 16; i++)
            {
                float sz = 26f + (float)rng.NextDouble() * 38f;
                var s = new GameObject("Star" + i, typeof(RectTransform));
                RT(s, root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f),
                    new Vector2(-500f + (float)rng.NextDouble() * 1000f, -80f - (float)rng.NextDouble() * 660f),
                    new Vector2(sz, sz));
                var img = s.AddComponent<Image>();
                img.sprite = starSpr;
                img.color = i % 3 == 0 ? ColStarCyan : i % 3 == 1 ? ColStar : ColMagic;
                s.AddComponent<Twinkle>();
            }

            // bottom scrim for legibility over the diorama — deep indigo fade
            var scrim = new GameObject("Scrim", typeof(RectTransform));
            var srt = RT(scrim, root, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0, 720));
            var sImg = scrim.AddComponent<Image>();
            sImg.sprite = ScrimSprite();
            sImg.color = new Color(0.10f, 0.07f, 0.20f, 0.92f);
            sImg.type = Image.Type.Sliced;

            // breathing halo behind the play button
            var halo = new GameObject("PlayHalo", typeof(RectTransform));
            RT(halo, root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0, -330), new Vector2(640f, 330f));
            var hImg = halo.AddComponent<Image>();
            hImg.sprite = circle;
            hImg.color = new Color(ColUIAccent.r, ColUIAccent.g, ColUIAccent.b, 0.16f);
            halo.AddComponent<Pulse>();

            // play button (pulsey hero button with chunky display font)
            var play = MakeButton("PLAY", root, new Vector2(0, -330), new Vector2(470, 142), ColUIAccent, ColUIPanel,
                ui.OnPlayClicked, 66f, titleFont);
            var pOut = play.gameObject.AddComponent<Outline>();
            pOut.effectColor = new Color(0.50f, 0.27f, 0.04f, 0.95f);
            pOut.effectDistance = new Vector2(0f, -4f);
            play.gameObject.AddComponent<Pulse>();

            // best score chip with a sparkle marker
            var chip = new GameObject("BestChip", typeof(RectTransform));
            RT(chip, root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -482), new Vector2(400, 80));
            var chipImg = chip.AddComponent<Image>();
            chipImg.sprite = pillSpr != null ? pillSpr : roundRect;
            chipImg.type = Image.Type.Sliced;
            chipImg.color = new Color(ColMagicDeep.r, ColMagicDeep.g, ColMagicDeep.b, 0.78f);
            var chipOut = chip.AddComponent<Outline>();
            chipOut.effectColor = new Color(ColMagic.r, ColMagic.g, ColMagic.b, 0.55f);
            chipOut.effectDistance = new Vector2(0f, 2f);
            var cstar = new GameObject("ChipStar", typeof(RectTransform));
            RT(cstar, chip.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(46, 0), new Vector2(52, 52));
            var csImg = cstar.AddComponent<Image>();
            csImg.sprite = starSpr;
            csImg.color = ColStar;
            var best = Text("", chip.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(16, 0), Vector2.zero, 40, TextAlignmentOptions.Center, ColUIText, FontStyles.Bold, 4f, titleFont);
            ui.bestText = best;

            // sound toggle — quiet ghost pill
            var sound = MakeButton("SOUND  ON", root, new Vector2(0, -604), new Vector2(340, 80), new Color(ColUIPanel.r, ColUIPanel.g, ColUIPanel.b, 0.82f), ColUIText,
                ui.OnSoundClicked, 30f);
            ui.soundLabel = sound.GetComponentInChildren<TMP_Text>();

            // swipe hints at bottom
            var hint = Text("SWIPE TO STEER • JUMP • SLIDE", root, new Vector2(0.5f, 0.05f), new Vector2(0.5f, 0.05f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(1000, 50), 30, TextAlignmentOptions.Center, new Color(1, 1, 1, 0.78f), FontStyles.Normal, 6f);
            var hsh = hint.gameObject.AddComponent<Shadow>();
            hsh.effectColor = new Color(0.1f, 0.13f, 0.18f, 0.7f);
            hsh.effectDistance = new Vector2(0f, -3f);

            // CC-BY attribution for the soundtrack
            Text("♪  ELECTRODOODLE — KEVIN MACLEOD (CC BY 3.0)", root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0, 18), new Vector2(1000, 36), 22,
                TextAlignmentOptions.Center, new Color(1f, 1f, 1f, 0.4f), FontStyles.Normal, 3f);
        }

        // ================= game canvas =================

        static void BuildGameCanvas()
        {
            MakeCanvas("GameCanvas", out var root);
            var ui = root.gameObject.AddComponent<GameUI>();

            // ---------- HUD ----------
            var hud = new GameObject("HUD", typeof(RectTransform));
            Stretch(RT(hud, root, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero).gameObject);
            Stretch(hud);
            ui.hudRoot = hud;

            ui.scoreText = Text("0", hud.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0, -70), new Vector2(600, 110), 92, TextAlignmentOptions.Center, ColUIText, FontStyles.Bold, 4f, titleFont);
            var scSh = ui.scoreText.gameObject.AddComponent<Shadow>();
            scSh.effectColor = new Color(0.08f, 0.11f, 0.16f, 0.85f);
            scSh.effectDistance = new Vector2(0f, -5f);

            // coin chip top-left: pill + icon + count
            var chip = new GameObject("CoinChip", typeof(RectTransform));
            RT(chip, hud.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28, -36), new Vector2(190, 76));
            var chipImg = chip.AddComponent<Image>();
            chipImg.sprite = pillSpr != null ? pillSpr : roundRect;
            chipImg.type = Image.Type.Sliced;
            chipImg.color = new Color(ColUIPanel.r, ColUIPanel.g, ColUIPanel.b, 0.72f);
            var coinGo = new GameObject("CoinIcon", typeof(RectTransform));
            RT(coinGo, chip.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(42, 0), new Vector2(52, 52));
            var ci = coinGo.AddComponent<Image>();
            ci.sprite = coinIcon;
            ui.coinText = Text("0", chip.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(78, -2), new Vector2(110, 60), 46, TextAlignmentOptions.Left, ColUIText, FontStyles.Bold, 2f);

            // pause button top-right
            var pauseBtn = MakeButton("II", hud.transform, new Vector2(0, 0), new Vector2(96, 96),
                new Color(ColUIPanel.r, ColUIPanel.g, ColUIPanel.b, 0.75f), ColUIText, ui.OnPauseClicked, 36f);
            var pbrt = (RectTransform)pauseBtn.transform;
            pbrt.anchorMin = pbrt.anchorMax = pbrt.pivot = new Vector2(1f, 1f);
            pbrt.anchoredPosition = new Vector2(-30, -30);
            ui.pauseButton = pauseBtn.gameObject;

            // ---------- READY ----------
            var ready = Panel(root.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                new Color(0.05f, 0.09f, 0.12f, 0.25f), "ReadyPanel").gameObject;
            Stretch(ready);
            ui.readyPanel = ready;
            var tap = Text("TAP TO RUN", ready.transform, new Vector2(0.5f, 0.62f), new Vector2(0.5f, 0.62f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(800, 130), 84, TextAlignmentOptions.Center, ColUIText, FontStyles.Bold, 6f, titleFont);
            var tapGo = tap.gameObject;
            tapGo.AddComponent<CanvasGroup>();
            tapGo.AddComponent<Pulse>();
            Text("SWIPE  LEFT / RIGHT  TO  SWITCH  LANES", ready.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(900, 50), 32, TextAlignmentOptions.Center, new Color(1, 1, 1, 0.8f), FontStyles.Normal, 2f);
            Text("UP  TO  JUMP   •   DOWN  TO  SLIDE", ready.transform, new Vector2(0.5f, 0.46f), new Vector2(0.5f, 0.46f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(900, 50), 32, TextAlignmentOptions.Center, new Color(1, 1, 1, 0.8f), FontStyles.Normal, 2f);
            ui.readyBestText = Text("BEST  0", ready.transform, new Vector2(0.5f, 0.36f), new Vector2(0.5f, 0.36f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(600, 60), 40, TextAlignmentOptions.Center, ColUIAccent, FontStyles.Bold, 4f);

            // ---------- PAUSE ----------
            var pause = Panel(root.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                new Color(0.05f, 0.09f, 0.12f, 0.6f), "PausePanel").gameObject;
            Stretch(pause);
            ui.pausePanel = pause;
            Text("PAUSED", pause.transform, new Vector2(0.5f, 0.68f), new Vector2(0.5f, 0.68f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(700, 120), 96, TextAlignmentOptions.Center, ColUIText, FontStyles.Bold, 6f, titleFont);
            MakeButton("RESUME", pause.transform, new Vector2(0, -60), new Vector2(400, 110), ColUIAccent, ColUIPanel,
                ui.OnResumeClicked, 44f, titleFont);
            MakeButton("MENU", pause.transform, new Vector2(0, -200), new Vector2(400, 110),
                new Color(ColUIText.r, ColUIText.g, ColUIText.b, 0.14f), ColUIText, ui.OnMenuClicked, 40f, titleFont);

            // ---------- DEAD ----------
            var dead = Panel(root.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                new Color(0.04f, 0.07f, 0.10f, 0.62f), "DeadPanel").gameObject;
            Stretch(dead);
            ui.pausePanel.SetActive(false);
            ui.deadPanel = dead;

            // centered card
            var card = Panel(dead.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 40),
                new Vector2(700, 980), new Color(0.10f, 0.14f, 0.19f, 0.96f), "Card", true).gameObject;

            var wip = Text("WIPEOUT!", card.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(0, -150), new Vector2(660, 130), 92, TextAlignmentOptions.Center, Hex("#FF5A3C"), FontStyles.Bold, 6f, titleFont);
            var wsh = wip.gameObject.AddComponent<Shadow>();
            wsh.effectColor = new Color(0f, 0f, 0f, 0.6f);
            wsh.effectDistance = new Vector2(0f, -6f);

            var scl = Text("SCORE", card.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(0, -260), new Vector2(400, 50), 30, TextAlignmentOptions.Center, new Color(1f, 1f, 1f, 0.55f), FontStyles.Normal, 8f);
            ui.deadScoreText = Text("0", card.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(0, -360), new Vector2(600, 130), 104, TextAlignmentOptions.Center, ColUIText, FontStyles.Bold, 4f, titleFont);

            // stats row: best chip + coin chip
            var bchip = new GameObject("BestChip", typeof(RectTransform));
            RT(bchip, card.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -30), new Vector2(300, 66));
            var bImg = bchip.AddComponent<Image>();
            bImg.sprite = pillSpr != null ? pillSpr : roundRect;
            bImg.type = Image.Type.Sliced;
            bImg.color = new Color(1f, 1f, 1f, 0.08f);
            ui.deadBestText = Text("BEST  0", bchip.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero, 36, TextAlignmentOptions.Center, ColUIAccent, FontStyles.Bold, 4f);

            var cchip = new GameObject("CoinChip", typeof(RectTransform));
            RT(cchip, card.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -115), new Vector2(220, 66));
            var cImg = cchip.AddComponent<Image>();
            cImg.sprite = pillSpr != null ? pillSpr : roundRect;
            cImg.type = Image.Type.Sliced;
            cImg.color = new Color(1f, 1f, 1f, 0.08f);
            var dci = new GameObject("DeadCoinIcon", typeof(RectTransform));
            RT(dci, cchip.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(48, 0), new Vector2(44, 44));
            var dcimg = dci.AddComponent<Image>();
            dcimg.sprite = coinIcon;
            ui.deadCoinText = Text("0", cchip.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(82, -1), new Vector2(130, 54), 40, TextAlignmentOptions.Left, ColUIText, FontStyles.Bold, 2f);

            var retry = MakeButton("RETRY", card.transform, new Vector2(0, -230), new Vector2(420, 120), ColUIAccent, ColUIPanel,
                ui.OnRestartClicked, 52f, titleFont);
            retry.gameObject.AddComponent<Pulse>();
            MakeButton("MENU", card.transform, new Vector2(0, -380), new Vector2(420, 100),
                new Color(ColUIText.r, ColUIText.g, ColUIText.b, 0.10f), ColUIText, ui.OnMenuClicked, 40f, titleFont);
            dead.SetActive(false);
            hud.SetActive(false);
        }

        // ================= visual upgrade: sprites =================

        static void GenerateSpritesV2()
        {
            // rounded card with baked soft drop-shadow (9-sliced)
            WriteSprite(Root + "/Sprites/Card.png", 96, 96, (x, y, w, h) =>
            {
                const float r = 26f, sh = 14f;
                // card body
                float dx = Mathf.Max(0, Mathf.Max(r - x, x - (w - 1 - r)));
                float dy = Mathf.Max(0, Mathf.Max(r - (y + sh * 0.5f), (y + sh * 0.5f) - (h - 1 - r)));
                float dCard = Mathf.Sqrt(dx * dx + dy * dy);
                if (dCard <= r) return new Color32(255, 255, 255, 255);
                // soft shadow below
                float dy2 = Mathf.Max(0, Mathf.Max(r - (y - sh * 0.5f), (y - sh * 0.5f) - (h - 1 - r)));
                float dSh = Mathf.Sqrt(dx * dx + dy2 * dy2);
                float a = Mathf.Clamp01((r + 8f - dSh) / 8f) * 0.45f;
                return new Color32(10, 16, 22, (byte)(a * 255));
            }, new Vector4(34, 34, 40, 34), out cardSpr);

            // pill chip
            WriteSprite(Root + "/Sprites/Pill.png", 96, 48, (x, y, w, h) =>
            {
                float r = h * 0.5f - 1f;
                float dx = Mathf.Max(0, Mathf.Max(r - x, x - (w - 1 - r)));
                float dy = Mathf.Max(0, Mathf.Max(r - y, y - (h - 1 - r)));
                return dx * dx + dy * dy <= r * r
                    ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
            }, new Vector4(24, 24, 24, 24), out pillSpr);

            // button with subtle top-gloss gradient baked in
            WriteSprite(Root + "/Sprites/Button.png", 96, 96, (x, y, w, h) =>
            {
                const float r = 24f;
                float dx = Mathf.Max(0, Mathf.Max(r - x, x - (w - 1 - r)));
                float dy = Mathf.Max(0, Mathf.Max(r - y, y - (h - 1 - r)));
                if (dx * dx + dy * dy > r * r) return new Color32(255, 255, 255, 0);
                float t = y / (float)h;
                byte v = (byte)Mathf.Lerp(255f, 218f, t); // top brighter
                return new Color32(v, v, v, 255);
            }, new Vector4(26, 26, 26, 26), out btnSpr);

            // soft radial puff for particles
            WriteSprite(Root + "/Sprites/SoftPuff.png", 64, 64, (x, y, w, h) =>
            {
                float dx = x - 31.5f, dy = y - 31.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / 31.5f;
                if (d >= 1f) return new Color32(255, 255, 255, 0);
                float a = 1f - d; a *= a;
                return new Color32(255, 255, 255, (byte)(a * 200));
            }, out softPuff);

            // vertical fade-up scrim (bottom solid -> top transparent)
            WriteSprite(Root + "/Sprites/Scrim.png", 8, 128, (x, y, w, h) =>
            {
                float t = y / (float)(h - 1);
                byte a = (byte)(Mathf.Pow(1f - t, 1.7f) * 255);
                return new Color32(255, 255, 255, a);
            }, out scrimSpr);

            // four-point sparkle star for menu magic
            WriteSprite(Root + "/Sprites/Star.png", 64, 64, (x, y, w, h) =>
            {
                float ax = Mathf.Abs(x - 31.5f) / 31.5f, ay = Mathf.Abs(y - 31.5f) / 31.5f;
                float diamond = Mathf.Clamp01(1f - (ax + ay) * 1.25f);
                float round = Mathf.Clamp01(1f - Mathf.Sqrt(ax * ax + ay * ay));
                float a = diamond * diamond * 1.1f + round * round * 0.22f;
                return new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255));
            }, out starSpr);
            Log("V2 sprites written");
        }

        static Sprite ScrimSprite() => scrimSpr;

        // ================= visual upgrade: character =================

        static AnimationClip Extract(string glb, string clip, string saveAs, bool loop)
        {
            string src = ArtDir + "/" + glb;
            AnimationClip found = null;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(src))
                if (o is AnimationClip c && (c.name == clip || c.name.EndsWith("|" + clip))) { found = c; break; }
            if (found == null) { Log("MISSING clip " + clip + " in " + glb); return null; }
            var inst = UnityEngine.Object.Instantiate(found);
            inst.name = "A_" + saveAs;
            var st = AnimationUtility.GetAnimationClipSettings(inst);
            st.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(inst, st);
            string path = Root + "/Animations/" + saveAs + ".anim";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(inst, path);
            clips[saveAs] = inst;
            return inst;
        }

        const string KnightSrc = "CharNew/Knight.glb";

        static void ExtractClips()
        {
            clips.Clear();
            // all clips come from the knight's own embedded set — bound to its rig
            Extract(KnightSrc, "Idle",               "Idle",    true);
            Extract(KnightSrc, "Unarmed_Idle",       "IdleB",   true);
            Extract(KnightSrc, "Running_A",          "Run",     true);
            Extract(KnightSrc, "Running_B",          "RunB",    true);
            Extract(KnightSrc, "Jump_Start",         "JumpStart", false);
            Extract(KnightSrc, "Jump_Idle",          "Airborne", true);
            Extract(KnightSrc, "Jump_Land",          "Land",    false);
            Extract(KnightSrc, "Dodge_Forward",      "Slide",   false);
            Extract(KnightSrc, "Dodge_Left",         "StrafeL", false);
            Extract(KnightSrc, "Dodge_Right",        "StrafeR", false);
            Extract(KnightSrc, "Death_A",            "Death",   false);
            Extract(KnightSrc, "Dodge_Forward",      "Start",   false);   // roll-out launch
            Extract(KnightSrc, "2H_Melee_Idle",      "Ready",   true);    // combat stance
            Extract(KnightSrc, "Cheer",              "Cheer",   false);
            Extract(KnightSrc, "PickUp",             "PickUp",  false);
            Extract(KnightSrc, "Interact",           "Interact", false);
            Extract(KnightSrc, "Hit_A",              "Hit",     false);
            // Commit the .anim imports NOW — prefabs serialized in this same
            // pass must resolve live asset references, not pending imports.
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Log("Clips extracted: " + clips.Count);
        }

        static void CreateCharMaterials()
        {
            Mat("CharBody",   Hex("#3B4354"), 0.5f, 0.15f);
            Mat("CharTrim",   Hex("#2E3440"), 0.45f, 0.1f);
            Mat("CharAccent", Hex("#FFB43A"), 0.55f, 0.2f, true);
            // glowing visor
            var screen = Mat("CharScreen", Hex("#0C1418"), 0.9f, 0.4f, true);
            screen.SetColor("_EmissionColor", Hex("#35D7FF") * 2.2f);
            EditorUtility.SetDirty(screen);

            Mat("MountainA", Hex("#7E94AE"), 0.04f);
            Mat("MountainB", Hex("#96A9BE"), 0.04f);
            Mat("Cloud",     Hex("#F6FBFF"), 0.35f);

            // particle material (URP particles/unlit, transparent)
            string p = Root + "/Materials/Dust.mat";
            dustMat = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (dustMat == null)
            {
                dustMat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = "Dust" };
                AssetDatabase.CreateAsset(dustMat, p);
            }
            dustMat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Sprites/SoftPuff.png"));
            dustMat.SetColor("_BaseColor", Color.white);
            dustMat.SetFloat("_Surface", 1f);
            dustMat.SetFloat("_Blend", 0f);
            dustMat.SetOverrideTag("RenderType", "Transparent");
            dustMat.renderQueue = (int)RenderQueue.Transparent;
            dustMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            dustMat.EnableKeyword("_ALPHABLEND_ON");
            EditorUtility.SetDirty(dustMat);
            Log("Character materials ready");
        }

        static void CreateAnimatorController()
        {
            string path = Root + "/Animations/Runner.controller";
            AssetDatabase.DeleteAsset(path);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
            var sm = ctrl.layers[0].stateMachine;
            var states = new List<AnimatorState>();
            foreach (var n in new[] { "Idle", "Run", "JumpStart", "Airborne", "Land", "Slide", "StrafeL", "StrafeR", "Death" })
            {
                if (!clips.TryGetValue(n, out var c) || c == null) { Log("no clip for state " + n); continue; }
                var s = ctrl.AddMotion(c, 0);   // AddMotion binds the clip reliably
                s.name = n;
                states.Add(s);
            }
            if (states.Count > 0) sm.defaultState = states[0]; // Idle
            EditorUtility.SetDirty(ctrl);
            runnerCtrl = ctrl;
            Log("AnimatorController created (" + states.Count + " states)");
        }

        // ================= visual upgrade: FX =================

        static ParticleSystem SetupDust(GameObject go)
        {
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.startLifetime = 0.6f;
            main.startSpeed = 0.35f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.3f);
            main.startColor = new Color(0.94f, 0.90f, 0.80f, 0.55f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 60;

            var em = ps.emission;
            em.rateOverTime = 0f;

            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = 0.16f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.55f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 1.6f));

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.y = new ParticleSystem.MinMaxCurve(0.7f);
            vel.space = ParticleSystemSimulationSpace.World;

            var rd = ps.GetComponent<ParticleSystemRenderer>();
            rd.material = dustMat;
            rd.renderMode = ParticleSystemRenderMode.Billboard;
            rd.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return ps;
        }

        static void CreateFx()
        {
            // coin pickup burst
            var go = new GameObject("CoinBurst");
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = true;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.55f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.6f, 3.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
            main.startColor = new Color(1.6f, 1.2f, 0.35f); // HDR for bloom
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0.9f;
            main.stopAction = ParticleSystemStopAction.Destroy;
            main.maxParticles = 24;

            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 14) });

            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = 0.12f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(new Color(1.7f, 1.35f, 0.4f), 0f), new GradientColorKey(new Color(1.2f, 0.7f, 0.2f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;

            var rd = ps.GetComponent<ParticleSystemRenderer>();
            rd.material = dustMat;
            rd.renderMode = ParticleSystemRenderMode.Billboard;
            rd.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            string path = Root + "/Prefabs/CoinBurst.prefab";
            coinBurstPrefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            UnityEngine.Object.DestroyImmediate(go);
            Log("FX prefabs created");
        }

        // ================= visual upgrade: post-processing =================

        static void CreateVolumeProfile()
        {
            string path = Root + "/WarriorRunVolume.asset";
            // load-or-create: keeps the GUID stable so scene references never break
            volProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (volProfile == null)
            {
                volProfile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(volProfile, path);
            }
            // drop any dead slots left by earlier builds (nulls -> per-frame NRE -> black screen)
            volProfile.components.RemoveAll(c => c == null);

            var bloom = VolComp<Bloom>();
            bloom.intensity.Override(0.5f);
            bloom.threshold.Override(1.0f);
            bloom.scatter.Override(0.65f);

            var tone = VolComp<Tonemapping>();
            tone.mode.Override(TonemappingMode.ACES);

            var ca = VolComp<ColorAdjustments>();
            ca.postExposure.Override(0.2f);
            ca.contrast.Override(14f);
            ca.saturation.Override(16f);
            ca.colorFilter.Override(Hex("#FFF3E2"));

            var vig = VolComp<Vignette>();
            vig.intensity.Override(0.21f);
            vig.smoothness.Override(0.55f);

            var wb = VolComp<WhiteBalance>();
            wb.temperature.Override(7f);

            EditorUtility.SetDirty(volProfile);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            Log("Volume profile created");
        }

        /// <summary>
        /// Get-or-add a volume component AND pin it into the profile asset.
        /// VolumeProfile.Add() only creates the object in memory — without an
        /// explicit AddObjectToAsset the component serializes as a null slot.
        /// </summary>
        static T VolComp<T>() where T : VolumeComponent
        {
            for (int i = 0; i < volProfile.components.Count; i++)
                if (volProfile.components[i] is T c)
                    return c;
            var comp = volProfile.Add<T>(true);
            if (comp != null && !AssetDatabase.Contains(comp))
                AssetDatabase.AddObjectToAsset(comp, volProfile);
            return comp;
        }

        static void AddGlobalVolume()
        {
            if (volProfile == null) return;
            var go = new GameObject("Global Volume");
            var v = go.AddComponent<Volume>();
            v.isGlobal = true;
            v.sharedProfile = volProfile;
        }

        static void PostCam(GameObject camGo)
        {
            var data = camGo.GetComponent<Camera>().GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            data.dithering = true;
        }

        // ================= visual upgrade: parallax backdrop =================

        static void MakeBackdrop(Transform parent)
        {
            // distant mountain ring
            var mtn = new GameObject("Mountains");
            mtn.transform.SetParent(parent, false);
            var ps = mtn.AddComponent<ParallaxScroller>();
            var so = new SerializedObject(ps);
            so.FindProperty("speedFactor").floatValue = 0.05f;
            so.FindProperty("loopLength").floatValue = 200f;
            so.FindProperty("killZ").floatValue = -42f;
            so.ApplyModifiedPropertiesWithoutUndo();

            var rng = new System.Random(7);
            for (int i = 0; i < 10; i++)
            {
                float z = 12f + i * 19f + (float)rng.NextDouble() * 8f;
                float side = i % 2 == 0 ? -1f : 1f;
                float x = side * (22f + (float)rng.NextDouble() * 16f);
                var cone = new GameObject("Mtn" + i);
                cone.transform.SetParent(mtn.transform, false);
                var mf = cone.AddComponent<MeshFilter>();
                mf.sharedMesh = coneMesh;
                var mr = cone.AddComponent<MeshRenderer>();
                mr.sharedMaterial = i % 3 == 0 ? mats["MountainA"] : mats["MountainB"];
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                float sx = 10f + (float)rng.NextDouble() * 9f;
                float sy = 9f + (float)rng.NextDouble() * 11f;
                cone.transform.localScale = new Vector3(sx, sy, 9f);
                cone.transform.position = new Vector3(x, coneMesh.bounds.extents.y * sy * 0.55f, z);
                cone.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 60f, 0);
            }

            // drifting cloud puffs
            var cl = new GameObject("Clouds");
            cl.transform.SetParent(parent, false);
            var cps = cl.AddComponent<ParallaxScroller>();
            var cso = new SerializedObject(cps);
            cso.FindProperty("speedFactor").floatValue = 0.16f;
            cso.FindProperty("loopLength").floatValue = 190f;
            cso.FindProperty("killZ").floatValue = -35f;
            cso.FindProperty("drift").floatValue = 0.6f;
            cso.ApplyModifiedPropertiesWithoutUndo();

            for (int i = 0; i < 8; i++)
            {
                float z = 15f + i * 23f + (float)rng.NextDouble() * 10f;
                float x = -28f + (float)rng.NextDouble() * 56f;
                float y = 15f + (float)rng.NextDouble() * 10f;
                var cluster = new GameObject("Cloud" + i);
                cluster.transform.SetParent(cl.transform, false);
                cluster.transform.position = new Vector3(x, y, z);
                int puffs = 2 + rng.Next(2);
                for (int p = 0; p < puffs; p++)
                {
                    var puff = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    UnityEngine.Object.DestroyImmediate(puff.GetComponent<Collider>());
                    puff.name = "p" + p;
                    puff.transform.SetParent(cluster.transform, false);
                    puff.transform.localPosition = new Vector3(p * 2.6f - puffs, (float)rng.NextDouble() * 0.8f, 0);
                    float s = 2.4f + (float)rng.NextDouble() * 2.2f;
                    puff.transform.localScale = new Vector3(s, s * 0.55f, s * 0.8f);
                    puff.GetComponent<MeshRenderer>().sharedMaterial = mats["Cloud"];
                    puff.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
        }

        // ================= magical menu: floating 3D logo + motes =================

        /// <summary>
        /// Extruded world-space logo: a stack of dark copies sits behind a
        /// gradient face so the title reads as real 3D from the menu camera.
        /// </summary>
        static void BuildTitle3D(Transform parent)
        {
            var root = new GameObject("Title3D");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(
                new Vector3(-0.5f, 4.3f, 9.8f), Quaternion.Euler(-8f, -12f, 2f));
            root.AddComponent<FloatyText>();

            for (int i = 5; i >= 1; i--)
                MakeTMP3D(root.transform, "WARRIOR RUN", new Vector3(0f, -0.02f * i, 0.06f * i),
                    Color.Lerp(Hex("#6E2A0C"), Hex("#3D1505"), i / 5f), new Vector2(4.6f, 1.9f), 2.6f);

            var face = MakeTMP3D(root.transform, "WARRIOR RUN", Vector3.zero,
                ColStar, new Vector2(4.6f, 1.9f), 2.6f);
            face.enableVertexGradient = true;
            face.colorGradient = new VertexGradient(Hex("#FFF7D6"), Hex("#FFF7D6"), Hex("#FF9435"), Hex("#FF9435"));

            var sub = MakeTMP3D(root.transform, "A MAGICAL ENDLESS ADVENTURE", new Vector3(0f, -1.15f, 0.02f),
                ColStarCyan, new Vector2(5.0f, 0.7f), 0.6f);
            sub.characterSpacing = 12f;
        }

        static TextMeshPro MakeTMP3D(Transform parent, string content, Vector3 localPos, Color col, Vector2 box, float maxFont)
        {
            var go = new GameObject("TMP3D_" + (content.Length > 8 ? content.Substring(0, 8) : content));
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.localPosition = localPos;
            rt.localRotation = Quaternion.identity;
            rt.localScale = Vector3.one;
            rt.sizeDelta = box;
            var t = go.AddComponent<TextMeshPro>();
            t.text = content;
            t.alignment = TextAlignmentOptions.Center;
            var f = titleFont != null ? titleFont : gameFont;
            if (f != null) t.font = f;
            t.enableAutoSizing = true;
            t.fontSizeMin = 0.2f;
            t.fontSizeMax = maxFont;
            t.color = col;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            return t;
        }

        /// <summary>Floating dust motes / fireflies — keeps the world feeling alive.</summary>
        static void MakeAmbientMotes(Transform parent)
        {
            var go = new GameObject("AmbientMotes");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(0f, 3f, 20f);
            var ps = go.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.playOnAwake = true;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.17f);
            // gold and fairy-cyan sparkles, HDR-bright so bloom catches them
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1.6f, 1.3f, 0.5f), new Color(0.65f, 1.1f, 1.5f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 160;

            var em = ps.emission;
            em.rateOverTime = 24f;

            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(18f, 6.5f, 40f);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.85f, 0.25f),
                    new GradientAlphaKey(0.85f, 0.75f), new GradientAlphaKey(0f, 1f)
                });
            col.color = g;

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.35f;
            noise.frequency = 0.25f;
            noise.scrollSpeed = 0.4f;

            var rd = ps.GetComponent<ParticleSystemRenderer>();
            rd.material = dustMat;
            rd.renderMode = ParticleSystemRenderMode.Billboard;
            rd.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            go.AddComponent<AmbientDrift>();
        }

        // ================= visual upgrade: player v2 =================

        static readonly HashSet<string> KnightHide = new()
            { "2H_Sword", "1H_Sword_Offhand", "Badge_Shield", "Rectangle_Shield", "Spike_Shield" };

        static GameObject MakePlayerV2()
        {
            var root = new GameObject("Player");

            var cc = root.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0, 0.9f, 0);
            cc.stepOffset = 0.4f;
            cc.slopeLimit = 50f;
            cc.skinWidth = 0.04f;

            root.AddComponent<PlayerController>();

            // pivots: Visual carries lean/roll, Squash carries squash-stretch
            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            var squash = new GameObject("Squash");
            squash.transform.SetParent(visual.transform, false);

            var charAsset = AssetDatabase.LoadAssetAtPath<GameObject>(CharGlb);
            if (charAsset == null) { Log("WARN: knight glb missing, falling back to cube rig"); return MakePlayerFallback(root); }

            var model = (GameObject)PrefabUtility.InstantiatePrefab(charAsset);
            model.transform.SetParent(squash.transform, false);
            model.name = "Knight";

            // keep the hero loadout, hide alternate weapon/shield variants
            foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true))
                if (KnightHide.Contains(r.name)) r.gameObject.SetActive(false);

            // normalize height to ~1.9m, feet at y=0
            var b = CalcBounds(model);
            float s = b.size.y > 0.01f ? 1.9f / b.size.y : 1f;
            model.transform.localScale = Vector3.one * s;
            model.transform.localPosition = new Vector3(0f, -b.min.y * s, 0f);

            // knight clips bind paths "Rig/root/..." — Animator sits on the model root
            var anim = model.GetComponent<Animator>();
            if (anim == null) anim = model.AddComponent<Animator>();
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            // no controller — MannequinAnimator drives a PlayableGraph directly

            // keep the knight's own texture; just tune + shadows
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                r.receiveShadows = false;
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || fixedMats.Contains(m)) continue;
                    fixedMats.Add(m);
                    if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.25f);
                    if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.1f);
                }
            }

            var man = visual.AddComponent<MannequinAnimator>();
            man.anim = anim;
            man.leanPivot = visual.transform;
            man.squashPivot = squash.transform;
            man.clipIdle = C("Idle");
            man.clipRun = C("Run");
            man.clipJumpStart = C("JumpStart");
            man.clipAirborne = C("Airborne");
            man.clipLand = C("Land");
            man.clipSlide = C("Slide");
            man.clipStrafeL = C("StrafeL");
            man.clipStrafeR = C("StrafeR");
            man.clipDeath = C("Death");
            man.clipStart = C("Start");
            man.clipIdleB = C("IdleB");
            man.clipCheer = C("Cheer");
            man.clipPickUp = C("PickUp");
            man.clipInteract = C("Interact");
            man.clipHit = C("Hit");
            man.clipReady = C("Ready");

            // dust trail at the heels
            var dustGo = new GameObject("DustTrail");
            dustGo.transform.SetParent(visual.transform, false);
            dustGo.transform.localPosition = new Vector3(0f, 0.12f, -0.35f);
            man.dust = SetupDust(dustGo);

            // vehicle rigs — hidden until the matching powerup drops
            var svc = root.AddComponent<SpecialVehicleController>();
            svc.visualRoot = visual;

            var carRig = new GameObject("CarRig");
            carRig.transform.SetParent(root.transform, false);
            BuildCarBody(carRig.transform);
            svc.carRig = carRig;
            svc.carWheels = new Transform[]
            {
                carRig.transform.Find("Wheel_FL"), carRig.transform.Find("Wheel_FR"),
                carRig.transform.Find("Wheel_RL"), carRig.transform.Find("Wheel_RR")
            };
            svc.exhaust = SetupExhaust(carRig.transform);
            AttachGlow(carRig.transform); // underglow is inside the body — light + sparks ride here
            carRig.SetActive(false);

            var planeRig = new GameObject("PlaneRig");
            planeRig.transform.SetParent(root.transform, false);
            BuildPlaneBody(planeRig.transform);
            svc.planeRig = planeRig;
            AttachGlow(planeRig.transform);
            planeRig.SetActive(false);

            // streaks live on the player root so they also fire while flying
            svc.speedLines = SetupSpeedLines(root.transform);

            return SavePrefab(root, "Player");
        }

        /// <summary>Standalone knight for the menu — showcase anims, no gameplay components.</summary>
        static GameObject MakeMenuKnight()
        {
            var root = new GameObject("MenuKnightRoot");
            var charAsset = AssetDatabase.LoadAssetAtPath<GameObject>(CharGlb);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(charAsset);
            model.transform.SetParent(root.transform, false);
            model.name = "Knight";
            foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true))
                if (KnightHide.Contains(r.name)) r.gameObject.SetActive(false);

            var b = CalcBounds(model);
            float s = b.size.y > 0.01f ? 1.9f / b.size.y : 1f;
            model.transform.localScale = Vector3.one * s;
            model.transform.localPosition = new Vector3(0f, -b.min.y * s, 0f);

            var anim = model.GetComponent<Animator>();
            if (anim == null) anim = model.AddComponent<Animator>();
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var man = root.AddComponent<MannequinAnimator>();
            man.anim = anim;
            man.menuShowcase = true;
            man.clipIdle = C("Idle");
            man.clipIdleB = C("IdleB");
            man.clipCheer = C("Cheer");
            man.clipPickUp = C("PickUp");
            man.clipInteract = C("Interact");
            return root;
        }

        static AnimationClip C(string n) => clips.TryGetValue(n, out var c) ? c : null;

        static Transform FindDeepChild(Transform root, string name)
        {
            foreach (Transform c in root)
            {
                if (c.name == name) return c;
                var r = FindDeepChild(c, name);
                if (r != null) return r;
            }
            return null;
        }

        static Bounds CalcBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        static GameObject MakePlayerFallback(GameObject root)
        {
            // old cube rig — only used if the imported character is missing
            var anim = root.AddComponent<RunnerAnimator>();
            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0, 0.85f, 0);
            Prim(PrimitiveType.Cube, "Torso", mats["Shirt"], body.transform, new Vector3(0, 0.32f, 0), new Vector3(0.62f, 0.6f, 0.36f));
            anim.body = body.transform;
            return SavePrefab(root, "Player");
        }

        // ================= project settings =================

        static void ConfigureProject()
        {
            PlayerSettings.productName = "Warrior Run";
            PlayerSettings.companyName = "WarriorRun";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.someshsampat.warriorrun");
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);

            // app icon
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Sprites/AppIcon.png");
            if (icon != null)
            {
                var imp = (TextureImporter)AssetImporter.GetAtPath(Root + "/Sprites/AppIcon.png");
                imp.textureType = TextureImporterType.Default;
                imp.alphaIsTransparency = true;
                imp.npotScale = TextureImporterNPOTScale.None;
                imp.SaveAndReimport();
                icon = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Sprites/AppIcon.png");
                try
                {
                    // fill every slot of every icon kind so counts always match
                    foreach (IconKind kind in Enum.GetValues(typeof(IconKind)))
                    {
                        var icons = PlayerSettings.GetIcons(NamedBuildTarget.Android, kind);
                        if (icons == null || icons.Length == 0) continue;
                        for (int i = 0; i < icons.Length; i++) icons[i] = icon;
                        PlayerSettings.SetIcons(NamedBuildTarget.Android, icons, kind);
                    }
                    PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Unknown, new[] { icon });
                }
                catch (Exception e) { Log("Icon set failed (non-fatal): " + e.Message); }
            }

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(Root + "/Scenes/Menu.unity", true),
                new EditorBuildSettingsScene(Root + "/Scenes/Game.unity", true)
            };
            Log("Project configured");
        }
    }
}
