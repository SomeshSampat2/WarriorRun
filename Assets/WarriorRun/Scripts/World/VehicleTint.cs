using UnityEngine;

namespace WarriorRun.World
{
    /// <summary>
    /// Two-tone paint job for the car &amp; plane. Pickups pick a random palette
    /// each time they spawn (OnEnable re-tints pooled instances); when the
    /// pickup is collected the same palette is passed to GameManager and the
    /// mounted vehicle gets the matching gradient via ApplyRig.
    /// Gradient runs front (+Z) to back along the rig's own length, detected
    /// per renderer — no material instances are created (MaterialPropertyBlock).
    /// </summary>
    public class VehicleTint : MonoBehaviour
    {
        public Color colorA = new Color(1f, 0.35f, 0.20f);
        public Color colorB = new Color(1f, 0.75f, 0.15f);

        static readonly (Color a, Color b)[] Palettes =
        {
            (Hex("#FF3D3D"), Hex("#FF9E2C")), // sunset inferno
            (Hex("#21E6FF"), Hex("#7A4DFF")), // synthwave cyan→violet
            (Hex("#B7FF2E"), Hex("#16C784")), // lime→teal
            (Hex("#FF4DD8"), Hex("#5B2EFF")), // magenta→indigo
            (Hex("#FFD24D"), Hex("#E33B3B")), // gold→crimson
            (Hex("#7FD8FF"), Hex("#1E3CFF")), // ice→ultramarine
            (Hex("#FF8E5E"), Hex("#D81E8F")), // coral→orchid
            (Hex("#F0FFF4"), Hex("#39D353")), // ghost mint
        };

        static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

        public static (Color a, Color b) RandomPalette() => Palettes[Random.Range(0, Palettes.Length)];

        void OnEnable()
        {
            var p = RandomPalette();
            colorA = p.a;
            colorB = p.b;
            ApplyRig(gameObject, colorA, colorB, Random.value < 0.5f);
        }

        /// <summary>
        /// Repaint a vehicle rig/pickup: paint materials get the a→b gradient by
        /// their position along the body, trim/accent parts take a darkened B,
        /// glow parts take B as emission. Glass, rubber, chrome and lights are
        /// left untouched.
        /// </summary>
        public static void ApplyRig(GameObject root, Color a, Color b, bool flip)
        {
            if (root == null) return;
            var mpb = new MaterialPropertyBlock();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mat = r.sharedMaterial;
                if (mat == null) continue;
                float t = Mathf.InverseLerp(-2.35f, 2.35f,
                    root.transform.InverseTransformPoint(r.bounds.center).z);
                if (flip) t = 1f - t;
                switch (mat.name)
                {
                    case "CarPaint":
                    case "PlanePaint":
                        mpb.SetColor("_BaseColor", Color.Lerp(a, b, t));
                        r.SetPropertyBlock(mpb);
                        break;
                    case "CarTrim":
                    case "PlaneAccent":
                        mpb.SetColor("_BaseColor", Color.Lerp(b, Color.black, 0.35f));
                        r.SetPropertyBlock(mpb);
                        break;
                    case "UnderGlow":
                        mpb.SetColor("_BaseColor", new Color(b.r, b.g, b.b, 0.30f));
                        mpb.SetColor("_EmissionColor", b * 1.8f);
                        r.SetPropertyBlock(mpb);
                        break;
                }
            }
            foreach (var l in root.GetComponentsInChildren<Light>(true))
                if (l.name == "GlowLight") l.color = b;
            foreach (var p in root.GetComponentsInChildren<ParticleSystem>(true))
                if (p.name == "AuraSparks")
                {
                    var m = p.main;
                    m.startColor = new Color(b.r * 1.5f, b.g * 1.5f, b.b * 1.5f, 0.5f);
                }
        }
    }
}
