#if !STANDALONE
using Together;
#endif
using UnityEngine;

namespace CasPrepared
{
    /// <summary>
    /// IMGUI skin matching Casualties: Together's multiplayer menu: the game's 9-sliced UI blocks
    /// (white pixel frame), the retro pixel font and the colors of the game's settings menu.
    /// Built from Together's assets when available, otherwise from generated frames of the same look.
    /// Never touches the global GUI.skin outside our own draw calls.
    /// </summary>
    internal static class PixelSkin
    {
        // Palette of the game's settings menu (same values Together's GameTheme falls back to).
        public static readonly Color TextColor = Hex(0xE6E6E6);
        public static readonly Color MutedColor = Hex(0xA3A3A3);
        public static readonly Color AccentColor = Hex(0x91D1FF);
        public static readonly Color SuccessColor = Hex(0xA2E8AF);
        public static readonly Color WarningColor = Hex(0xFFC814);
        public static readonly Color DangerColor = Hex(0xFF8787);

        public static GUISkin Skin { get; private set; }
        /// <summary>Base font size in pixels; also used as the layout unit.</summary>
        public static int FontSize { get; private set; } = 20;
        public static float Unit => FontSize;

        public static GUIStyle Window, Panel, Inset, Title, Label, Muted, Small, Right, RightMuted,
            Accent, Success, Warning, Danger, Button, BigButton, SmallButton, Row, Field, Tab, TabActive, TabFiller,
            BarBack, BarFill, BarFillDanger, Banner,
            ItemName, ItemNameAccent, ItemPrice; // item rows in the catalog and selection lists (smaller font)

        private static float _builtScale = -1f;
        private static Font _builtFont;
        private static bool _builtWithTogether;

        private static int _builtW, _builtH;
        private static readonly System.Collections.Generic.List<Texture2D> OwnedTextures = new System.Collections.Generic.List<Texture2D>();

        // Together's UI block sprites (the game's own 9-slice frames). We scale them ourselves:
        // Together's scaled-texture cache is only refreshed on a resolution change, so its textures
        // can lag behind the current UI scale and end up smaller than their border (IMGUI then draws nothing).
#if STANDALONE
        // Same game sprites Together uses, found by name; the font is looked up in the game's own assets.
        private static Sprite TogetherSmall => GameAssets.UiBlockSmall;
        private static Sprite TogetherNano => GameAssets.UiBlockNano;
        private static Font GameFont => GameAssets.PixelFont;
        private static float UiScale => GameAssets.UiScale;
        private const float BaseFontSize = 20f; // Together's DEFAULT_RETROGAME_FONTSIZE
#else
        private static Sprite TogetherSmall => UIManager.uiBlockSmall;
        private static Sprite TogetherNano => UIManager.uiBlockNano;
        private static Font GameFont => CoopModAssets.gamefont;
        private static float UiScale => UIBullshit.uiScale;
        private static float BaseFontSize => UIBullshit.DEFAULT_RETROGAME_FONTSIZE;
#endif

        /// <summary>Call from OnGUI before drawing. Rebuilds on UI scale / asset changes.</summary>
        public static void Ensure()
        {
            float scale = CurrentScale();
            bool withTogether = TogetherSmall != null && TogetherNano != null;
            if (Skin != null && Mathf.Approximately(scale, _builtScale) && _builtFont == GameFont &&
                withTogether == _builtWithTogether && _builtW == Screen.width && _builtH == Screen.height)
                return;
            _builtW = Screen.width;
            _builtH = Screen.height;
            Build(scale, withTogether);
        }

        private static float CurrentScale()
        {
            float s = UiScale;
            if (s <= 0.01f || float.IsNaN(s)) s = Screen.height / 1080f;
            return Mathf.Clamp(s, 0.4f, 6f);
        }

        private static void Build(float scale, bool withTogether)
        {
            _builtScale = scale;
            _builtFont = GameFont;
            _builtWithTogether = withTogether;
            FontSize = Mathf.Max(10, Mathf.RoundToInt(BaseFontSize * scale));

            if (Skin != null) Object.Destroy(Skin);
            Skin = Object.Instantiate(GUI.skin);
            Skin.hideFlags = HideFlags.HideAndDontSave;
            if (GameFont != null) Skin.font = GameFont;

            // ---- textures (brightness/alpha pairs mirror Together's button skin) ----
            foreach (var t in OwnedTextures) if (t != null) Object.Destroy(t);
            OwnedTextures.Clear();

            int pixel = Mathf.Max(1, Mathf.RoundToInt(3f * scale)); // one "game pixel" on screen
            Frame small = null, nano = null;
            Texture2D nanoDim = null;
            if (withTogether)
            {
                try
                {
                    small = FromSprite(TogetherSmall, pixel, 4,
                        (1f, 0.97f), (1f, 0.85f), (0.65f, 0.75f), (0.4f, 0.75f));
                    nano = FromSprite(TogetherNano, pixel, 2,
                        (1f, 0.9f), (1f, 0.8f), (0.65f, 0.7f), (0.4f, 0.7f));
                    nanoDim = Tinted(TogetherNano, pixel, 0.45f, 0.6f);
                }
                catch (System.Exception e)
                {
                    Plugin.Logger.LogWarning("Together UI sprites unusable, using generated frames: " + e.Message);
                    small = nano = null;
                }
            }
            if (small == null || nano == null || nanoDim == null)
            {
                small = Frame.Generated(pixel, 2);
                nano = Frame.Generated(Mathf.Max(1, pixel / 2), 2);
                nanoDim = Generated(pixel, new Color(0.35f, 0.35f, 0.35f), new Color(0.05f, 0.05f, 0.05f, 0.7f));
            }
            var fill = Solid(AccentColor);
            var fillDanger = Solid(DangerColor);

            int fs = FontSize;
            int pad = Mathf.Max(2, Mathf.RoundToInt(fs * 0.35f));

            // ---- base text styles ----
            Label = Text(Skin.label, TextColor, fs);
            Label.wordWrap = false;
            Label.alignment = TextAnchor.MiddleLeft;
            Label.padding = new RectOffset(pad / 2, pad / 2, 0, 0);
            Label.margin = new RectOffset(2, 2, 2, 2);

            Muted = Text(Label, MutedColor, fs);
            Small = Text(Label, MutedColor, Mathf.RoundToInt(fs * 0.8f));
            Small.wordWrap = true;
            Small.alignment = TextAnchor.UpperLeft;
            Right = Text(Label, TextColor, fs);
            Right.alignment = TextAnchor.MiddleRight;
            RightMuted = Text(Right, MutedColor, fs);
            Accent = Text(Label, AccentColor, fs);
            Success = Text(Label, SuccessColor, fs);
            Warning = Text(Label, WarningColor, fs);
            Danger = Text(Label, DangerColor, fs);
            Title = Text(Label, TextColor, Mathf.RoundToInt(fs * 1.25f));

            int itemFs = Mathf.RoundToInt(fs * 0.8f);
            ItemName = Text(Label, TextColor, itemFs);
            ItemName.clipping = TextClipping.Clip;
            ItemNameAccent = Text(ItemName, AccentColor, itemFs);
            ItemPrice = Text(Right, TextColor, itemFs);

            // ---- containers ----
            Window = new GUIStyle(Skin.window)
            {
                border = Offset(small.Border),
                padding = new RectOffset(small.Border + pad, small.Border + pad, small.Border + pad, small.Border + pad),
                overflow = new RectOffset(0, 0, 0, 0),
                contentOffset = Vector2.zero
            };
            Window.normal.background = Window.onNormal.background = small.Normal;
            Window.hover.background = Window.onHover.background = small.Normal;
            Window.focused.background = Window.onFocused.background = small.Normal;
            Window.active.background = Window.onActive.background = small.Normal;

            Panel = Box(nano.Normal, nano.Border, pad);
            Inset = Box(nanoDim, nano.Border, pad / 2);
            Banner = Box(small.Normal, small.Border, pad);
            Banner.font = GameFont;
            Banner.fontSize = fs;
            Banner.alignment = TextAnchor.MiddleCenter;
            SetTextColor(Banner, WarningColor);

            BarBack = Box(nanoDim, nano.Border, 0);
            BarFill = Box(fill, 0, 0);
            BarFillDanger = Box(fillDanger, 0, 0);

            // ---- interactive ----
            Button = ButtonStyle(Skin.button, small, fs, pad);
            BigButton = ButtonStyle(Skin.button, small, Mathf.RoundToInt(fs * 1.15f), pad);
            SmallButton = ButtonStyle(Skin.button, nano, fs, Mathf.Max(1, pad / 3));
            SmallButton.padding = new RectOffset(0, 0, 0, 0);

            Row = new GUIStyle(Skin.button)
            {
                font = GameFont,
                fontSize = fs,
                alignment = TextAnchor.MiddleLeft,
                border = Offset(nano.Border),
                padding = new RectOffset(pad, pad, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                overflow = new RectOffset(0, 0, 0, 0)
            };
            Row.normal.background = null;
            Row.hover.background = Row.onHover.background = nano.Hover;
            Row.active.background = Row.onActive.background = nano.Active;
            Row.focused.background = null;
            SetTextColor(Row, TextColor);

            Tab = ButtonStyle(Skin.button, nano, Mathf.RoundToInt(fs * 0.7f), Mathf.Max(1, pad / 2));
            Tab.normal.background = nanoDim;
            SetTextColor(Tab, MutedColor);
            Tab.hover.textColor = TextColor;
            TabActive = new GUIStyle(Tab);
            TabActive.normal.background = TabActive.hover.background = nano.Normal;
            SetTextColor(TabActive, AccentColor);
            TabFiller = new GUIStyle(Tab);
            TabFiller.normal.background = TabFiller.hover.background = TabFiller.active.background = null;

            Field = new GUIStyle(Skin.textField)
            {
                font = GameFont,
                fontSize = fs,
                alignment = TextAnchor.MiddleLeft,
                border = Offset(nano.Border),
                padding = new RectOffset(nano.Border + pad, nano.Border + pad, nano.Border, nano.Border),
                margin = new RectOffset(2, 2, 2, 2),
                wordWrap = false,
                clipping = TextClipping.Clip
            };
            Field.normal.background = Field.onNormal.background = nanoDim;
            Field.hover.background = Field.onHover.background = nano.Dim;
            Field.focused.background = Field.onFocused.background = nano.Normal;
            SetTextColor(Field, TextColor);

            Skin.settings.cursorColor = TextColor;
            Skin.settings.selectionColor = new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.4f);

            // ---- scrollbars (Together uses the nano block for track and thumb) ----
            float barWidth = Mathf.Max(8f, Mathf.Round(fs * 0.8f));
            ScrollBar(Skin.verticalScrollbar, Skin.verticalScrollbarThumb, nanoDim, nano, barWidth, true);
            ScrollBar(Skin.horizontalScrollbar, Skin.horizontalScrollbarThumb, nanoDim, nano, barWidth, false);
            Skin.scrollView.normal.background = null;
            Skin.scrollView.padding = new RectOffset(0, 0, 0, 0);

            Skin.label = Label;
            Skin.button = Button;
            Skin.textField = Field;
            Skin.box = Panel;
            Skin.window = Window;
        }

        // ---- drawing helpers ----

        /// <summary>Draws a sprite (atlas-aware, aspect-fit) inside the rect.</summary>
        public static void DrawSprite(Rect rect, Sprite sprite)
        {
            if (sprite == null || sprite.texture == null || Event.current.type != EventType.Repaint) return;
            var tex = sprite.texture;
            var tr = sprite.textureRect;
            var uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);
            float aspect = tr.width / Mathf.Max(1f, tr.height);
            var fit = rect;
            if (aspect > 1f)
            {
                fit.height = rect.width / aspect;
                fit.y += (rect.height - fit.height) / 2f;
            }
            else
            {
                fit.width = rect.height * aspect;
                fit.x += (rect.width - fit.width) / 2f;
            }
            GUI.DrawTextureWithTexCoords(fit, tex, uv, true);
        }

        /// <summary>Horizontal bar: framed track with a filled part. Fraction is clamped to [0, 1].</summary>
        /// <summary>Label that cuts the text with ".." instead of running into the next column.</summary>
        public static void FitLabel(Rect rect, string text, GUIStyle style)
        {
            var content = new GUIContent(text);
            float max = rect.width - style.padding.horizontal;
            if (max > 0f && style.CalcSize(content).x - style.padding.horizontal > max)
            {
                // binary search the longest prefix that fits with the ellipsis
                int lo = 0, hi = text.Length;
                while (lo < hi)
                {
                    int mid = (lo + hi + 1) / 2;
                    content.text = text.Substring(0, mid).TrimEnd() + "..";
                    if (style.CalcSize(content).x - style.padding.horizontal <= max) lo = mid;
                    else hi = mid - 1;
                }
                content.text = text.Substring(0, lo).TrimEnd() + "..";
            }
            GUI.Label(rect, content, style);
        }

        public static void DrawBar(Rect rect, float fraction, bool danger)
        {
            GUI.Box(rect, GUIContent.none, BarBack);
            float inset = Mathf.Max(2f, Mathf.Round(Unit * 0.15f));
            var inner = new Rect(rect.x + inset, rect.y + inset, (rect.width - inset * 2f) * Mathf.Clamp01(fraction), rect.height - inset * 2f);
            if (inner.width >= 1f) GUI.Box(inner, GUIContent.none, danger ? BarFillDanger : BarFill);
        }

        // ---- builders ----

        private sealed class Frame
        {
            public readonly int Border;
            public readonly Texture2D Normal, Dim, Hover, Active;

            public Frame(int border, Texture2D normal, Texture2D dim, Texture2D hover, Texture2D active)
            {
                Border = Mathf.Max(1, border);
                Normal = normal;
                Dim = dim;
                Hover = hover;
                Active = active;
            }

            /// <summary>Fallback in the same spirit: dark fill, white pixel border.</summary>
            public static Frame Generated(int pixel, int borderPixels)
            {
                var white = new Color(0.95f, 0.95f, 0.95f);
                return new Frame(pixel * borderPixels,
                    PixelSkin.Generated(pixel, white, new Color(0.055f, 0.055f, 0.055f, 0.97f)),
                    PixelSkin.Generated(pixel, white * 0.85f, new Color(0.06f, 0.06f, 0.06f, 0.85f)),
                    PixelSkin.Generated(pixel, white * 0.65f, new Color(0.12f, 0.12f, 0.12f, 0.9f)),
                    PixelSkin.Generated(pixel, white * 0.45f, new Color(0.2f, 0.2f, 0.2f, 0.9f)));
            }
        }

        private static Frame FromSprite(Sprite sprite, int pixel, int defaultBorder,
            (float b, float a) normal, (float b, float a) dim, (float b, float a) hover, (float b, float a) active)
        {
            var rect = sprite.textureRect;
            int w = Mathf.RoundToInt(rect.width), h = Mathf.RoundToInt(rect.height);
            int border = Mathf.RoundToInt(sprite.border.x);
            if (border <= 0) border = defaultBorder;
            // IMGUI skips drawing when both borders don't fit inside the texture
            border = Mathf.Clamp(border, 1, Mathf.Max(1, (Mathf.Min(w, h) - 1) / 2));
            return new Frame(border * pixel,
                Tinted(sprite, pixel, normal.b, normal.a),
                Tinted(sprite, pixel, dim.b, dim.a),
                Tinted(sprite, pixel, hover.b, hover.a),
                Tinted(sprite, pixel, active.b, active.a));
        }

        /// <summary>Nearest-neighbour upscale by an integer factor plus brightness/alpha tint (like Together's TexDarkener).</summary>
        private static Texture2D Tinted(Sprite sprite, int pixel, float brightness, float alpha)
        {
            var src = sprite.texture;
            var r = sprite.textureRect;
            int sx = Mathf.RoundToInt(r.x), sy = Mathf.RoundToInt(r.y);
            int w = Mathf.RoundToInt(r.width), h = Mathf.RoundToInt(r.height);
            var srcPixels = src.GetPixels(sx, sy, w, h);

            int W = w * pixel, H = h * pixel;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color[W * H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                var c = srcPixels[(y / pixel) * w + x / pixel];
                float a = c.a;
                c *= brightness;
                c.a = a * alpha;
                pixels[y * W + x] = c;
            }
            tex.SetPixels(pixels);
            tex.Apply();
            OwnedTextures.Add(tex);
            return tex;
        }

        private static Texture2D Generated(int pixel, Color border, Color fill)
        {
            int size = pixel * 5;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool edge = x < pixel || y < pixel || x >= size - pixel || y >= size - pixel;
                // cut the corner pixel for the game's rounded pixel look
                bool corner = (x < pixel || x >= size - pixel) && (y < pixel || y >= size - pixel);
                pixels[y * size + x] = corner ? Color.clear : edge ? border : fill;
            }
            tex.SetPixels(pixels);
            tex.Apply();
            OwnedTextures.Add(tex);
            return tex;
        }

        private static Texture2D Solid(Color color)
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.HideAndDontSave
            };
            tex.SetPixel(0, 0, color);
            tex.Apply();
            OwnedTextures.Add(tex);
            return tex;
        }

        private static GUIStyle Text(GUIStyle from, Color color, int size)
        {
            var s = new GUIStyle(from) { font = GameFont, fontSize = size, richText = false };
            s.normal.background = null;
            s.hover.background = null;
            s.active.background = null;
            s.focused.background = null;
            SetTextColor(s, color);
            return s;
        }

        private static GUIStyle Box(Texture2D tex, int border, int pad)
        {
            var s = new GUIStyle
            {
                border = Offset(border),
                padding = new RectOffset(border + pad, border + pad, border + pad, border + pad),
                margin = new RectOffset(2, 2, 2, 2),
                font = GameFont
            };
            s.normal.background = tex;
            SetTextColor(s, TextColor);
            return s;
        }

        private static GUIStyle ButtonStyle(GUIStyle from, Frame frame, int fontSize, int pad)
        {
            var s = new GUIStyle(from)
            {
                font = GameFont,
                fontSize = fontSize,
                alignment = TextAnchor.MiddleCenter,
                border = Offset(frame.Border),
                padding = new RectOffset(frame.Border + pad, frame.Border + pad, frame.Border, frame.Border),
                margin = new RectOffset(2, 2, 2, 2),
                overflow = new RectOffset(0, 0, 0, 0),
                wordWrap = false
            };
            s.normal.background = s.onNormal.background = frame.Dim;
            s.hover.background = s.onHover.background = frame.Hover;
            s.active.background = s.onActive.background = frame.Active;
            s.focused.background = s.onFocused.background = frame.Dim;
            SetTextColor(s, TextColor);
            return s;
        }

        private static void ScrollBar(GUIStyle track, GUIStyle thumb, Texture2D trackTex, Frame frame, float width, bool vertical)
        {
            track.normal.background = trackTex;
            track.border = Offset(frame.Border);
            track.padding = new RectOffset(0, 0, 0, 0);
            track.margin = new RectOffset(2, 0, 0, 0);
            track.overflow = new RectOffset(0, 0, 0, 0);
            thumb.normal.background = frame.Normal;
            thumb.hover.background = frame.Dim;
            thumb.active.background = frame.Hover;
            thumb.border = Offset(frame.Border);
            thumb.padding = new RectOffset(0, 0, 0, 0);
            thumb.overflow = new RectOffset(0, 0, 0, 0);
            if (vertical)
            {
                track.fixedWidth = width;
                thumb.fixedWidth = width;
            }
            else
            {
                track.fixedHeight = width;
                thumb.fixedHeight = width;
            }
        }

        private static void SetTextColor(GUIStyle s, Color c)
        {
            s.normal.textColor = s.hover.textColor = s.active.textColor = s.focused.textColor = c;
            s.onNormal.textColor = s.onHover.textColor = s.onActive.textColor = s.onFocused.textColor = c;
        }

        private static RectOffset Offset(int n) => new RectOffset(n, n, n, n);

        private static Color Hex(int rgb)
        {
            return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
        }
    }
}
