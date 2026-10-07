using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace WarriorRun.EditorTools
{
    /// <summary>
    /// Re-skins every flat surface with WarriorRun/DetailSurface — a procedural
    /// shader that adds world-space mottle, strata bands, up-face dusting and
    /// per-instance tint jitter so cloned props stop reading as clones.
    ///
    /// Two paths:
    ///   * ReskinChildren — called by the builders on every instantiated model;
    ///     swaps each renderer's flat materials for cached detail-skin copies.
    ///     Covers glTF/FBX embedded materials (where import postprocessors are
    ///     unreliable) with zero asset duplication per model.
    ///   * Apply() — re-skins the shared Materials library in place (prims:
    ///     walls, floors, beds, curbs); called inside the rebuild pipeline
    ///     after ExtraContent.
    /// Emissive mats (lava veins, neon guides) and transparent mats (water
    /// film) are left untouched.
    /// </summary>
    public static class DetailSurfaceUpgrade
    {
        const string ShaderName = "WarriorRun/DetailSurface";
        const string MatsDir = "Assets/WarriorRun/Materials";
        const string DetailDir = "Assets/WarriorRun/Materials/Detail";

        static readonly Dictionary<string, Material> cache = new();

        /// <summary>Swap every flat material on the object's renderers for a
        /// cached detail-skin clone (keyed on source name+color, so identical
        /// source mats across files collapse to one skin).</summary>
        public static void ReskinChildren(GameObject go)
        {
            var sh = Shader.Find(ShaderName);
            if (sh == null || go == null) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var src = r.sharedMaterials;
                var dst = new Material[src.Length];
                bool any = false;
                for (int i = 0; i < src.Length; i++)
                {
                    dst[i] = Reskinned(src[i], sh);
                    any |= dst[i] != src[i];
                }
                if (any) r.sharedMaterials = dst;
            }
        }

        static Material Reskinned(Material src, Shader sh)
        {
            if (src == null || !IsConvertible(src)) return src;
            var baseCol = SourceBaseColor(src);
            var key = src.name + "|" + ColorUtility.ToHtmlStringRGB(baseCol);
            if (cache.TryGetValue(key, out var hit) && hit != null) return hit;

            var path = $"{DetailDir}/{Sanitize(src.name)}_{ColorUtility.ToHtmlStringRGB(baseCol)}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                EnsureFolder(DetailDir);
                m = new Material(sh) { name = src.name + "_d" };
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = sh;
            m.SetColor("_BaseColor", baseCol);
            TuneForName(m, src.name);
            EditorUtility.SetDirty(m);
            cache[key] = m;
            return m;
        }

        /// <summary>Re-skin the shared flat material library in place — every
        /// prim-built floor/wall/bed picks up detail without code changes.</summary>
        public static void Apply()
        {
            var sh = Shader.Find(ShaderName);
            if (sh == null) { Debug.LogWarning("[DetailSurface] shader not found — skipping"); return; }

            int converted = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { MatsDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("/Detail/")) continue; // already a skin clone
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null || !IsConvertible(m)) continue;
                var c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white;
                m.shader = sh;
                m.SetColor("_BaseColor", c);
                TuneForName(m, m.name);
                EditorUtility.SetDirty(m);
                converted++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[DetailSurface] reskinned {converted} shared materials");
        }

        /// <summary>Flat-color lit/unlit materials only — leave particles,
        /// skyboxes, flow shaders, transparents and emissives untouched.</summary>
        static bool IsConvertible(Material m)
        {
            var sn = m.shader.name;
            if (sn == ShaderName) return false;
            // URP Lit/Unlit mats + glTFast's imported glTF shader-graph mats
            bool family = sn.StartsWith("Universal Render Pipeline/") || sn.Contains("glTF");
            if (!family) return false;
            if (sn.Contains("Particles") || sn.Contains("Terrain")) return false;
            // textured mats (Nature3 PNGs, dungeon atlases) keep their maps
            if (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null) return false;
            if (m.renderQueue >= (int)UnityEngine.Rendering.RenderQueue.Transparent) return false;
            if (m.HasProperty("_Surface") && m.GetFloat("_Surface") > 0.5f) return false;
            if (m.IsKeywordEnabled("_EMISSION") && m.HasProperty("_EmissionColor")
                && m.GetColor("_EmissionColor").maxColorComponent > 0.05f) return false;
            // glTFast emission lives in "emissiveFactor" — keep those mats glowing
            if (m.HasProperty("emissiveFactor")
                && m.GetColor("emissiveFactor").maxColorComponent > 0.01f) return false;
            return true;
        }

        /// <summary>Read a material's flat base color across importers:
        /// glTFast uses baseColorFactor, URP mats _BaseColor, legacy _Color.</summary>
        static Color SourceBaseColor(Material src)
        {
            if (src.shader.name.Contains("glTF"))
            {
                if (src.HasProperty("baseColorFactor")) return src.GetColor("baseColorFactor");
                if (src.HasProperty("baseColor")) return src.GetColor("baseColor");
            }
            if (src.HasProperty("_BaseColor")) return src.GetColor("_BaseColor");
            if (src.HasProperty("_Color")) return src.GetColor("_Color");
            if (src.HasProperty("baseColorFactor")) return src.GetColor("baseColorFactor");
            return Color.white;
        }

        /// <summary>Per-category surface presets, keyed off material name.</summary>
        static void TuneForName(Material m, string n)
        {
            n = n.ToLowerInvariant();
            float mottle = 0.22f, mottleScale = 1.6f, bands = 0f, bandFreq = 2.4f;
            float dust = 0.15f, jitter = 0.30f;
            var dustCol = new Color(0.85f, 0.80f, 0.70f);

            bool Has(params string[] k) { foreach (var s in k) if (n.Contains(s)) return true; return false; }

            if (Has("rock", "stone", "basalt", "cliff", "mesa", "boulder", "obsidian",
                    "strata", "canyon", "gorge", "spire", "rubble", "frozenrock", "bed"))
            {
                // banded sediment + dusted tops — the canyon/gorge look
                bands = 0.28f; bandFreq = 2.6f; dust = 0.28f; jitter = 0.45f;
            }
            if (Has("brick", "wall", "ruin", "column", "pillar", "plaza", "temple"))
            {
                bands = 0.10f; bandFreq = 4.0f; dust = 0.22f; jitter = 0.25f;
            }
            if (Has("leaf", "canopy", "foliage", "fern", "bush", "frond", "palm",
                    "petal", "blossom", "flower", "moss", "grass", "pine"))
            {
                // strong hue spread — repeated trees read as a mixed forest
                mottle = 0.30f; mottleScale = 2.4f; jitter = 0.55f; dust = 0f;
            }
            if (Has("trunk", "bark", "wood", "log", "stump", "drift", "charred", "branch"))
            {
                mottle = 0.30f; mottleScale = 3.2f; bands = 0.16f; bandFreq = 6.0f;
            }
            if (Has("snow", "ice", "frost", "drift"))
            {
                mottle = 0.14f; dust = 0.4f; dustCol = new Color(0.95f, 0.98f, 1.0f); jitter = 0.25f;
            }
            if (Has("sand", "dirt", "soil", "path", "meadow", "track", "desert", "beach", "dune"))
            {
                mottle = 0.26f; mottleScale = 1.1f; dust = 0.10f; jitter = 0.20f;
            }
            if (Has("asphalt", "road", "walk", "slide", "flume", "pave"))
            {
                mottle = 0.18f; mottleScale = 2.8f; dust = 0.08f; jitter = 0.12f;
            }
            if (Has("lava", "ember", "ash", "magma"))
            {
                mottle = 0.34f; mottleScale = 2.0f; bands = 0.14f; dust = 0.12f;
                dustCol = new Color(0.16f, 0.12f, 0.11f);
            }
            if (Has("roof", "tile", "awning", "cap"))
            {
                bands = 0.12f; bandFreq = 8.0f; dust = 0.15f; jitter = 0.20f;
            }
            if (Has("paint", "glass", "trim", "metal", "car", "taxi", "van", "hydrant", "sign"))
            {
                mottle = 0.10f; jitter = 0.15f; dust = 0.10f;
            }

            m.SetFloat("_MottleAmt", mottle);
            m.SetFloat("_MottleScale", mottleScale);
            m.SetFloat("_BandAmt", bands);
            m.SetFloat("_BandFreq", bandFreq);
            m.SetFloat("_DustAmt", dust);
            m.SetColor("_DustColor", dustCol);
            m.SetFloat("_JitterAmt", jitter);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }

        static string Sanitize(string n)
        {
            foreach (var c in System.IO.Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
            return n.Replace(' ', '_');
        }
    }
}
