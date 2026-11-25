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
        /// <summary>Base value per unit (custom price if the host set one). Fractional for loose rounds.</summary>
        public float BaseValue;
        /// <summary>Trader value per unit without host rules (0 for items traders do not sell).</summary>
        public float VanillaValue;
        /// <summary>Added by the host (not sold by traders).</summary>
        public bool Extra;

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
    /// Host item rules (custom prices, hidden and extra items) are applied on top, see ItemRules.
    /// </summary>
    internal static class LoadoutCatalog
    {
        public const int MaxPerItem = 50;
        public const int MaxTotalItems = 200;

        /// <summary>Rules to apply. Multiplayer clients switch this to the rules received from the host.</summary>
        public static Func<ItemRules> RulesProvider = () => ItemRulesStore.Local;

        // trader list without rules
        private static List<CatalogEntry> _base;
        private static Dictionary<string, CatalogEntry> _baseById;

        // effective list (rules applied)
        private static List<CatalogEntry> _entries;
        private static List<string> _groups;
        private static Dictionary<string, CatalogEntry> _byId;
        private static ItemRules _builtRules;
        private static int _builtRevision = -1;

        private static readonly Dictionary<string, GameObject> PrefabCache = new Dictionary<string, GameObject>();
        private static readonly Dictionary<string, Sprite> SpriteCache = new Dictionary<string, Sprite>();

        /// <summary>Changes whenever the effective list is rebuilt (rules changed).</summary>
        public static int Version { get; private set; }

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

        /// <summary>The item list has been read from the game (false in the very first frames).</summary>
        public static bool IsLoaded
        {
            get
            {
                EnsureBuilt();
                return _base != null;
            }
        }

        /// <summary>Item is on the trader list (before host rules).</summary>
        public static bool TryGetBase(string id, out CatalogEntry entry)
        {
            EnsureBuilt();
            entry = null;
            return id != null && _baseById != null && _baseById.TryGetValue(id, out entry);
        }

        public static IEnumerable<string> BaseIds
        {
            get
            {
                EnsureBuilt();
                return _base?.Select(e => e.Id) ?? Enumerable.Empty<string>();
            }
        }

        /// <summary>Every item id the game knows (except our crate).</summary>
        public static IEnumerable<string> AllItemIds =>
            Item.GlobalItems == null ? Enumerable.Empty<string>()
                : Item.GlobalItems.Keys.Where(k => !string.IsNullOrEmpty(k) && k != SupplyCrate.ItemId);

        public static bool IsGameItem(string id) =>
            id != null && id != SupplyCrate.ItemId && Item.GlobalItems != null && Item.GlobalItems.ContainsKey(id);

        /// <summary>Display name of any game item id.</summary>
        public static string NameOf(string id)
        {
            if (id != null && Item.GlobalItems != null && Item.GlobalItems.TryGetValue(id, out var info) && info != null &&
                !string.IsNullOrEmpty(info.fullName))
                return info.fullName;
            return id;
        }

        /// <summary>Trader value of any game item (0 when traders do not sell it).</summary>
        public static int TraderValueOf(string id)
        {
            if (id == null || Item.GlobalItems == null || !Item.GlobalItems.TryGetValue(id, out var info) || info == null) return 0;
            if (info.value <= 0) return 0;
            try
            {
                return Math.Max(0, info.DefaultValue());
            }
            catch
            {
                return 0;
            }
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
            if (_base == null) BuildBase();
            if (_base == null) return;

            var rules = RulesProvider?.Invoke() ?? ItemRulesStore.Local;
            if (_entries != null && ReferenceEquals(rules, _builtRules) && rules.Revision == _builtRevision) return;
            _builtRules = rules;
            _builtRevision = rules.Revision;
            BuildEffective(rules);
        }

        private static void BuildEffective(ItemRules rules)
        {
            var list = new List<CatalogEntry>();
            foreach (var b in _base)
            {
                if (rules.IsBanned(b.Id)) continue;
                list.Add(rules.TryGetPrice(b.Id, out var custom) ? Copy(b, custom) : b);
            }
            foreach (var id in rules.Added)
            {
                if (rules.IsBanned(id) || _baseById.ContainsKey(id) || !IsGameItem(id)) continue;
                float vanilla = TraderValueOf(id);
                // an extra item needs a price: the custom one, else the trader value
                bool hasCustom = rules.TryGetPrice(id, out var custom);
                float price = hasCustom ? custom : vanilla;
                if (!hasCustom && price <= 0f) continue;
                var entry = NewEntry(id, Item.GlobalItems[id], price);
                entry.VanillaValue = vanilla;
                entry.Extra = true;
                list.Add(entry);
            }

            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            _entries = list;
            _byId = list.ToDictionary(e => e.Id, e => e);
            _groups = list.Select(e => e.Group).Distinct()
                .OrderBy(g => Array.IndexOf(GroupOrder, g) is var i && i >= 0 ? i : GroupOrder.Length)
                .ThenBy(g => g, StringComparer.OrdinalIgnoreCase)
                .ToList();
            Version++;
        }

        private static CatalogEntry Copy(CatalogEntry b, float price) => new CatalogEntry
        {
            Id = b.Id,
            Name = b.Name,
            Category = b.Category,
            Group = b.Group,
            Description = b.Description,
            BaseValue = price,
            VanillaValue = b.VanillaValue
        };

        private static void BuildBase()
        {
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

            if (list.Count == 0) return;
            foreach (var e in list) e.VanillaValue = e.BaseValue;
            _base = list;
            _baseById = list.ToDictionary(e => e.Id, e => e);
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
            if (SpriteCache.TryGetValue(id, out var cached)) return cached;
            Sprite sprite = null;
            var prefab = GetPrefab(id);
            if (prefab != null)
            {
                var sr = prefab.GetComponent<SpriteRenderer>();
                if (sr == null) sr = prefab.GetComponentInChildren<SpriteRenderer>(true);
                if (sr != null && sr.sprite != null) sprite = sr.sprite;
            }
            SpriteCache[id] = sprite;
            return sprite;
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
