using UnityEngine;
using UnityEngine.UI;

namespace WarriorRun.UI
{
    /// <summary>
    /// Twinkling sparkle for menu decoration: random-phased alpha + scale
    /// shimmer on a UI graphic. Unscaled time so it glows on every panel.
    /// </summary>
    public class Twinkle : MonoBehaviour
    {
        [SerializeField] float minScale = 0.45f;
        [SerializeField] float maxScale = 1.2f;
        [SerializeField] float speed = 2.1f;
        [SerializeField] float minAlpha = 0.1f;

        Graphic g;
        Vector3 baseScale;
        float phase;
        float baseAlpha = 1f;

        void Awake()
        {
            g = GetComponent<Graphic>();
            if (g != null) baseAlpha = g.color.a;
            baseScale = transform.localScale;
            phase = Random.value * Mathf.PI * 2f;
        }

        void Update()
        {
            float t = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * speed + phase);
            transform.localScale = baseScale * Mathf.Lerp(minScale, maxScale, t);
            if (g != null)
            {
                var c = g.color;
                c.a = Mathf.Lerp(minAlpha, baseAlpha, t);
                g.color = c;
            }
        }
    }
}
