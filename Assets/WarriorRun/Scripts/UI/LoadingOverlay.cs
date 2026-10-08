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
    /// needs manual wiring. Rotates Blender-rendered biome backdrops behind a
    /// beveled "3D" progress bar with a knight token riding the fill edge.
    /// Shown while the next scene streams in async — the main thread stays
    /// free to animate, so the transition never stutters.
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
        RawImage bgA, bgB;
        RectTransform clipRt;      // fill clip — width animated, masks fill+shine
        RectTransform tokenRt;     // knight token riding the bar edge
        Image edgeGlow;
        Image shine;
        Image biomeTag;
        TMP_Text tipText;
        TMP_Text pctText;
        TMP_Text captionText;
        TMP_Text biomeText;
        float shown;

        // ---- backdrop rotation state ----
        Texture2D[] bgs;
        int bgIndex;               // index of the texture currently fading in
        int bgFront;               // 0 = A on top, 1 = B on top
        float bgTimer, bgFade;
        const float BgHold = 4.6f, BgFadeLen = 1.3f;

        static readonly string[] Biomes =
        {
            "EMERALD TRAIL", "MAGMA RIFT", "NEON DISTRICT", "FROSTPEAK PASS",
        };

        static readonly string[] Tips =
        {
            "SWIPE LEFT / RIGHT TO SWITCH LANES",
            "SWIPE UP TO JUMP  •  SWIPE DOWN TO SLIDE",
            "GRAB THE MAGNET TO PULL COINS IN",
            "THE SHIELD TANKS ONE HIT",
            "COIN STREAKS FILL YOUR SCORE MULTIPLIER",
        };
        int tipIndex;
        float tipTimer;
        CanvasGroup tipGroup;

        // ------------------------------------------------------------------
        // palette — matched to WarriorRunBuilder.cs
        static readonly Color Gold    = new Color(1.00f, 0.706f, 0.227f, 1f);
        static readonly Color GoldHi  = new Color(1.00f, 0.86f, 0.45f, 1f);
        static readonly Color Ink     = new Color(0.055f, 0.075f, 0.12f, 1f);
        static readonly Color Frame   = new Color(0.10f, 0.13f, 0.19f, 1f);
        static readonly Color Track   = new Color(0.045f, 0.055f, 0.085f, 1f);
        static readonly Color SoftWht = new Color(0.90f, 0.92f, 0.97f, 1f);
        static readonly Color Muted   = new Color(0.62f, 0.68f, 0.80f, 1f);

        const float BarW = 640f, BarH = 30f;

        // ------------------------------------------------------------------
        // procedural sprite/texture cache (built once per session)
        static Sprite s_rounded;   // soft rounded rect, white
        static Sprite s_bar;       // rounded bar w/ baked vertical shading
        static Sprite s_circle;    // soft circle
        static Sprite s_shine;     // diagonal sheen band
        static Texture2D s_scrim;  // vertical dark gradient

        static Sprite Rounded(int size, int radius, float edge = 5f)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float h = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // signed distance to rounded-rect boundary
                    float dx = Mathf.Abs(x - h) - (h - radius);
                    float dy = Mathf.Abs(y - h) - (h - radius);
                    float d = Mathf.Min(Mathf.Max(dx, dy), 0f)
                              + new Vector2(Mathf.Max(dx, 0f), Mathf.Max(dy, 0f)).magnitude
                              - radius;
                    float a = Mathf.Clamp01((-d) / edge + 0.5f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f,
                0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }

        static Sprite BarSprite(int w, int h, int radius)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float dx = Mathf.Abs(x - w * 0.5f) - (w * 0.5f - radius);
                    float dy = Mathf.Abs(y - h * 0.5f) - (h * 0.5f - radius);
                    float d = Mathf.Min(Mathf.Max(dx, dy), 0f)
                              + new Vector2(Mathf.Max(dx, 0f), Mathf.Max(dy, 0f)).magnitude
                              - radius;
                    float a = Mathf.Clamp01((-d) / 4f + 0.5f);
                    // baked 3D shading: bright top highlight → deep bottom shade
                    float v = y / (float)(h - 1);
                    float lum = Mathf.Lerp(0.55f, 1.25f, Mathf.SmoothStep(0f, 1f, v));
                    if (v > 0.82f) lum += (v - 0.82f) * 2.2f;           // top specular edge
                    if (v < 0.10f) lum *= 0.55f;                        // bottom crevice
                    lum = Mathf.Clamp01(lum);
                    tex.SetPixel(x, y, new Color(lum, lum, lum, a));
                }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f,
                0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }

        static Sprite Circle(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(half, half));
                    float a = Mathf.Clamp01((half - 2f - d) / 6f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        static Sprite Shine(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float t = (x - y * 0.55f) / (w * 0.55f);   // slanted band centre
                    float a = Mathf.Clamp01(1f - Mathf.Abs(t - 0.5f) / 0.30f);
                    a = a * a * 0.65f;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        }

        static Texture2D ScrimTex()
        {
            var tex = new Texture2D(4, 256, TextureFormat.RGBA32, false);
            for (int y = 0; y < 256; y++)
            {
                float v = y / 255f;                     // v=1 top of image
                // dark top (UI legibility), clear middle, darkest bottom (tips + bar)
                float a = Mathf.Lerp(0.62f, 0.0f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 0.98f, v)))
                        + Mathf.Lerp(0.78f, 0.0f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.34f, 0.0f, v)));
                for (int x = 0; x < 4; x++)
                    tex.SetPixel(x, y, new Color(0.02f, 0.03f, 0.06f, Mathf.Clamp01(a)));
            }
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.Apply();
            return tex;
        }

        void EnsureSprites()
        {
            if (s_rounded == null) s_rounded = Rounded(96, 30);
            if (s_bar == null) s_bar = BarSprite(256, 40, 20);
            if (s_circle == null) s_circle = Circle(96);
            if (s_shine == null) s_shine = Shine(96, 40);
            if (s_scrim == null) s_scrim = ScrimTex();
        }

        // ------------------------------------------------------------------
        // tiny builders
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

        RawImage Raw(RectTransform rt, Texture tex, float alpha = 1f)
        {
            var img = rt.gameObject.AddComponent<RawImage>();
            img.texture = tex;
            img.color = new Color(1f, 1f, 1f, alpha);
            return img;
        }

        TMP_Text Txt(RectTransform rt, string s, float size, Color c, FontStyles style = FontStyles.Normal)
        {
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (TMP_Settings.defaultFontAsset != null) t.font = TMP_Settings.defaultFontAsset;
            t.text = s;
            t.fontSize = size;
            t.color = c;
            t.fontStyle = style;
            t.alignment = TextAlignmentOptions.Center;
            t.characterSpacing = 6f;
            t.raycastTarget = false;
            return t;
        }

        // ------------------------------------------------------------------
        void Build(string caption)
        {
            EnsureSprites();

            var cv = gameObject.AddComponent<Canvas>();
            cv.renderMode = RenderMode.ScreenSpaceOverlay;
            cv.sortingOrder = 200;
            var sc = gameObject.AddComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1080f, 1920f);
            group = gameObject.AddComponent<CanvasGroup>();

            // ---- rotating biome backdrops (double-buffered RawImages) ----
            var texes = new List<Texture2D>();
            for (int i = 0; i < Biomes.Length; i++)
            {
                var t = Resources.Load<Texture2D>("Loading/bg" + i);
                if (t != null) texes.Add(t);
            }
            bgs = texes.ToArray();

            if (bgs.Length > 0)
            {
                bgIndex = Random.Range(0, bgs.Length);
                bgA = MakeBg("BgA");
                bgB = MakeBg("BgB");
                SetBg(bgA, bgs[bgIndex]);
                SetBg(bgB, bgs[(bgIndex + 1) % bgs.Length]);
                bgB.color = new Color(1f, 1f, 1f, 0f);
            }
            else
            {
                Img(Box("BgFallback", transform, Vector2.zero, new Vector2(6000f, 6000f)), Ink);
            }

            // eat every touch — nothing under the overlay is clickable
            var blocker = Img(Box("TouchBlocker", transform, Vector2.zero, new Vector2(6000f, 6000f)),
                new Color(0f, 0f, 0f, 0f));
            blocker.raycastTarget = true;

            // cinematic scrim — keeps UI readable over bright backdrops
            var scrim = Img(Box("Scrim", transform, Vector2.zero, new Vector2(6000f, 6000f)),
                Color.white, Sprite.Create(s_scrim, new Rect(0, 0, 4, 256), new Vector2(0.5f, 0.5f), 100f));
            scrim.raycastTarget = false;

            // ---- header ----
            var emblemTex = Resources.Load<Texture2D>("Loading/emblem");
            if (emblemTex != null)
            {
                var glow = Img(Box("EmblemGlow", transform, new Vector2(0f, 560f), new Vector2(300f, 300f)),
                    new Color(Gold.r, Gold.g, Gold.b, 0.28f), s_circle);
                glow.raycastTarget = false;
                var em = Raw(Box("Emblem", transform, new Vector2(0f, 560f), new Vector2(170f, 170f)), emblemTex);
                em.raycastTarget = false;
            }
            var title = Txt(Box("Title", transform, new Vector2(0f, 400f), new Vector2(1000f, 120f)),
                "WARRIOR RUN", 92f, Gold, FontStyles.Bold);
            var shadow = Txt(Box("TitleShadow", transform, new Vector2(3f, 394f), new Vector2(1000f, 120f)),
                "WARRIOR RUN", 92f, new Color(0f, 0f, 0f, 0.55f), FontStyles.Bold);
            shadow.transform.SetSiblingIndex(title.transform.GetSiblingIndex()); // behind title
            captionText = Txt(Box("Cap", transform, new Vector2(0f, 316f), new Vector2(1000f, 60f)),
                caption, 36f, SoftWht);

            // ---- biome tag pill ----
            var pill = Box("BiomePill", transform, new Vector2(0f, 226f), new Vector2(360f, 52f));
            biomeTag = Img(pill, new Color(0.05f, 0.07f, 0.11f, 0.82f), s_rounded);
            biomeTag.type = Image.Type.Sliced;
            biomeTag.raycastTarget = false;
            var pillEdge = Img(Box("PillEdge", pill.transform, new Vector2(0f, 24f), new Vector2(360f, 4f)),
                new Color(1f, 1f, 1f, 0.16f));
            pillEdge.raycastTarget = false;
            biomeText = Txt(Box("BiomeTxt", pill.transform, Vector2.zero, new Vector2(340f, 46f)),
                bgs.Length > 0 ? Biomes[bgIndex] : "", 26f, GoldHi);

            // ---- 3D progress bar ----
            // soft shadow under the whole bar
            var barShadow = Img(Box("BarShadow", transform, new Vector2(0f, -176f), new Vector2(BarW + 30f, 60f)),
                new Color(0f, 0f, 0f, 0.5f), s_rounded);
            barShadow.type = Image.Type.Sliced;
            barShadow.raycastTarget = false;

            var frame = Img(Box("BarFrame", transform, new Vector2(0f, -160f), new Vector2(BarW + 18f, BarH + 16f)),
                Frame, s_rounded);
            frame.type = Image.Type.Sliced;
            frame.raycastTarget = false;
            // frame bevel: light top lip, dark bottom crease
            Img(Box("FrameTop", frame.transform, new Vector2(0f, (BarH + 16f) / 2f - 2f), new Vector2(BarW + 14f, 3f)),
                new Color(1f, 1f, 1f, 0.22f), s_rounded).raycastTarget = false;
            Img(Box("FrameBot", frame.transform, new Vector2(0f, -(BarH + 16f) / 2f + 2f), new Vector2(BarW + 14f, 3f)),
                new Color(0f, 0f, 0f, 0.4f), s_rounded).raycastTarget = false;

            var track = Img(Box("Track", frame.transform, Vector2.zero, new Vector2(BarW, BarH)),
                Track, s_rounded);
            track.type = Image.Type.Sliced;
            track.raycastTarget = false;
            // inset groove: dark crease along the top inside edge
            Img(Box("TrackInset", track.transform, new Vector2(0f, BarH / 2f - 2f), new Vector2(BarW - 6f, 5f)),
                new Color(0f, 0f, 0f, 0.45f), s_rounded).raycastTarget = false;

            // clip rect — grows 0 → BarW; masks fill + shine
            clipRt = Box("FillClip", track.transform, Vector2.zero, new Vector2(0f, BarH));
            clipRt.anchorMin = clipRt.anchorMax = new Vector2(0f, 0.5f);
            clipRt.pivot = new Vector2(0f, 0.5f);
            var mask = clipRt.gameObject.AddComponent<RectMask2D>();
            mask.softness = new Vector2Int(0, 0);

            // fill anchored to clip's left edge so it stays world-fixed while the clip cuts it
            var fillRt = Box("Fill", clipRt, Vector2.zero, new Vector2(BarW, BarH));
            fillRt.anchorMin = fillRt.anchorMax = new Vector2(0f, 0.5f);
            fillRt.pivot = new Vector2(0f, 0.5f);
            var fillImg = Img(fillRt, Gold, s_bar);
            fillImg.type = Image.Type.Sliced;
            fillImg.raycastTarget = false;
            var shineRt = Box("Shine", clipRt, Vector2.zero, new Vector2(130f, BarH));
            shineRt.anchorMin = shineRt.anchorMax = new Vector2(0f, 0.5f);
            shineRt.pivot = new Vector2(0.5f, 0.5f);
            shine = Img(shineRt, new Color(1f, 1f, 1f, 0.85f), s_shine);
            shine.raycastTarget = false;

            // glow + knight token riding the leading edge
            edgeGlow = Img(Box("EdgeGlow", transform, new Vector2(-BarW / 2f, -160f), new Vector2(120f, 120f)),
                new Color(Gold.r, Gold.g, Gold.b, 0.4f), s_circle);
            edgeGlow.raycastTarget = false;

            tokenRt = Box("Token", transform, new Vector2(-BarW / 2f, -160f), new Vector2(96f, 96f));
            var ring = Img(Box("TokenRing", tokenRt, Vector2.zero, new Vector2(96f, 96f)), Gold, s_circle);
            ring.raycastTarget = false;
            var faceMask = Img(Box("TokenMask", tokenRt, Vector2.zero, new Vector2(80f, 80f)),
                Color.white, s_circle);
            faceMask.raycastTarget = false;
            var m = faceMask.gameObject.AddComponent<Mask>();
            m.showMaskGraphic = false;
            var faceTex = Resources.Load<Texture2D>("Loading/runner");
            if (faceTex != null)
            {
                var face = Raw(Box("TokenFace", faceMask.transform, Vector2.zero, new Vector2(80f, 80f)), faceTex);
                face.raycastTarget = false;
            }

            // percent + tip
            pctText = Txt(Box("Pct", transform, new Vector2(0f, -252f), new Vector2(400f, 70f)),
                "0%", 52f, SoftWht, FontStyles.Bold);

            var tipRt = Box("Tip", transform, new Vector2(0f, -700f), new Vector2(980f, 80f));
            tipGroup = tipRt.gameObject.AddComponent<CanvasGroup>();
            var tipLabel = Txt(Box("TipLabel", tipRt, new Vector2(0f, 34f), new Vector2(200f, 30f)),
                "TIP", 24f, Gold, FontStyles.Bold);
            tipIndex = Random.Range(0, Tips.Length);
            tipText = Txt(tipRt, Tips[tipIndex], 30f, Muted);
            tipText.rectTransform.anchoredPosition = new Vector2(0f, -8f);
            tipText.rectTransform.sizeDelta = new Vector2(980f, 50f);
        }

        RawImage MakeBg(string name)
        {
            var rt = Box(name, transform, Vector2.zero, new Vector2(600f, 600f));
            var fit = rt.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            var img = rt.gameObject.AddComponent<RawImage>();
            img.raycastTarget = false;
            return img;
        }

        static void SetBg(RawImage img, Texture2D t)
        {
            img.texture = t;
            var fit = img.GetComponent<AspectRatioFitter>();
            if (fit != null && t != null) fit.aspectRatio = (float)t.width / t.height;
        }

        // ------------------------------------------------------------------
        IEnumerator LoadRoutine(int buildIndex)
        {
            var op = SceneManager.LoadSceneAsync(buildIndex);
            op.allowSceneActivation = false;

            float t0 = Time.unscaledTime;
            const float minShow = 2.2f;            // let the showcase read even on fast loads
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

            // compile every shader variant + flush garbage while the overlay
            // still hides the scene — otherwise the first gameplay frames pay
            // for it as mid-run hitches
            if (captionText != null) captionText.text = "WARMING UP";
            yield return null;
            Shader.WarmupAllShaders();
            System.GC.Collect();
            yield return null;

            // fade the overlay away over the new scene
            float f0 = Time.unscaledTime;
            while (Time.unscaledTime - f0 < 0.5f)
            {
                group.alpha = 1f - (Time.unscaledTime - f0) / 0.5f;
                yield return null;
            }
            loading = false;
            Destroy(gameObject);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;

            // ---- backdrop crossfade + Ken Burns ----
            if (bgs != null && bgs.Length > 1)
            {
                bgTimer += dt;
                if (bgTimer >= BgHold)
                {
                    bgTimer = 0f;
                    bgFade = BgFadeLen;
                    bgIndex = (bgIndex + 1) % bgs.Length;
                    var next = bgFront == 0 ? bgB : bgA;
                    SetBg(next, bgs[bgIndex]);
                    next.transform.SetSiblingIndex(1);  // on top of the other bg, under the UI
                    bgFront = 1 - bgFront;
                }
                var front = bgFront == 0 ? bgA : bgB;
                if (bgFade > 0f)
                {
                    bgFade -= dt;
                    float a = Mathf.Clamp01(1f - bgFade / BgFadeLen);
                    front.color = new Color(1f, 1f, 1f, a);   // back stays at full alpha underneath
                    front.rectTransform.localScale = Vector3.one;  // no Ken Burns pop mid-fade
                    if (biomeText != null && bgFade <= BgFadeLen * 0.5f)
                        biomeText.text = Biomes[bgIndex];
                }
                else
                {
                    // slow push-in on the settled image
                    float kb = 1f + 0.07f * (bgTimer / BgHold);
                    front.rectTransform.localScale = new Vector3(kb, kb, 1f);
                }
            }

            // ---- bar: clip width + token position track the progress edge ----
            float px = Mathf.Lerp(-BarW / 2f, BarW / 2f, shown);
            if (clipRt != null)
                clipRt.sizeDelta = new Vector2(Mathf.Max(shown * BarW, 0.001f), BarH);
            if (tokenRt != null)
            {
                tokenRt.anchoredPosition = new Vector2(px, -160f);
                float pulse = 1f + Mathf.Sin(Time.unscaledTime * 6f) * 0.045f;
                tokenRt.localScale = new Vector3(pulse, pulse, 1f);
            }
            if (edgeGlow != null)
            {
                edgeGlow.rectTransform.anchoredPosition = new Vector2(px, -160f);
                float g = 0.30f + Mathf.Sin(Time.unscaledTime * 5f) * 0.14f;
                edgeGlow.color = new Color(Gold.r, Gold.g, Gold.b, g);
            }
            if (pctText != null)
            {
                int p = Mathf.RoundToInt(shown * 100f);
                pctText.text = p + "%";
                pctText.color = p >= 100 ? GoldHi : SoftWht;
            }

            // travelling sheen inside the fill (clip space: 0 = left edge of bar)
            if (shine != null)
            {
                float sw = Mathf.Repeat(Time.unscaledTime * 300f, BarW + 260f) - 130f;
                shine.rectTransform.anchoredPosition = new Vector2(sw, 0f);
            }

            // ---- tips rotate ----
            if (tipText != null && tipGroup != null)
            {
                tipTimer += dt;
                if (tipTimer > 5.5f)
                {
                    tipTimer = 0f;
                    tipIndex = (tipIndex + 1) % Tips.Length;
                    StartCoroutine(SwapTip());
                }
            }
        }

        IEnumerator SwapTip()
        {
            for (float a = 1f; a > 0f; a -= Time.unscaledDeltaTime * 4f)
            {
                tipGroup.alpha = a;
                yield return null;
            }
            tipText.text = Tips[tipIndex];
            for (float a = 0f; a < 1f; a += Time.unscaledDeltaTime * 4f)
            {
                tipGroup.alpha = a;
                yield return null;
            }
            tipGroup.alpha = 1f;
        }
    }
}
