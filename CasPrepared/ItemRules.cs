using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CasPrepared
{
    /// <summary>
    /// Host-side changes to the item list: custom base prices (before the multiplier),
    /// hidden (banned) items and extra items that traders do not sell.
    /// </summary>
    internal sealed class ItemRules
    {
        private const string KeyPrices = "prices";
        private const string KeyBanned = "banned";
        private const string KeyAdded = "added";

        private readonly Dictionary<string, float> _prices = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly HashSet<string> _banned = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _added = new HashSet<string>(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, float> Prices => _prices;
        public IEnumerable<string> Banned => _banned;
        public IEnumerable<string> Added => _added;
        public int BannedCount => _banned.Count;
        public int AddedCount => _added.Count;

        /// <summary>Bumped on every change (local cache key).</summary>
        public int Revision { get; private set; }
        /// <summary>Content hash. Clients send the one they have; the host resends the rules when it differs.</summary>
        public string Hash { get; private set; }

        public ItemRules()
        {
            Hash = ComputeHash();
        }

        public bool TryGetPrice(string id, out float price) => _prices.TryGetValue(id, out price);
        public bool IsBanned(string id) => _banned.Contains(id);
        public bool IsAdded(string id) => _added.Contains(id);

        public void SetPrice(string id, float price) { _prices[id] = price; Changed(); }
        public bool ResetPrice(string id) { var r = _prices.Remove(id); if (r) Changed(); return r; }
        public int ResetAllPrices() { var n = _prices.Count; _prices.Clear(); if (n > 0) Changed(); return n; }
        public bool Ban(string id) { var r = _banned.Add(id); if (r) Changed(); return r; }
        public bool Unban(string id) { var r = _banned.Remove(id); if (r) Changed(); return r; }
        public int UnbanAll() { var n = _banned.Count; _banned.Clear(); if (n > 0) Changed(); return n; }
        public bool Add(string id) { var r = _added.Add(id); if (r) Changed(); return r; }

        /// <summary>Removes an extra item together with its custom price and hidden flag.</summary>
        public bool Remove(string id)
        {
            if (!_added.Remove(id)) return false;
            _prices.Remove(id);
            _banned.Remove(id);
            Changed();
            return true;
        }

        private void Changed()
        {
            Revision++;
            Hash = ComputeHash();
        }

        public JObject ToJson()
        {
            var prices = new JObject();
            foreach (var pair in _prices.OrderBy(p => p.Key, StringComparer.Ordinal)) prices[pair.Key] = pair.Value;
            return new JObject
            {
                [KeyPrices] = prices,
                [KeyBanned] = new JArray(_banned.OrderBy(x => x, StringComparer.Ordinal)),
                [KeyAdded] = new JArray(_added.OrderBy(x => x, StringComparer.Ordinal))
            };
        }

        /// <summary>Replaces the content. <paramref name="hash"/> keeps the sender's hash (multiplayer clients).</summary>
        public void ReplaceWith(JToken json, string hash = null)
        {
            _prices.Clear();
            _banned.Clear();
            _added.Clear();
            if (json is JObject obj)
            {
                if (obj[KeyPrices] is JObject prices)
                    foreach (var prop in prices.Properties())
                    {
                        if (string.IsNullOrWhiteSpace(prop.Name)) continue;
                        var value = prop.Value.Type == JTokenType.Float || prop.Value.Type == JTokenType.Integer
                            ? prop.Value.Value<float>() : float.NaN;
                        if (IsValidPrice(value)) _prices[prop.Name] = value;
                    }
                ReadSet(obj[KeyBanned], _banned);
                ReadSet(obj[KeyAdded], _added);
            }
            Revision++;
            Hash = string.IsNullOrEmpty(hash) ? ComputeHash() : hash;
        }

        public static bool IsValidPrice(float price) => !float.IsNaN(price) && !float.IsInfinity(price) && price >= 0f && price <= MaxPrice;
        public const float MaxPrice = 1000000f;

        private static void ReadSet(JToken token, HashSet<string> into)
        {
            if (!(token is JArray array)) return;
            foreach (var t in array)
            {
                var id = t.Type == JTokenType.String ? t.Value<string>() : null;
                if (!string.IsNullOrWhiteSpace(id)) into.Add(id);
            }
        }

        /// <summary>FNV-1a 64 of the canonical JSON.</summary>
        private string ComputeHash()
        {
            var text = ToJson().ToString(Formatting.None);
            ulong h = 14695981039346656037UL;
            foreach (var b in Encoding.UTF8.GetBytes(text))
            {
                h ^= b;
                h *= 1099511628211UL;
            }
            return h.ToString("x16", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>The local rules (host / single player), stored in their own JSON file next to the BepInEx config.</summary>
    internal static class ItemRulesStore
    {
        public static readonly ItemRules Local = new ItemRules();

        public static string FilePath => Path.Combine(Paths.ConfigPath, Plugin.ModGUID + ".items.json");

        public static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                Local.ReplaceWith(JToken.Parse(File.ReadAllText(FilePath)));
                Plugin.Logger.LogInfo($"Item rules loaded: {Local.Prices.Count} custom prices, {Local.BannedCount} hidden, {Local.AddedCount} added.");
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError($"Could not read {FilePath}: {e.Message}. Item rules are empty; the broken file is kept as .bad.");
                try { File.Copy(FilePath, FilePath + ".bad", true); } catch { /* best effort */ }
            }
        }

        public static void Save()
        {
            try
            {
                File.WriteAllText(FilePath, Local.ToJson().ToString(Formatting.Indented));
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError($"Could not write {FilePath}: {e.Message}");
                throw new Exception("Could not save the item rules file. See the BepInEx log.");
            }
        }
    }
}
