using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEngine;

namespace WarriorRun.EditorTools
{
    /// <summary>
    /// One-shot icon + splash setup for Warrior Run.
    ///   WarriorRun ▸ Apply App Icon + Splash  — or batch:
    ///   -executeMethod WarriorRun.EditorTools.AppIconSetup.Apply
    /// Assigns the adaptive icon (API 26+, Unity auto-fills older launchers from
    /// it) plus the classic application icon slots, disables the Unity splash
    /// and points the Android 12+ system splash at the badge artwork.
    /// </summary>
    public static class AppIconSetup
    {
        const string Dir = "Assets/WarriorRun/Sprites/AppIcon";

        [MenuItem("WarriorRun/Apply App Icon + Splash")]
        public static void Apply()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            FixImporters();
            AssignPlatformIcons();
            AssignBuildTargetIcons();
            ConfigureSplash();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[WarriorRun] App icons + splash applied");
        }

        // ---------- textures ----------

        static void FixImporters()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Dir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is not TextureImporter ti) continue;
                ti.textureType = TextureImporterType.Default;
                ti.mipmapEnabled = false;
                ti.isReadable = true;                       // platform-icon pipeline reads pixels
                ti.alphaIsTransparency = true;
                ti.npotScale = TextureImporterNPOTScale.None;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.SaveAndReimport();
            }
        }

        static Texture2D T(string name)
        {
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/" + name + ".png");
            if (t == null) Debug.LogWarning("[WarriorRun] missing icon asset: " + name);
            return t;
        }

        // ---------- platform icons ----------

        static void AssignPlatformIcons()
        {
            var platform = NamedBuildTarget.Android;
            foreach (var kind in PlayerSettings.GetSupportedIconKinds(platform))
            {
                var icons = PlayerSettings.GetPlatformIcons(platform, kind);
                string kname = kind.ToString();
                foreach (var icon in icons)
                {
                    if (icon.maxLayerCount >= 2)              // adaptive: [bg, fg] layers
                        icon.SetTextures(T("IconBG_" + icon.width), T("IconFG_" + icon.width));
                    else if (kname.Contains("Round"))
                        icon.SetTextures(T("IconRound_" + icon.width));
                    else
                        icon.SetTextures(T("Icon_" + icon.width));
                }
                PlayerSettings.SetPlatformIcons(platform, kind, icons);
                Debug.Log("[WarriorRun] " + kname + " icons: " + icons.Length + " slots");
            }
        }

        // ---------- classic build-target + default icon ----------

        static void AssignBuildTargetIcons()
        {
            var platform = NamedBuildTarget.Android;
            var sizes = PlayerSettings.GetIconSizes(platform, IconKind.Application);
            var texs = new List<Texture2D>();
            foreach (int s in sizes)
            {
                var t = T("Icon_" + s);
                texs.Add(t != null ? t : Texture2D.whiteTexture);
            }
            PlayerSettings.SetIcons(platform, texs.ToArray(), IconKind.Application);
            Debug.Log("[WarriorRun] Application icons: " + texs.Count + " slots");

            var defNbt = NamedBuildTarget.FromBuildTargetGroup(BuildTargetGroup.Unknown);
            var defSizes = PlayerSettings.GetIconSizes(defNbt, IconKind.Application);
            var defTexs = new List<Texture2D>();
            foreach (int s in defSizes)
            {
                var t = T("Icon_" + s);
                defTexs.Add(t != null ? t : Texture2D.whiteTexture);
            }
            if (defTexs.Count > 0)
                PlayerSettings.SetIcons(defNbt, defTexs.ToArray(), IconKind.Application);
        }

        // ---------- splash ----------

        static void ConfigureSplash()
        {
            // Unity 6: splash is optional on every plan — drop it entirely.
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            PlayerSettings.SplashScreen.logos = new PlayerSettings.SplashScreenLogo[0];

            // Android 12+ still shows a system splash window on cold start —
            // give it the badge artwork instead of the blank default.
            var splash = T("SplashIcon");
            if (splash == null) return;
            try
            {
                var ps = Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings");
                var so = new SerializedObject(ps);
                var prop = so.FindProperty("androidSplashScreen");
                if (prop != null)
                {
                    prop.objectReferenceValue = splash;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    Debug.Log("[WarriorRun] androidSplashScreen -> SplashIcon");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[WarriorRun] splash icon patch skipped: " + e.Message);
            }
        }
    }
}
