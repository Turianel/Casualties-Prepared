using System.Collections.Generic;
using UnityEngine;

namespace CasPrepared
{
    /// <summary>Small pixel-art icons drawn from text masks ('#' = pixel), tinted when drawn.</summary>
    internal static class PixelIcons
    {
        public enum Icon { Load, Save, Delete }

        private static readonly Dictionary<Icon, string[]> Masks = new Dictionary<Icon, string[]>
        {
            // arrow down into a tray
            [Icon.Load] = new[]
            {
                "...###...",
                "...###...",
                "...###...",
                ".#######.",
                "..#####..",
                "...###...",
                "....#....",
                "#.......#",
                "#########",
            },
            // floppy disk
            [Icon.Save] = new[]
            {
                "#######..",
                "#.#####.#",
                "#.#####.#",
                "#.......#",
                "#.......#",
                "#.#####.#",
                "#.#...#.#",
                "#.#####.#",
                "#########",
            },
            // bin
            [Icon.Delete] = new[]
            {
                "...###...",
                "#########",
                ".........",
                ".#######.",
                ".#.#.#.#.",
                ".#.#.#.#.",
                ".#.#.#.#.",
                ".#.#.#.#.",
                ".#######.",
            },
        };

        private static readonly Dictionary<Icon, Texture2D> Textures = new Dictionary<Icon, Texture2D>();

        /// <summary>Draws the icon centred in <paramref name="rect"/>, scaled by whole pixels.</summary>
        public static void Draw(Rect rect, Icon icon, Color color)
        {
            if (Event.current.type != EventType.Repaint) return;
            var tex = Get(icon);
            int scale = Mathf.Max(1, Mathf.FloorToInt(Mathf.Min(rect.width, rect.height) * 0.6f / tex.width));
            float w = tex.width * scale, h = tex.height * scale;
            var r = new Rect(Mathf.Round(rect.center.x - w / 2f), Mathf.Round(rect.center.y - h / 2f), w, h);
            var prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(r, tex);
            GUI.color = prev;
        }

        private static Texture2D Get(Icon icon)
        {
            if (Textures.TryGetValue(icon, out var tex) && tex != null) return tex;
            var mask = Masks[icon];
            int h = mask.Length, w = mask[0].Length;
            tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                // texture rows go bottom-up, masks top-down
                pixels[(h - 1 - y) * w + x] = x < mask[y].Length && mask[y][x] == '#' ? Color.white : Color.clear;
            tex.SetPixels(pixels);
            tex.Apply();
            Textures[icon] = tex;
            return tex;
        }
    }
}
