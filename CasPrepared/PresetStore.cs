using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CasPrepared
{
    internal sealed class LoadoutPreset
    {
        public string Name;
        public Dictionary<string, int> Items = new Dictionary<string, int>(StringComparer.Ordinal);
        public int Count => Items.Values.Sum();
    }

    /// <summary>
    /// Player-side saved loadouts. Only item ids and amounts are stored: prices always come from
    /// the current host (or the local game), so the same preset may cost differently on another server.
    /// One file shared by the multiplayer and the standalone build.
    /// </summary>
    internal static class PresetStore
    {
        public const int MaxNameLength = 32;
        public const int MaxPresets = 100;

        private static List<LoadoutPreset> _presets;

        public static string FilePath => Path.Combine(Paths.ConfigPath, "CasPrepared.presets.json");

        public static IReadOnlyList<LoadoutPreset> All
        {
            get
            {
                EnsureLoaded();
                return _presets;
            }
        }

        public static LoadoutPreset Find(string name)
        {
            EnsureLoaded();
            name = CleanName(name);
            return _presets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Trims, collapses spaces, drops control characters and cuts to MaxNameLength.</summary>
        public static string CleanName(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            var sb = new StringBuilder(name.Length);
            bool space = false;
            foreach (var c in name)
            {
                if (char.IsControl(c)) continue;
                if (char.IsWhiteSpace(c))
                {
                    space = sb.Length > 0;
                    continue;
                }
                if (space) sb.Append(' ');
                space = false;
                sb.Append(c);
            }
            var s = sb.ToString();
            return s.Length > MaxNameLength ? s.Substring(0, MaxNameLength).TrimEnd() : s;
        }

        /// <summary>Saves (or overwrites a preset with the same name). Returns an error or null.</summary>
        public static string Save(string name, IEnumerable<KeyValuePair<string, int>> items)
        {
            EnsureLoaded();
            name = CleanName(name);
            if (name.Length == 0) return "Type a name for the preset.";
            var lines = items.Where(p => !string.IsNullOrEmpty(p.Key) && p.Value > 0).ToList();
            if (lines.Count == 0) return "Nothing selected to save.";

            var preset = Find(name);
            if (preset == null)
            {
                if (_presets.Count >= MaxPresets) return $"Too many presets (max {MaxPresets}). Delete one first.";
                preset = new LoadoutPreset();
                _presets.Add(preset);
            }
            preset.Name = name;
            preset.Items = lines.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            Sort();
            return Write();
        }

        public static string Delete(LoadoutPreset preset)
        {
            EnsureLoaded();
            if (!_presets.Remove(preset)) return null;
            return Write();
        }

        private static void Sort() => _presets.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        private static void EnsureLoaded()
        {
            if (_presets != null) return;
            _presets = new List<LoadoutPreset>();
            try
            {
                if (!File.Exists(FilePath)) return;
                var root = JToken.Parse(File.ReadAllText(FilePath)) as JObject;
                if (!(root?["presets"] is JArray array)) return;
                foreach (var t in array.OfType<JObject>())
                {
                    var name = CleanName(t.Value<string>("name"));
                    if (name.Length == 0 || _presets.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))) continue;
                    var preset = new LoadoutPreset { Name = name };
                    if (t["items"] is JObject items)
                        foreach (var prop in items.Properties())
                            if (prop.Value.Type == JTokenType.Integer && prop.Value.Value<int>() > 0)
                                preset.Items[prop.Name] = prop.Value.Value<int>();
                    if (preset.Items.Count > 0) _presets.Add(preset);
                }
                Sort();
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError($"Could not read {FilePath}: {e.Message}. The broken file is kept as .bad.");
                try { File.Copy(FilePath, FilePath + ".bad", true); } catch { /* best effort */ }
            }
        }

        private static string Write()
        {
            try
            {
                var array = new JArray();
                foreach (var p in _presets)
                {
                    var items = new JObject();
                    foreach (var pair in p.Items) items[pair.Key] = pair.Value;
                    array.Add(new JObject { ["name"] = p.Name, ["items"] = items });
                }
                File.WriteAllText(FilePath, new JObject { ["presets"] = array }.ToString(Formatting.Indented));
                return null;
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError($"Could not write {FilePath}: {e.Message}");
                return "Could not save presets (see BepInEx log).";
            }
        }
    }
}
