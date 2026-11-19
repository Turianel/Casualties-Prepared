using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CasPrepared
{
    internal sealed class CatalogEntry
    {
        public string Id;
        public string Name;
        public string Category;
        /// <summary>Display group used by the menu tabs (Ammo, Weapons, Gear, Medical, ...).</summary>
        public string Group;
        public string Description;
        /// <summary>Base value per unit. Fractional for loose rounds.</summary>
        public float BaseValue;

        private bool _iconResolved;
        private Sprite _icon;

        /// <summary>The item's world sprite, resolved lazily from its prefab.</summary>
        public Sprite Icon
        {
            get
            {
                if (_iconResolved) return _icon;
                _iconResolved = true;
                _icon = LoadoutCatalog.SpriteOf(Id);
                return _icon;
            }
        }
    }

    /// <summary>
    /// List of purchasable items. Uses the same rules traders use: value > 0 and not untradeable.
    /// Price = base trader value (without reputation modifier) * host multiplier.
    /// Loose rounds have value 0 in vanilla (their value lives in the magazine), so they are priced
    /// per round from the cheapest full magazine / box of the same ammo type.
    /// </summary>
    internal static class LoadoutCatalog
    {
        public const int MaxPerItem = 50;
        public const int MaxTotalItems = 200;

        private static List<CatalogEntry> _entries;
        private static List<string> _groups;
        private static Dictionary<string, CatalogEntry> _byId;
        private static readonly Dictionary<string, GameObject> PrefabCache = new Dictionary<string, GameObject>();

        public static IReadOnlyList<CatalogEntry> Entries
        {
            get
            {
                EnsureBuilt();
                return _entries ?? (IReadOnlyList<CatalogEntry>)Array.Empty<CatalogEntry>();
            }
        }

        /// <summary>Distinct display groups in menu order.</summary>
        public static IReadOnlyList<string> Groups
        {
            get
            {
                EnsureBuilt();
                return _groups ?? (IReadOnlyList<string>)Array.Empty<string>();
            }
        }

        public static bool TryGet(string id, out CatalogEntry entry)
        {
            EnsureBuilt();
            entry = null;
            return id != null && _byId != null && _byId.TryGetValue(id, out entry);
        }

        /// <summary>Price of <paramref name="count"/> units. Rounded once per line so fractional prices add up fairly.</summary>
        public static long PriceOf(CatalogEntry entry, float multiplier, int count = 1)
        {
            if (count <= 0) return 0;
            double raw = (double)entry.BaseValue * Math.Max(0f, multiplier) * count;
            return Math.Max(0L, (long)Math.Ceiling(raw - 1e-6));
        }

        /// <summary>Host-side check that the item actually has a prefab to instantiate.</summary>
        public static bool IsSpawnable(string id) => GetPrefab(id) != null;

        private static GameObject GetPrefab(string id)
        {
            if (PrefabCache.TryGetValue(id, out var prefab)) return prefab;
            try
            {
                prefab = Resources.Load(id) as GameObject;
            }
            catch
            {
                prefab = null;
            }
            PrefabCache[id] = prefab;
            return prefab;
        }

        private static AmmoScript GetAmmo(string id)
        {
            var prefab = GetPrefab(id);
            return prefab != null ? prefab.GetComponent<AmmoScript>() : null;
        }

        private static void EnsureBuilt()
        {
            if (_entries != null && _entries.Count > 0) return;
            if (Item.GlobalItems == null || Item.GlobalItems.Count == 0) return;

            var list = new List<CatalogEntry>();
            var looseRounds = new List<(string id, ItemInfo info, AmmoScript ammo)>();
            // cheapest value per round of a full magazine/box, per ammo type
            var perRound = new Dictionary<GunScript.AmmoType, float>();

            foreach (var pair in Item.GlobalItems)
            {
                var id = pair.Key;
                var info = pair.Value;
                if (info == null || string.IsNullOrEmpty(id) || id == SupplyCrate.ItemId) continue;
                if (info.untradeable || info.secret) continue;

                if (info.value <= 0)
                {
                    var round = GetAmmo(id);
                    if (round != null && round.itemType == AmmoScript.AmmoItemType.Round)
                        looseRounds.Add((id, info, round));
                    continue;
                }

                int value;
                try
                {
                    value = info.DefaultValue();
                }
                catch
                {
                    continue;
                }
                if (value <= 0) continue;

                var mag = GetAmmo(id);
                if (mag != null && mag.itemType == AmmoScript.AmmoItemType.Magazine && mag.maxRounds > 0)
                {
                    var unit = (float)value / mag.maxRounds;
                    if (!perRound.TryGetValue(mag.ammoType, out var best) || unit < best)
                        perRound[mag.ammoType] = unit;
                }

                list.Add(NewEntry(id, info, value));
            }

            foreach (var (id, info, ammo) in looseRounds)
                if (perRound.TryGetValue(ammo.ammoType, out var unit) && unit > 0f)
                    list.Add(NewEntry(id, info, unit));

            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            _entries = list;
            _byId = list.ToDictionary(e => e.Id, e => e);
            _groups = list.Select(e => e.Group).Distinct()
                .OrderBy(g => Array.IndexOf(GroupOrder, g) is var i && i >= 0 ? i : GroupOrder.Length)
                .ThenBy(g => g, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static CatalogEntry NewEntry(string id, ItemInfo info, float value)
        {
            return new CatalogEntry
            {
                Id = id,
                Name = string.IsNullOrEmpty(info.fullName) ? id : info.fullName,
                Category = info.category ?? string.Empty,
                Group = GroupOf(id, info),
                Description = info.description ?? string.Empty,
                BaseValue = value
            };
        }

        private static readonly string[] GroupOrder =
            { "Weapons", "Ammo", "Gear", "Medical", "Drug", "Food", "Water", "Tool", "Utility", "Container", "Other" };

        private static string GroupOf(string id, ItemInfo info)
        {
            var prefab = GetPrefab(id);
            if (prefab != null)
            {
                if (prefab.GetComponent<AmmoScript>() != null) return "Ammo";
                if (prefab.GetComponent<GunScript>() != null) return "Weapons";
            }
            if (info.wearable) return "Gear";
            var cat = (info.category ?? string.Empty).Trim();
            if (cat.Length == 0 || cat.Equals("custom", StringComparison.OrdinalIgnoreCase) ||
                cat.Equals("trash", StringComparison.OrdinalIgnoreCase))
                return "Other";
            return char.ToUpperInvariant(cat[0]) + cat.Substring(1).ToLowerInvariant();
        }

        internal static Sprite SpriteOf(string id)
        {
            var prefab = GetPrefab(id);
            if (prefab == null) return null;
            var sr = prefab.GetComponent<SpriteRenderer>();
            if (sr == null) sr = prefab.GetComponentInChildren<SpriteRenderer>(true);
            if (sr != null && sr.sprite != null) return sr.sprite;
            return null;
        }

        /// <summary>Spawned items match the price: magazines and ammo boxes come full.</summary>
        public static void PrepareSpawned(GameObject go)
        {
            if (go != null && go.TryGetComponent<AmmoScript>(out var ammo) &&
                ammo.itemType == AmmoScript.AmmoItemType.Magazine)
                ammo.rounds = ammo.maxRounds;
        }
    }
}
