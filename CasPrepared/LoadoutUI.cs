using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace CasPrepared
{
    /// <summary>
    /// IMGUI loadout window in the Together / game settings style (see PixelSkin):
    /// catalog with search and category tabs, selected list, budget bar, spawn button.
    /// Spawn is disabled while cost exceeds budget. The host re-validates everything anyway.
    /// </summary>
    internal static class LoadoutUI
    {
        private const int WindowId = 0x0CA59E9;
        private const string SearchControl = "casprep_search";
        private const string AllGroup = "All";

        public static bool IsOpen { get; private set; }
        public static bool IsSearchFocused { get; private set; }

        private static readonly Dictionary<string, int> Selected = new Dictionary<string, int>();
        private static Rect _window;
        private static bool _placed;
        private static string _search = string.Empty;
        private static string _group = AllGroup;
        private static string _filterKey;
        private static List<CatalogEntry> _filtered = new List<CatalogEntry>();
        private static Vector2 _catalogScroll;
        private static Vector2 _selectedScroll;
        private static float _catalogViewHeight = 2000f;
        private static float _selectedViewHeight = 2000f;
        private static string _message;
        private static CatalogEntry _hovered;
        private static CatalogEntry _hoveredNext;

        /// <summary>True while game input should be ignored (typing in search or mouse over the window).</summary>
        public static bool BlocksGameInput
        {
            get
            {
                if (!IsOpen) return false;
                if (IsSearchFocused) return true;
                var mouse = Input.mousePosition;
                return _window.Contains(new Vector2(mouse.x, Screen.height - mouse.y));
            }
        }

        public static void Open()
        {
            IsOpen = true;
            _message = null;
            _filterKey = null; // rebuild filter (catalog may have loaded since)
        }

        public static void Close()
        {
            IsOpen = false;
            IsSearchFocused = false;
            _hovered = _hoveredNext = null;
        }

        public static void ClearSelection() => Selected.Clear();

        public static void SetMessage(string message) => _message = message;

        public static void Draw()
        {
            if (!IsOpen) return;
            PixelSkin.Ensure();
            PlaceWindow();

            var prevSkin = GUI.skin;
            GUI.skin = PixelSkin.Skin;
            try
            {
                _window = GUI.Window(WindowId, _window, DrawWindow, GUIContent.none, PixelSkin.Window);
                _window = ClampToScreen(_window);
            }
            finally
            {
                GUI.skin = prevSkin;
            }
        }

        private static void PlaceWindow()
        {
            float u = PixelSkin.Unit;
            float w = Mathf.Min(48f * u, Screen.width - 2f * u);
            float h = Mathf.Min(37f * u, Screen.height - 2f * u);
            if (!_placed || !Mathf.Approximately(_window.width, w) || !Mathf.Approximately(_window.height, h))
            {
                var centre = _placed ? _window.center : new Vector2(Screen.width / 2f, Screen.height / 2f);
                _window = new Rect(centre.x - w / 2f, centre.y - h / 2f, w, h);
                _placed = true;
            }
        }

        private static Rect ClampToScreen(Rect r)
        {
            r.x = Mathf.Clamp(r.x, 0f, Mathf.Max(0f, Screen.width - r.width));
            r.y = Mathf.Clamp(r.y, 0f, Mathf.Max(0f, Screen.height - r.height));
            return r;
        }

        private static void DrawWindow(int id)
        {
            GUI.skin = PixelSkin.Skin; // window callbacks run outside our Draw() scope
            float u = PixelSkin.Unit;
            int budget = LoadoutClient.Budget;
            float mult = LoadoutClient.Multiplier;
            long cost = TotalCost(mult);
            int count = Selected.Values.Sum();
            bool overBudget = cost > budget;
            if (Event.current.type == EventType.Layout)
            {
                _hovered = _hoveredNext;
                _hoveredNext = null;
            }

            GUILayout.BeginVertical();

            DrawTitleBar(u);
            DrawStatusStrip(u, mult);

            // ---- body: catalog | selection ----
            GUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
            DrawCatalog(u, mult);
            GUILayout.Space(u * 0.4f);
            DrawSelection(u, mult, count);
            GUILayout.EndHorizontal();

            DrawDescription(u);
            DrawFooter(u, budget, cost, count, overBudget);

            GUILayout.EndVertical();

            IsSearchFocused = GUI.GetNameOfFocusedControl() == SearchControl;
            GUI.DragWindow(new Rect(0, 0, 10000, u * 2.2f));
        }

        private static void DrawTitleBar(float u)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(u * 1.8f));
            GUILayout.Label("SUPPLY DROP", PixelSkin.Title, GUILayout.ExpandHeight(true));
            GUILayout.Label("loadout", PixelSkin.Muted, GUILayout.ExpandHeight(true));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("X", PixelSkin.SmallButton, GUILayout.Width(u * 1.6f), GUILayout.Height(u * 1.6f))) Close();
            GUILayout.EndHorizontal();
        }

        private static void DrawStatusStrip(float u, float mult)
        {
            GUILayout.BeginHorizontal(PixelSkin.Inset, GUILayout.Height(u * 1.7f));

#if !STANDALONE // standalone has no time limit
            GUILayout.Label("TIME", PixelSkin.Muted);
            if (LoadoutClient.HasTimeLimit)
            {
                var secs = LoadoutClient.RemainingSeconds;
                var t = TimeSpan.FromSeconds(secs);
                GUILayout.Label($"{(int)t.TotalMinutes:00}:{t.Seconds:00}",
                    secs < 20f ? PixelSkin.Danger : secs < 60f ? PixelSkin.Warning : PixelSkin.Label);
            }
            else
            {
                GUILayout.Label("NO LIMIT", PixelSkin.Label);
            }
            GUILayout.Space(u);
#endif

            float max = LoadoutClient.MaxDistance, dist = LoadoutClient.Distance;
            float frac = max > 0f ? dist / max : 0f;
            GUILayout.Label("RANGE", PixelSkin.Muted);
            GUILayout.Label($"{dist:0.0}/{max:0}",
                frac > 0.85f ? PixelSkin.Danger : frac > 0.6f ? PixelSkin.Warning : PixelSkin.Label);
            GUILayout.Space(u);

            GUILayout.Label("PRICES", PixelSkin.Muted);
            GUILayout.Label("x" + mult.ToString("0.##", CultureInfo.InvariantCulture),
                Mathf.Approximately(mult, 1f) ? PixelSkin.Label : PixelSkin.Accent);

            GUILayout.FlexibleSpace();
            GUILayout.Label("stay near your spawn", PixelSkin.Muted);
            GUILayout.EndHorizontal();
        }

        // ---- catalog ----

        private static void DrawCatalog(float u, float mult)
        {
            GUILayout.BeginVertical(PixelSkin.Panel, GUILayout.Width(_window.width * 0.56f), GUILayout.ExpandHeight(true));

            GUILayout.BeginHorizontal();
            GUILayout.Label("SEARCH", PixelSkin.Muted, GUILayout.Height(u * 1.6f));
            GUI.SetNextControlName(SearchControl);
            _search = GUILayout.TextField(_search ?? string.Empty, 40, PixelSkin.Field, GUILayout.Height(u * 1.6f), GUILayout.ExpandWidth(true));
            if (GUILayout.Button("X", PixelSkin.SmallButton, GUILayout.Width(u * 1.6f), GUILayout.Height(u * 1.6f)))
            {
                _search = string.Empty;
                GUI.FocusControl(null);
            }
            GUILayout.EndHorizontal();

            DrawGroupTabs(u);
            RefreshFilter();

            float rowH = Mathf.Round(u * 1.7f);
            _catalogScroll = GUILayout.BeginScrollView(_catalogScroll, false, true, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none);
            if (_filtered.Count == 0)
            {
                GUILayout.Label(LoadoutCatalog.Entries.Count == 0 ? "Item list is still loading..." : "No items found.", PixelSkin.Muted);
            }
            else
            {
                var area = GUILayoutUtility.GetRect(10f, _filtered.Count * rowH, GUILayout.ExpandWidth(true));
                for (int i = 0; i < _filtered.Count; i++)
                {
                    var row = new Rect(area.x, area.y + i * rowH, area.width, rowH - 1f);
                    // only rows in view (the list can hold a few hundred items)
                    if (row.yMax < _catalogScroll.y || row.y > _catalogScroll.y + _catalogViewHeight) continue;
                    DrawCatalogRow(row, _filtered[i], mult, u);
                }
            }
            GUILayout.EndScrollView();
            if (Event.current.type == EventType.Repaint) _catalogViewHeight = GUILayoutUtility.GetLastRect().height;

            GUILayout.Label("click: +1   shift+click: +5", PixelSkin.Small);
            GUILayout.EndVertical();
        }

        private static void DrawGroupTabs(float u)
        {
            var groups = LoadoutCatalog.Groups;
            if (groups.Count == 0) return;
            var labels = new List<string> { AllGroup };
            labels.AddRange(groups);
            if (!labels.Contains(_group)) _group = AllGroup;

            int perRow = Mathf.Clamp(Mathf.FloorToInt(_window.width * 0.56f / (u * 5.2f)), 3, labels.Count);
            for (int start = 0; start < labels.Count; start += perRow)
            {
                GUILayout.BeginHorizontal();
                for (int i = start; i < start + perRow; i++)
                {
                    if (i >= labels.Count)
                    {
                        // keep cell widths equal on the last row
                        GUILayout.Label(GUIContent.none, PixelSkin.TabFiller, GUILayout.Height(u * 1.3f));
                        continue;
                    }
                    bool active = labels[i] == _group;
                    if (GUILayout.Button(labels[i], active ? PixelSkin.TabActive : PixelSkin.Tab, GUILayout.Height(u * 1.3f)) && !active)
                    {
                        _group = labels[i];
                        _catalogScroll = Vector2.zero;
                    }
                }
                GUILayout.EndHorizontal();
            }
        }

        private static void DrawCatalogRow(Rect row, CatalogEntry entry, float mult, float u)
        {
            Selected.TryGetValue(entry.Id, out var inCart);
            if (GUI.Button(row, GUIContent.none, PixelSkin.Row))
                Add(entry.Id, Event.current.shift ? 5 : 1);
            if (row.Contains(Event.current.mousePosition)) _hoveredNext = entry;

            float pad = u * 0.3f;
            var icon = new Rect(row.x + pad, row.y + 2f, row.height - 4f, row.height - 4f);
            PixelSkin.DrawSprite(icon, entry.Icon);

            float priceW = u * 4.5f;
            var name = new Rect(icon.xMax + pad, row.y, row.width - icon.width - priceW - pad * 4f, row.height);
            PixelSkin.FitLabel(name, entry.Name, inCart > 0 ? PixelSkin.ItemNameAccent : PixelSkin.ItemName);
            var price = new Rect(row.xMax - priceW - pad, row.y, priceW, row.height);
            GUI.Label(price, UnitPrice(entry, mult), PixelSkin.ItemPrice);
        }

        // ---- selection ----

        private static void DrawSelection(float u, float mult, int count)
        {
            GUILayout.BeginVertical(PixelSkin.Panel, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            GUILayout.BeginHorizontal();
            GUILayout.Label("SELECTED", PixelSkin.Muted, GUILayout.Height(u * 1.6f));
            GUILayout.Label(count + " / " + LoadoutCatalog.MaxTotalItems, PixelSkin.Label, GUILayout.Height(u * 1.6f));
            GUILayout.FlexibleSpace();
            var prev = GUI.enabled;
            GUI.enabled = prev && Selected.Count > 0;
            if (GUILayout.Button("CLEAR", PixelSkin.SmallButton, GUILayout.Width(u * 4.5f), GUILayout.Height(u * 1.6f))) Selected.Clear();
            GUI.enabled = prev;
            GUILayout.EndHorizontal();

            float rowH = Mathf.Round(u * 1.7f);
            _selectedScroll = GUILayout.BeginScrollView(_selectedScroll, false, true, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none);
            if (Selected.Count == 0)
            {
                GUILayout.Label("Nothing selected yet.", PixelSkin.Muted);
                GUILayout.Label("Pick items on the left. They arrive in a supply crate at your feet.", PixelSkin.Small);
            }
            else
            {
                var lines = Selected.ToList();
                var area = GUILayoutUtility.GetRect(10f, lines.Count * rowH, GUILayout.ExpandWidth(true));
                for (int i = 0; i < lines.Count; i++)
                {
                    if (!LoadoutCatalog.TryGet(lines[i].Key, out var entry)) continue;
                    var row = new Rect(area.x, area.y + i * rowH, area.width, rowH - 1f);
                    if (row.yMax < _selectedScroll.y || row.y > _selectedScroll.y + _selectedViewHeight) continue;
                    DrawSelectedRow(row, entry, lines[i].Value, mult, u);
                }
            }
            GUILayout.EndScrollView();
            if (Event.current.type == EventType.Repaint) _selectedViewHeight = GUILayoutUtility.GetLastRect().height;
            GUILayout.EndVertical();
        }

        private static void DrawSelectedRow(Rect row, CatalogEntry entry, int amount, float mult, float u)
        {
            if (row.Contains(Event.current.mousePosition)) _hoveredNext = entry;
            float pad = u * 0.3f;
            float btn = row.height - 2f;

            var plus = new Rect(row.xMax - btn, row.y + 1f, btn, btn);
            var minus = new Rect(plus.x - btn - 2f, row.y + 1f, btn, btn);
            if (GUI.Button(plus, "+", PixelSkin.SmallButton)) Add(entry.Id, Event.current.shift ? 5 : 1);
            if (GUI.Button(minus, "-", PixelSkin.SmallButton)) Add(entry.Id, Event.current.shift ? -5 : -1);

            float priceW = u * 3.6f;
            var price = new Rect(minus.x - priceW - pad, row.y, priceW, row.height);
            GUI.Label(price, LoadoutCatalog.PriceOf(entry, mult, amount).ToString(), PixelSkin.ItemPrice);

            var icon = new Rect(row.x + pad, row.y + 2f, row.height - 4f, row.height - 4f);
            PixelSkin.DrawSprite(icon, entry.Icon);
            var name = new Rect(icon.xMax + pad, row.y, price.x - icon.xMax - pad * 2f, row.height);
            if (amount > 1)
            {
                // keep the count visible: only the name gets cut
                var countText = new GUIContent("x" + amount);
                float countW = PixelSkin.ItemNameAccent.CalcSize(countText).x;
                var nameOnly = new Rect(name.x, name.y, Mathf.Max(0f, name.width - countW), name.height);
                PixelSkin.FitLabel(nameOnly, entry.Name, PixelSkin.ItemName);
                GUI.Label(new Rect(nameOnly.xMax, name.y, countW, name.height), countText, PixelSkin.ItemNameAccent);
            }
            else
            {
                PixelSkin.FitLabel(name, entry.Name, PixelSkin.ItemName);
            }
        }

        // ---- footer ----

        private static void DrawDescription(float u)
        {
            var text = _hovered == null ? " " :
                string.IsNullOrEmpty(_hovered.Description) ? _hovered.Name : _hovered.Name + ": " + _hovered.Description;
            GUILayout.Label(text, PixelSkin.Small, GUILayout.Height(u * 2.1f));
        }

        private static void DrawFooter(float u, int budget, long cost, int count, bool overBudget)
        {
            GUILayout.BeginHorizontal(PixelSkin.Inset, GUILayout.Height(u * 3.5f));

            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            GUILayout.BeginHorizontal();
            GUILayout.Label("BUDGET", PixelSkin.Muted);
            GUILayout.Label(budget.ToString(), PixelSkin.Label);
            GUILayout.Space(u);
            GUILayout.Label("COST", PixelSkin.Muted);
            GUILayout.Label(cost.ToString(), overBudget ? PixelSkin.Danger : PixelSkin.Label);
            GUILayout.Space(u);
            GUILayout.Label(overBudget ? "OVER BUDGET" : "LEFT", overBudget ? PixelSkin.Danger : PixelSkin.Muted);
            if (!overBudget) GUILayout.Label((budget - cost).ToString(), PixelSkin.Success);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            var bar = GUILayoutUtility.GetRect(10f, u * 0.9f, GUILayout.ExpandWidth(true));
            PixelSkin.DrawBar(bar, budget > 0 ? cost / (float)budget : (cost > 0 ? 1f : 0f), overBudget);
            GUILayout.EndVertical();

            GUILayout.Space(u * 0.5f);
            bool canSpawn = LoadoutClient.IsAvailable && !LoadoutClient.BuyInFlight && count > 0 && !overBudget;
            var prev = GUI.enabled;
            GUI.enabled = prev && canSpawn;
            if (GUILayout.Button(LoadoutClient.BuyInFlight ? "SPAWNING..." : "SPAWN", PixelSkin.BigButton,
                    GUILayout.Width(u * 8f), GUILayout.ExpandHeight(true)))
            {
                _message = null;
                LoadoutClient.Buy(new Dictionary<string, int>(Selected));
            }
            GUI.enabled = prev;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (!string.IsNullOrEmpty(_message)) GUILayout.Label(_message, PixelSkin.Danger);
            GUILayout.FlexibleSpace();
            GUILayout.Label($"[{ModConfig.MenuKey.Value}] toggle menu   you can walk while it is open", PixelSkin.Small);
            GUILayout.EndHorizontal();
        }

        // ---- logic ----

        private static void Add(string id, int delta)
        {
            Selected.TryGetValue(id, out var current);
            int total = Selected.Values.Sum();
            if (delta > 0)
            {
                delta = Mathf.Min(delta, LoadoutCatalog.MaxPerItem - current, LoadoutCatalog.MaxTotalItems - total);
                if (delta <= 0) return;
            }
            current += delta;
            if (current <= 0) Selected.Remove(id);
            else Selected[id] = current;
        }

        private static long TotalCost(float mult)
        {
            long cost = 0;
            foreach (var pair in Selected)
                if (LoadoutCatalog.TryGet(pair.Key, out var entry))
                    cost += LoadoutCatalog.PriceOf(entry, mult, pair.Value);
            return cost;
        }

        /// <summary>Whole numbers for normal items, decimals for loose rounds (line total is rounded up once).</summary>
        private static string UnitPrice(CatalogEntry entry, float mult)
        {
            var unit = entry.BaseValue * Mathf.Max(0f, mult);
            return Mathf.Approximately(unit, Mathf.Round(unit))
                ? Mathf.RoundToInt(unit).ToString()
                : unit.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static void RefreshFilter()
        {
            var key = _group + "\n" + _search + "\n" + LoadoutCatalog.Entries.Count;
            if (_filterKey == key) return;
            _filterKey = key;
            var query = (_search ?? string.Empty).Trim();
            _filtered = LoadoutCatalog.Entries
                .Where(e => _group == AllGroup || e.Group == _group)
                .Where(e => query.Length == 0 ||
                            e.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            e.Id.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            e.Group.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
        }
    }

    /// <summary>Stops the character from reacting to keys/clicks aimed at the loadout window.</summary>
    [HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.HandleInput))]
    internal static class PlayerCamera_HandleInput_LoadoutPatch
    {
        private static bool Prefix() => !LoadoutUI.BlocksGameInput;
    }
}
