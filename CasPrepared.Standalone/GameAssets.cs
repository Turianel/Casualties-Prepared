using System;
using System.Linq;
using TMPro;
using UnityEngine;

namespace CasPrepared
{
    /// <summary>
    /// UI assets for the standalone skin, taken from the game itself (no Together):
    /// the 9-slice UI block sprites (same lookup Together uses) and the game's "Retro Gaming" pixel font.
    /// Searches are retried a few times (assets may load after the first frame), then given up:
    /// PixelSkin falls back to generated frames and the default IMGUI font.
    /// </summary>
    internal static class GameAssets
    {
        private const int MaxAttempts = 10;
        private const float RetryInterval = 2f;

        private static Sprite _small, _nano;
        private static Font _font;
        private static int _attempts;
        private static float _nextAttempt;

        public static Sprite UiBlockSmall { get { Search(); return _small; } }
        public static Sprite UiBlockNano { get { Search(); return _nano; } }
        public static Font PixelFont { get { Search(); return _font; } }

        /// <summary>0 = let PixelSkin derive the scale from the screen height (1080p = 1).</summary>
        public static float UiScale => 0f;

        private static void Search()
        {
            if ((_small != null && _nano != null && _font != null) || _attempts >= MaxAttempts) return;
            if (Time.unscaledTime < _nextAttempt) return;
            _nextAttempt = Time.unscaledTime + RetryInterval;
            _attempts++;

            try
            {
                if (_small == null || _nano == null)
                {
                    var sprites = Resources.FindObjectsOfTypeAll<Sprite>();
                    if (_small == null) _small = FindSprite(sprites, "uiBlockSmall") ?? Exact(sprites, "uiBlock");
                    if (_nano == null) _nano = FindSprite(sprites, "uiBlockNano");
                }
                if (_font == null) _font = FindFont();
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("UI asset search failed: " + e.Message);
            }

            if (_small != null && _nano != null && _font != null)
                Plugin.Logger.LogInfo($"UI assets found: {_small.name}, {_nano.name}, font '{_font.name}'.");
            else if (_attempts >= MaxAttempts)
                Plugin.Logger.LogWarning($"Some UI assets were not found (frames: {(_small != null && _nano != null ? "ok" : "generated")}, " +
                    $"font: {(_font != null ? _font.name : "default")}).");
        }

        private static Sprite Exact(Sprite[] sprites, string name) =>
            sprites.FirstOrDefault(s => s != null && s.name == name);

        private static Sprite FindSprite(Sprite[] sprites, string name) =>
            Exact(sprites, name) ?? sprites.FirstOrDefault(s => s != null && s.name.Contains(name));

        private static Font FindFont()
        {
            bool IsRetro(string n) =>
                n != null && n.Replace(" ", "").IndexOf("retrogaming", StringComparison.OrdinalIgnoreCase) >= 0;

            // A plain Font asset (the TTF behind the game's TextMeshPro font) works directly with IMGUI.
            var fonts = Resources.FindObjectsOfTypeAll<Font>().Where(f => f != null && IsRetro(f.name)).ToList();
            var font = fonts.FirstOrDefault(f => f.dynamic && !f.name.Contains("FIXED"))
                       ?? fonts.FirstOrDefault(f => f.dynamic)
                       ?? fonts.FirstOrDefault();
            if (font != null) return font;

            // Otherwise take the source file referenced by the TextMeshPro font asset, if the build kept it.
            foreach (var asset in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                if (asset != null && IsRetro(asset.name) && asset.sourceFontFile != null)
                    return asset.sourceFontFile;
            return null;
        }
    }
}
