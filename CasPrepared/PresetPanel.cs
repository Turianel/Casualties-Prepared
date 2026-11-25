using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Icon = CasPrepared.PixelIcons.Icon;

namespace CasPrepared
{
    /// <summary>
    /// Side panel of the loadout window: saved presets with their cost at the current prices,
    /// a name field to save the current selection, and load / overwrite / delete buttons for the selected preset.
    /// Overwriting and deleting ask for a second click.
    /// </summary>
    internal static class PresetPanel
    {
        private const int WindowId = 0x0CA59EA;
        private const string NameControl = "casprep_preset_name";
        private const float ConfirmSeconds = 3f;

        public static bool IsOpen { get; private set; }
        public static bool NameFocused { get; private set; }
        public static Rect Rect { get; private set; }

        private static string _name = string.Empty;
        private static Vector2 _scroll;
        private static float _viewHeight = 2000f;
        private static string _message;
        private static bool _messageIsError;
        private static string _confirmKey;
        private static float _confirmUntil;
        private static LoadoutPreset _hovered;
        private static LoadoutPreset _selected;
        private static LoadoutPreset _hoveredNext;

        private static GUIStyle _priceSource, _priceOk, _priceWarn, _priceDanger;

        public static void Toggle()
        {
            if (IsOpen) Close();
            else
            {
                IsOpen = true;
                _message = null;
                _confirmKey = null;
            }
        }

        public static void Close()
        {
            IsOpen = false;
            NameFocused = false;
            _confirmKey = null;
            _hovered = _hoveredNext = null;
        }

        public static bool ContainsMouse(Vector2 guiPoint) => IsOpen && Rect.Contains(guiPoint);

        /// <summary>Drawn next to the main window: on its right, or on its left when there is no room.</summary>
        public static void Draw(Rect main)
        {
            if (!IsOpen)
            {
                NameFocused = false;
                return;
            }
            float u = PixelSkin.Unit;
            float w = Mathf.Min(18f * u, Screen.width * 0.45f);
            float gap = u * 0.3f;
            float x = main.xMax + gap;
            if (x + w > Screen.width) x = main.x - gap - w;
            if (x < 0f) x = Screen.width - w;
            Rect = new Rect(x, main.y, w, main.height);
            GUI.Window(WindowId, Rect, DrawWindow, GUIContent.none, PixelSkin.Window);
        }

        private static void DrawWindow(int id)
        {
            GUI.skin = PixelSkin.Skin;
            float u = PixelSkin.Unit;
            EnsureStyles();
            if (Event.current.type == EventType.Layout)
            {
                _hovered = _hoveredNext;
                _hoveredNext = null;
            }
            if (_confirmKey != null && Time.unscaledTime > _confirmUntil) _confirmKey = null;
            if (_selected != null && !PresetStore.All.Contains(_selected)) _selected = null;

            GUILayout.BeginVertical();

            GUILayout.BeginHorizontal(GUILayout.Height(u * 1.8f));
            GUILayout.Label("PRESETS", PixelSkin.Title, GUILayout.ExpandHeight(true));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("X", PixelSkin.SmallButton, GUILayout.Width(u * 1.6f), GUILayout.Height(u * 1.6f))) Close();
            GUILayout.EndHorizontal();

            DrawNameRow(u);
            DrawList(u);
            DrawActions(u);
            DrawInfo(u);

            GUILayout.EndVertical();
            NameFocused = GUI.GetNameOfFocusedControl() == NameControl;
        }

        /// <summary>Name field + save: saves the current selection as a new preset (or overwrites one with that name).</summary>
        private static void DrawNameRow(float u)
        {
            GUILayout.BeginHorizontal(PixelSkin.Inset);
            GUI.SetNextControlName(NameControl);
            // maxLength stops typing at the limit
            _name = GUILayout.TextField(_name ?? string.Empty, PresetStore.MaxNameLength, PixelSkin.Field,
                GUILayout.Height(u * 1.6f), GUILayout.ExpandWidth(true));
            var clean = PresetStore.CleanName(_name);
            var existing = clean.Length > 0 ? PresetStore.Find(clean) : null;
            string key = "save:" + (existing?.Name ?? clean).ToLowerInvariant();
            bool canSave = clean.Length > 0 && LoadoutUI.HasSelection;
            if (IconButton(GUILayoutUtility.GetRect(u * 1.6f, u * 1.6f, GUILayout.Width(u * 1.6f), GUILayout.Height(u * 1.6f)),
                    Icon.Save, canSave, key))
                Save(clean, existing);
            GUILayout.EndHorizontal();
        }

        private static void DrawList(float u)
        {
            var presets = PresetStore.All;
            float rowH = Mathf.Round(u * 1.7f);
            int budget = LoadoutClient.Budget;
            float mult = LoadoutClient.Multiplier;

            GUILayout.BeginVertical(PixelSkin.Panel, GUILayout.ExpandHeight(true));
            _scroll = GUILayout.BeginScrollView(_scroll, false, true, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none);
            if (presets.Count == 0)
            {
                GUILayout.Label("No presets yet.", PixelSkin.Muted);
                GUILayout.Label("Pick items, type a name above and press save.", PixelSkin.Small);
            }
            else
            {
                var area = GUILayoutUtility.GetRect(10f, presets.Count * rowH, GUILayout.ExpandWidth(true));
                for (int i = 0; i < presets.Count; i++)
                {
                    var row = new Rect(area.x, area.y + i * rowH, area.width, rowH - 1f);
                    if (row.yMax < _scroll.y || row.y > _scroll.y + _viewHeight) continue;
                    DrawRow(row, presets[i], budget, mult, u);
                }
            }
            GUILayout.EndScrollView();
            if (Event.current.type == EventType.Repaint) _viewHeight = GUILayoutUtility.GetLastRect().height;
            GUILayout.EndVertical();
        }

        private static void DrawRow(Rect row, LoadoutPreset preset, int budget, float mult, float u)
        {
            bool hovered = row.Contains(Event.current.mousePosition);
            if (hovered) _hoveredNext = preset;
            bool selected = preset == _selected;

            if (GUI.Button(row, GUIContent.none, PixelSkin.Row) && !selected)
            {
                _selected = preset;
                _confirmKey = null;
            }
            if (selected && Event.current.type == EventType.Repaint) PixelSkin.TabActive.Draw(row, false, false, false, false);

            float pad = u * 0.3f;
            long cost = CostOf(preset, mult, out var missing);
            float priceW = u * 3.4f;
            var price = new Rect(row.xMax - priceW - pad, row.y, priceW, row.height);
            var style = cost > budget ? _priceDanger : missing > 0 ? _priceWarn : _priceOk;
            GUI.Label(price, cost + (missing > 0 ? "*" : string.Empty), style);

            var name = new Rect(row.x + pad, row.y, price.x - row.x - pad * 2f, row.height);
            PixelSkin.MarqueeLabel(name, preset.Name, selected ? PixelSkin.ItemNameAccent : PixelSkin.ItemName,
                "p:" + preset.Name, hovered);
        }

        /// <summary>Load / overwrite / delete for the selected preset.</summary>
        private static void DrawActions(float u)
        {
            var p = _selected;
            string lower = p?.Name.ToLowerInvariant();
            float h = u * 1.8f;
            GUILayout.BeginHorizontal();
            if (IconButton(GUILayoutUtility.GetRect(u, h, GUILayout.ExpandWidth(true), GUILayout.Height(h)),
                    Icon.Load, p != null, null))
                Load(p);
            GUILayout.Space(u * 0.2f);
            if (IconButton(GUILayoutUtility.GetRect(u, h, GUILayout.ExpandWidth(true), GUILayout.Height(h)),
                    Icon.Save, p != null && LoadoutUI.HasSelection, p == null ? null : "save:" + lower))
                Save(p.Name, p);
            GUILayout.Space(u * 0.2f);
            if (IconButton(GUILayoutUtility.GetRect(u, h, GUILayout.ExpandWidth(true), GUILayout.Height(h)),
                    Icon.Delete, p != null, p == null ? null : "del:" + lower))
                Delete(p);
            GUILayout.EndHorizontal();
        }

        private static void DrawInfo(float u)
        {
            var p = _hovered ?? _selected;
            string text;
            if (p != null)
            {
                long cost = CostOf(p, LoadoutClient.Multiplier, out var missing);
                text = $"{p.Name}: {p.Count} items, cost {cost} / {LoadoutClient.Budget}";
                if (missing > 0) text += $"\n* {missing} item type(s) not available here, skipped on load";
            }
            else
            {
#if STANDALONE
                text = "Select a preset. Costs use the current prices.";
#else
                text = "Select a preset. Costs use this host's prices.";
#endif
            }
            GUILayout.Label(text, PixelSkin.Small, GUILayout.Height(u * 2.1f));
            GUILayout.Label(_message ?? " ", _messageIsError ? PixelSkin.Danger : PixelSkin.Success, GUILayout.Height(u * 1.4f));
        }

        // ---- actions ----

        private static void Save(string name, LoadoutPreset existing)
        {
            if (existing != null && !Confirm("save:" + existing.Name.ToLowerInvariant()))
            {
                Say($"Click again to overwrite '{existing.Name}'.", true);
                return;
            }
            var error = PresetStore.Save(name, LoadoutUI.SelectionSnapshot());
            if (error != null)
            {
                Say(error, true);
                return;
            }
            _name = string.Empty;
            GUI.FocusControl(null);
            _selected = PresetStore.Find(name);
            Say($"Saved '{name}'.", false);
        }

        private static void Delete(LoadoutPreset preset)
        {
            if (!Confirm("del:" + preset.Name.ToLowerInvariant()))
            {
                Say($"Click again to delete '{preset.Name}'.", true);
                return;
            }
            var error = PresetStore.Delete(preset);
            if (error == null && preset == _selected) _selected = null;
            Say(error ?? $"Deleted '{preset.Name}'.", error != null);
        }

        private static void Load(LoadoutPreset preset)
        {
            _confirmKey = null;
            int skipped = LoadoutUI.LoadSelection(preset.Items);
            Say(skipped > 0 ? $"Loaded '{preset.Name}', {skipped} item(s) skipped." : $"Loaded '{preset.Name}'.", skipped > 0);
        }

        private static bool Confirm(string key)
        {
            if (_confirmKey == key && Time.unscaledTime <= _confirmUntil)
            {
                _confirmKey = null;
                return true;
            }
            _confirmKey = key;
            _confirmUntil = Time.unscaledTime + ConfirmSeconds;
            return false;
        }

        private static void Say(string text, bool error)
        {
            _message = text;
            _messageIsError = error;
        }

        // ---- helpers ----

        /// <summary>Cost at the current prices; <paramref name="missing"/> = item types not in the current list.</summary>
        private static long CostOf(LoadoutPreset preset, float mult, out int missing)
        {
            long cost = 0;
            missing = 0;
            foreach (var pair in preset.Items)
            {
                if (LoadoutCatalog.TryGet(pair.Key, out var entry))
                    cost += LoadoutCatalog.PriceOf(entry, mult, Mathf.Min(pair.Value, LoadoutCatalog.MaxPerItem));
                else missing++;
            }
            return cost;
        }

        /// <summary>Button with a pixel icon. Armed (waiting for the confirm click) buttons turn red.</summary>
        private static bool IconButton(Rect rect, Icon icon, bool enabled, string confirmKey)
        {
            var prev = GUI.enabled;
            GUI.enabled = prev && enabled;
            bool clicked = GUI.Button(rect, GUIContent.none, PixelSkin.SmallButton);
            GUI.enabled = prev;
            bool armed = confirmKey != null && confirmKey == _confirmKey;
            var color = !enabled ? new Color(1f, 1f, 1f, 0.35f) : armed ? PixelSkin.DangerColor : PixelSkin.TextColor;
            PixelIcons.Draw(rect, icon, color);
            return clicked && enabled;
        }

        private static void EnsureStyles()
        {
            if (_priceSource == PixelSkin.ItemPrice && _priceOk != null) return;
            _priceSource = PixelSkin.ItemPrice;
            _priceOk = new GUIStyle(_priceSource);
            _priceWarn = Colored(_priceSource, PixelSkin.WarningColor);
            _priceDanger = Colored(_priceSource, PixelSkin.DangerColor);
        }

        private static GUIStyle Colored(GUIStyle from, Color color)
        {
            var s = new GUIStyle(from);
            s.normal.textColor = s.hover.textColor = s.active.textColor = s.focused.textColor = color;
            return s;
        }
    }
}
