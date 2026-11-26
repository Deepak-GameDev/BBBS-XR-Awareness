using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BBB.Awareness
{
    /// <summary>
    /// Small helpers to build world-space UI from code.
    /// Building the UI in code (instead of hand-made prefabs) keeps the scene setup to a single
    /// component and makes every visual easy to review in code.
    /// </summary>
    public static class UiFactory
    {
        public static readonly Color PersonColor = new Color(1.00f, 0.45f, 0.10f);   // warm orange
        public static readonly Color ObjectColor = new Color(0.15f, 0.80f, 1.00f);   // cool cyan
        public static readonly Color PanelColor = new Color(0.05f, 0.06f, 0.08f, 0.78f);
        public static readonly Color TextColor = new Color(0.95f, 0.96f, 0.98f);
        public static readonly Color DimTextColor = new Color(0.65f, 0.70f, 0.78f);

        private static Font s_font;
        private static readonly Dictionary<int, Sprite> s_circleCache = new Dictionary<int, Sprite>();

        public static Font Font
        {
            get
            {
                if (s_font == null)
                {
                    s_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                }
                return s_font;
            }
        }

        /// <summary>World-space canvas. 1 UI unit = metersPerUnit metres.</summary>
        public static Canvas CreateWorldCanvas(string name, Transform parent, Vector2 size, float metersPerUnit)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            go.transform.SetParent(parent, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            go.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 4f; // sharper text in VR
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = size;
            rt.localScale = Vector3.one * metersPerUnit;
            return canvas;
        }

        public static RectTransform CreateRect(string name, Transform parent, Vector2 anchoredPos, Vector2 size, Vector2 pivot)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = pivot;
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;
            return rt;
        }

        public static Image CreateImage(string name, Transform parent, Vector2 anchoredPos, Vector2 size, Color color, Sprite sprite = null)
        {
            var rt = CreateRect(name, parent, anchoredPos, size, new Vector2(0.5f, 0.5f));
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        public static Text CreateText(string name, Transform parent, Vector2 anchoredPos, Vector2 size, int fontSize, TextAnchor alignment, Color color, FontStyle style = FontStyle.Normal)
        {
            var rt = CreateRect(name, parent, anchoredPos, size, new Vector2(0.5f, 0.5f));
            var text = rt.gameObject.AddComponent<Text>();
            text.font = Font;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>
        /// Anti-aliased circle sprite generated at runtime.
        /// thickness01 = 0 -> filled disc; e.g. 0.1 -> thin ring.
        /// </summary>
        public static Sprite Circle(float thickness01 = 0f, int resolution = 128)
        {
            int key = Mathf.RoundToInt(thickness01 * 1000f) * 10000 + resolution;
            if (s_circleCache.TryGetValue(key, out var cached) && cached != null)
            {
                return cached;
            }

            var tex = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            float outer = resolution * 0.5f - 1f;
            float inner = thickness01 <= 0f ? -1f : outer * (1f - thickness01);
            var center = new Vector2(resolution * 0.5f, resolution * 0.5f);
            var pixels = new Color32[resolution * resolution];

            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                    float a = Mathf.Clamp01(outer - d + 0.5f);          // soft outer edge
                    if (inner > 0f)
                    {
                        a *= Mathf.Clamp01(d - inner + 0.5f);           // soft inner edge
                    }
                    pixels[y * resolution + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            var sprite = Sprite.Create(tex, new Rect(0, 0, resolution, resolution), new Vector2(0.5f, 0.5f), 100f);
            s_circleCache[key] = sprite;
            return sprite;
        }
    }
}
