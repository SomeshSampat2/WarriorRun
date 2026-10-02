using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

namespace WarriorRun.UI
{
    /// <summary>
    /// Full-screen loading overlay, built entirely at runtime so no scene file
    /// needs manual wiring. Shown while the next scene streams in async —
    /// the main thread stays free to animate, so the transition never stutters.
    /// </summary>
    public class LoadingOverlay : MonoBehaviour
    {
        static bool loading;

        /// <summary>Crossfade-safe scene hop: call instead of SceneManager.LoadScene.</summary>
        public static void RunToScene(int buildIndex, string caption = "ENTERING THE ARENA")
        {
            if (loading) return;              // one transition at a time — taps can't double-fire
            loading = true;
            Time.timeScale = 1f;
            var go = new GameObject("LoadingOverlay");
            DontDestroyOnLoad(go);
            var ov = go.AddComponent<LoadingOverlay>();
            ov.Build(caption);
            ov.StartCoroutine(ov.LoadRoutine(buildIndex));
        }

        // ---- visuals ----
        CanvasGroup group;
        Image fill;
        Image[] dots;
        TMP_Text tipText;
        TMP_Text pctText;
        float shown;

        static readonly string[] Tips =
        {
            "TIP  —  SWIPE LEFT / RIGHT TO SWITCH LANES",
            "TIP  —  SWIPE UP TO JUMP  •  SWIPE DOWN TO SLIDE",
            "TIP  —  GRAB THE MAGNET TO PULL COINS IN",
            "TIP  —  SHIELD TANKS ONE HIT",
        };

        Sprite CircleSprite()
        {
            const int S = 64;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            float half = S * 0.5f;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(half, half));
                    float a = Mathf.Clamp01((half - 3f - d) / 6f);   // soft edge
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
        }

        RectTransform Box(string name, Transform parent, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        Image Img(RectTransform rt, Color c, Sprite s = null)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.color = c;
            if (s != null) img.sprite = s;
            img.raycastTarget = true;
            return img;
        }

        TMP_Text Txt(RectTransform rt, string s, float size, Color c)
        {
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (TMP_Settings.defaultFontAsset != null) t.font = TMP_Settings.defaultFontAsset;
            t.text = s;
            t.fontSize = size;
            t.color = c;
            t.alignment = TextAlignmentOptions.Center;
            t.characterSpacing = 6f;
            t.raycastTarget = false;
            return t;
        }

        static readonly Color BgCol   = new Color(0.045f, 0.06f, 0.10f, 1f);
        static readonly Color Gold    = new Color(1.00f, 0.72f, 0.18f, 1f);
        static readonly Color SoftWht = new Color(0.85f, 0.88f, 0.95f, 1f);
        static readonly Color Track   = new Color(0.16f, 0.19f, 0.26f, 1f);

        void Build(string caption)
        {
            var cv = gameObject.AddComponent<Canvas>();
            cv.renderMode = RenderMode.ScreenSpaceOverlay;
            cv.sortingOrder = 200;
            var sc = gameObject.AddComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1080f, 1920f);
            group = gameObject.AddComponent<CanvasGroup>();

            Img(Box("Bg", transform, Vector2.zero, new Vector2(5000f, 5000f)), BgCol)
                .raycastTarget = true; // eat every touch — nothing under the overlay is clickable

            Txt(Box("Title", transform, new Vector2(0f, 330f), new Vector2(1000f, 120f)),
                "WARRIOR RUN", 88f, Gold);
            Txt(Box("Cap", transform, new Vector2(0f, 240f), new Vector2(1000f, 60f)),
                caption, 34f, SoftWht);

            // comet spinner: 8 dots chasing a circle
            var spinRoot = Box("Spin", transform, new Vector2(0f, 20f), Vector2.one);
            var circle = CircleSprite();
            dots = new Image[8];
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * 2f / 8f;
                var d = Img(Box("d" + i, spinRoot, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 78f,
                            new Vector2(26f, 26f)), Gold, circle);
                d.raycastTarget = false;
                dots[i] = d;
            }

            // progress track + fill
            var track = Img(Box("Track", transform, new Vector2(0f, -150f), new Vector2(560f, 16f)), Track);
            track.raycastTarget = false;
            var fillRt = Box("Fill", track.transform, Vector2.zero, new Vector2(560f, 16f));
            fillRt.anchorMin = new Vector2(0f, 0.5f); fillRt.anchorMax = new Vector2(0f, 0.5f);
            fillRt.pivot = new Vector2(0f, 0.5f);
            fill = Img(fillRt, Gold);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillAmount = 0f;
            fill.raycastTarget = false;

            pctText = Txt(Box("Pct", transform, new Vector2(0f, -215f), new Vector2(400f, 50f)), "0%", 30f, SoftWht);
            tipText = Txt(Box("Tip", transform, new Vector2(0f, -560f), new Vector2(980f, 60f)),
                Tips[Random.Range(0, Tips.Length)], 28f, new Color(0.62f, 0.67f, 0.78f, 1f));
        }

        IEnumerator LoadRoutine(int buildIndex)
        {
            var op = SceneManager.LoadSceneAsync(buildIndex);
            op.allowSceneActivation = false;

            float t0 = Time.unscaledTime;
            const float minShow = 1.4f;            // let the intro read even on fast loads
            float raw = 0f;
            while (op.progress < 0.9f || Time.unscaledTime - t0 < minShow)
            {
                raw = Mathf.Max(raw, Mathf.Clamp01(op.progress / 0.9f));
                shown = Mathf.MoveTowards(shown, raw, Time.unscaledDeltaTime * 0.9f);
                yield return null;
            }
            while (shown < 0.999f)
            {
                shown = Mathf.MoveTowards(shown, 1f, Time.unscaledDeltaTime * 2.4f);
                yield return null;
            }
            op.allowSceneActivation = true;
            yield return null;                     // wait for activation + first frame
            yield return new WaitForSecondsRealtime(0.35f);

            // fade the overlay away over the new scene
            float f0 = Time.unscaledTime;
            while (Time.unscaledTime - f0 < 0.4f)
            {
                group.alpha = 1f - (Time.unscaledTime - f0) / 0.4f;
                yield return null;
            }
            loading = false;
            Destroy(gameObject);
        }

        void Update()
        {
            if (fill != null) fill.fillAmount = shown;
            if (pctText != null) pctText.text = Mathf.RoundToInt(shown * 100f) + "%";

            // comet trail — head dot bright, tail fades
            if (dots != null)
            {
                float head = Time.unscaledTime * 8f;
                for (int i = 0; i < dots.Length; i++)
                {
                    float phase = ((i - head) % 8f + 8f) % 8f / 8f;   // 0 = head … 1 = tail
                    var c = dots[i].color;
                    c.a = Mathf.Lerp(0.12f, 1f, 1f - phase);
                    dots[i].color = c;
                    float s = Mathf.Lerp(0.6f, 1.25f, 1f - phase);
                    dots[i].rectTransform.localScale = new Vector3(s, s, 1f);
                }
            }
        }
    }
}
