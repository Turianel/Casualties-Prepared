using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CUCoreLib.Registries;
using HarmonyLib;
#if !STANDALONE
using Together;
#endif

namespace CasPrepared
{
    /// <summary>
    /// One console command, "casprep &lt;command&gt; [value]". Settings persist in the BepInEx config,
    /// item rules in their own file (ItemRulesStore). Multiplayer: setters are host-only.
    /// Standalone: setters work only outside a run.
    /// Console hints follow the typed sub-command (see ConsoleHintPatch).
    /// </summary>
    internal static class LoadoutCommands
    {
        public const string Name = "casprep";

        private sealed class Sub
        {
            public string Name;
            public string Help;
            public (string, string)[] Params = Array.Empty<(string, string)>();
            /// <summary>Autofill list for parameter i (0-based), or null.</summary>
            public Func<int, List<string>> Fill;
            /// <summary>Receives the parameters only (after the sub-command name).</summary>
            public Action<string[]> Run;
            public (string, string)[] Description;
        }

        private static readonly List<Sub> Subs = new List<Sub>();
        private static readonly (string, string)[] RootArgs = { ("command", "Type a command, Tab completes. casprep help lists them.") };
        private static Command _command;
        private static List<string> _subNames;
        private static Dictionary<int, List<string>> _rootFill;
        private static readonly Dictionary<int, List<string>> HintFill = new Dictionary<int, List<string>>();

#if STANDALONE
        private const string HostOnly = "";
#else
        private const string HostOnly = " (host)";
#endif

        public static void Register()
        {
            AddSubs();
            foreach (var s in Subs) s.Description = new[] { (s.Name, s.Help + HostTag(s)) }.Concat(s.Params).ToArray();
            _subNames = Subs.Select(s => s.Name).ToList();
            _rootFill = new Dictionary<int, List<string>> { [0] = _subNames };
            _command = new Command(Name, "CasPrepared loadout menu: settings and item list. casprep help lists the commands.",
                Execute, _rootFill, RootArgs);
            ConsoleCommandRegistry.Register(_command);
        }

        private static string HostTag(Sub s) => s.Name == "menu" || s.Name == "help" || s.Name == "giveloadout" || s.Name == "cheats" ? string.Empty : HostOnly;

        private static void AddSubs()
        {
            Add("menu", "Open or close the loadout menu.", p => LoadoutClient.ToggleMenu());
            Add("help", "List the commands.", p => Help());
            Add("status", Describe("show the settings", "list players and their loadout state"), p => Status());
            Add("enable", Describe("turn the loadout menu on or off for new runs", "turn the loadout menu on or off for sessions you host"),
                p =>
                {
                    if (p.Length >= 1)
                    {
                        RequireConfigAccess();
                        ModConfig.Enabled.Value = ParseBool(p[0]);
                    }
                    Log("Loadout menu: " + (ModConfig.Enabled.Value ? "ON" : "OFF"));
                },
                i => i == 0 ? OnOffList : null, ("on/off", "Omit to show the current state."));
            Add("budget", "Show or set the loadout budget.",
                p => IntSetting(p, "Budget", v => ModConfig.Budget.Value = v, () => ModConfig.Budget.Value),
                null, ("number", "New budget. Omit to show the current value."));
            Add("mult", "Show or set the price multiplier.",
                p => FloatSetting(p, "Price multiplier", v => ModConfig.PriceMultiplier.Value = v, () => ModConfig.PriceMultiplier.Value),
                null, ("number", "Multiplier for all prices (1.0 = trader price)."));
            Add("radius", "Show or set how far (tiles) a player may move from spawn before the menu closes.",
                p => FloatSetting(p, "Max distance", v => ModConfig.MaxDistance.Value = v, () => ModConfig.MaxDistance.Value),
                null, ("tiles", "Distance in world units."));
            Add("cratetime", "Show or set the supply crate lifetime in seconds.",
                p => IntSetting(p, "Crate lifetime", v => ModConfig.CrateLifetimeSeconds.Value = v, () => ModConfig.CrateLifetimeSeconds.Value),
                null, ("seconds", "Lifetime in seconds."));
#if !STANDALONE
            Add("window", "Show or set how long (seconds) the menu stays available after spawn. 0 or off disables the timer.",
                p =>
                {
                    if (p.Length >= 1 && IsOff(p[0])) p = new[] { "0" };
                    IntSetting(p, "Window seconds", v => ModConfig.WindowSeconds.Value = v, () => ModConfig.WindowSeconds.Value);
                    if (ModConfig.WindowSeconds.Value <= 0) Log("Time limit is off: only the distance limit applies.");
                },
                i => i == 0 ? OffList : null, ("seconds/off", "Window length in seconds. 0 / off = no time limit."));
            Add("reset", "Let a player pick a loadout again (opens a new window on their next spawn check).",
                p =>
                {
                    RequireConfigAccess();
                    if (p.Length < 1) throw new Exception("Usage: casprep reset <player name | steamid | all>");
                    var target = string.Join(" ", p);
                    Log($"Reset {LoadoutServer.Reset(target)} record(s) for '{target}'.");
                },
                i => i == 0 ? PlayerList(true) : null, ("player", "Player name, SteamID or 'all'."));
            Add("cheats", "Show whether Together cheat rules allow casprep giveloadout.", p => Cheats());
            Add("giveloadout", "(cheat) Open the loadout menu again. Needs sv_cheats (and AllowClientCheatCommands for clients).",
                p =>
                {
                    if (!Net.IsRunning) throw new Exception("Only works in a multiplayer session.");
                    // Fast local check; the host validates again with its own rules.
                    var blocked = LoadoutServer.CheatBlockReason(fromHost: Net.IsServer);
                    if (blocked != null) throw new Exception(blocked);
                    LoadoutClient.Give(p.Length >= 1 ? string.Join(" ", p) : null);
                },
                i => i == 0 ? PlayerList(false) : null, ("player", "Optional player name or SteamID. Omit for yourself."));
#endif
            // ---- item list ----
            Add("setprice", "Set a custom base price for an item (before the multiplier).", SetPrice,
                i => i == 0 ? Fill(FillKind.Listed) : null, ("item", "Item from the list."), ("price", "Price per unit before the multiplier."));
            Add("resetprice", "Remove a custom price (back to the trader price).", ResetPrice,
                i => i == 0 ? Fill(FillKind.Priced) : null, ("item/all", "Item with a custom price, or 'all'."));
            Add("ban", "Hide an item from the list.", Ban,
                i => i == 0 ? Fill(FillKind.Bannable) : null, ("item", "Item to hide."));
            Add("unban", "Show a hidden item again.", Unban,
                i => i == 0 ? Fill(FillKind.Banned) : null, ("item/all", "Hidden item, or 'all'."));
            Add("additem", "Add an item that traders do not sell.", AddItem,
                i => i == 0 ? Fill(FillKind.Addable) : null, ("item", "Any game item."), ("price", "Base price. Optional when the item has a trader price."));
            Add("removeitem", "Remove an added item.", RemoveItem,
                i => i == 0 ? Fill(FillKind.Added) : null, ("item", "Item added with casprep additem."));
            Add("items", "List custom prices, hidden and added items.", p => ListRules());
        }

        private static string Describe(string standalone, string multiplayer)
        {
#if STANDALONE
            return char.ToUpperInvariant(standalone[0]) + standalone.Substring(1) + ".";
#else
            return char.ToUpperInvariant(multiplayer[0]) + multiplayer.Substring(1) + ".";
#endif
        }

        private static void Add(string name, string help, Action<string[]> run,
            Func<int, List<string>> fill = null, params (string, string)[] parameters)
        {
            Subs.Add(new Sub { Name = name, Help = help, Run = run, Fill = fill, Params = parameters ?? Array.Empty<(string, string)>() });
        }

        private static Sub Find(string name) =>
            name == null ? null : Subs.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        private static void Execute(string[] args)
        {
            if (args.Length < 2)
            {
                Help();
                return;
            }
            var sub = Find(args[1]);
            if (sub == null) throw new Exception($"Unknown command '{args[1]}'. Type: casprep help");
            sub.Run(args.Skip(2).ToArray());
        }

        private static void Help()
        {
            Log("CasPrepared commands (casprep <command> [value]):");
            foreach (var s in Subs)
            {
                var ps = s.Params.Length == 0 ? string.Empty : " [" + string.Join("] [", s.Params.Select(p => p.Item1)) + "]";
                Log($"  {s.Name}{ps} - {s.Help}{HostTag(s)}");
            }
        }

        // ---- console hints ----

        /// <summary>
        /// The console shows hints by argument index for the whole command. Before it does,
        /// swap the descriptions and autofill lists for the sub-command being typed.
        /// </summary>
        internal static void PrepareHints(string[] args)
        {
            if (_command == null || args == null || args.Length == 0 || !Name.Equals(args[0], StringComparison.OrdinalIgnoreCase)) return;
            var sub = args.Length >= 3 ? Find(args[1]) : null;
            if (sub == null)
            {
                _command.argDescription = RootArgs;
                _command.argAutofill = _rootFill;
                return;
            }
            _command.argDescription = sub.Description;
            HintFill.Clear();
            HintFill[0] = _subNames;
            int param = args.Length - 3; // parameter being typed
            if (param >= 0 && sub.Fill != null)
            {
                var list = sub.Fill(param);
                if (list != null) HintFill[param + 1] = list;
            }
            _command.argAutofill = HintFill;
        }

        private static readonly List<string> OnOffList = new List<string> { "on", "off" };
#if !STANDALONE
        private static readonly List<string> OffList = new List<string> { "off" };

        private static List<string> PlayerList(bool withAll)
        {
            var list = ScavPlayer.AllPlayers.Where(p => p != null && !string.IsNullOrEmpty(p.playerName))
                .Select(p => p.playerName).Distinct().ToList();
            if (withAll) list.Add("all");
            return list;
        }
#endif

        private enum FillKind { Listed, Priced, Bannable, Banned, Addable, Added }

        private static readonly Dictionary<FillKind, (string stamp, List<string> list)> FillCache =
            new Dictionary<FillKind, (string, List<string>)>();

        private static List<string> Fill(FillKind kind)
        {
            var rules = ItemRulesStore.Local;
            var stamp = rules.Revision + ":" + (Item.GlobalItems?.Count ?? 0) + ":" + LoadoutCatalog.IsLoaded;
            if (FillCache.TryGetValue(kind, out var cached) && cached.stamp == stamp) return cached.list;

            IEnumerable<string> ids;
            switch (kind)
            {
                case FillKind.Listed: ids = ListedIds(); break;
                case FillKind.Priced: ids = rules.Prices.Keys.Concat(new[] { "all" }); break;
                case FillKind.Bannable: ids = ListedIds().Where(id => !rules.IsBanned(id)); break;
                case FillKind.Banned: ids = rules.Banned.Concat(new[] { "all" }); break;
                case FillKind.Addable:
                    var listed = new HashSet<string>(ListedIds());
                    ids = LoadoutCatalog.AllItemIds.Where(id => !listed.Contains(id));
                    break;
                default: ids = rules.Added; break;
            }
            var list = ids.Distinct().OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            FillCache[kind] = (stamp, list);
            return list;
        }

        /// <summary>Items the host can price or hide: the trader list plus added items.</summary>
        private static IEnumerable<string> ListedIds() => LoadoutCatalog.BaseIds.Concat(ItemRulesStore.Local.Added);

        // ---- item list commands ----

        private static void SetPrice(string[] p)
        {
            RequireConfigAccess();
            if (p.Length < 2) throw new Exception("Usage: casprep setprice <item> <price>");
            var id = ResolveItem(p[0], ListedIds(), out var known);
            if (id == null)
                throw new Exception(known != null
                    ? $"'{known}' is not in the list. Add it with: casprep additem {known} <price>"
                    : UnknownItem(p[0]));
            var price = ParsePrice(p[1]);
            ItemRulesStore.Local.SetPrice(id, price);
            ItemRulesStore.Save();
            var trader = LoadoutCatalog.TryGetBase(id, out var b) ? b.VanillaValue : LoadoutCatalog.TraderValueOf(id);
            Log($"{LoadoutCatalog.NameOf(id)} ({id}): base price {Num(price)} (trader {Num(trader)}), x{Num(ModConfig.PriceMultiplier.Value)} multiplier on top.");
        }

        private static void ResetPrice(string[] p)
        {
            RequireConfigAccess();
            if (p.Length < 1) throw new Exception("Usage: casprep resetprice <item | all>");
            var rules = ItemRulesStore.Local;
            if (IsAll(p[0]))
            {
                var n = rules.ResetAllPrices();
                ItemRulesStore.Save();
                Log($"Removed {n} custom price(s).");
                return;
            }
            var id = ResolveItem(p[0], rules.Prices.Keys, out _) ?? throw new Exception($"'{p[0]}' has no custom price.");
            rules.ResetPrice(id);
            ItemRulesStore.Save();
            Log($"{LoadoutCatalog.NameOf(id)} ({id}): back to the trader price.");
            if (rules.IsAdded(id) && LoadoutCatalog.TraderValueOf(id) <= 0)
                Log($"It has no trader price, so it stays out of the list until you set one: casprep setprice {id} <price>");
        }

        private static void Ban(string[] p)
        {
            RequireConfigAccess();
            if (p.Length < 1) throw new Exception("Usage: casprep ban <item>");
            var id = ResolveItem(p[0], ListedIds(), out var known) ?? throw new Exception(known != null
                ? $"'{known}' is not in the list anyway." : UnknownItem(p[0]));
            if (!ItemRulesStore.Local.Ban(id)) throw new Exception($"'{id}' is already hidden.");
            ItemRulesStore.Save();
            Log($"{LoadoutCatalog.NameOf(id)} ({id}) is hidden from the list.");
        }

        private static void Unban(string[] p)
        {
            RequireConfigAccess();
            if (p.Length < 1) throw new Exception("Usage: casprep unban <item | all>");
            var rules = ItemRulesStore.Local;
            if (IsAll(p[0]))
            {
                var n = rules.UnbanAll();
                ItemRulesStore.Save();
                Log($"{n} item(s) back in the list.");
                return;
            }
            var id = ResolveItem(p[0], rules.Banned, out _) ?? throw new Exception($"'{p[0]}' is not hidden.");
            rules.Unban(id);
            ItemRulesStore.Save();
            Log($"{LoadoutCatalog.NameOf(id)} ({id}) is back in the list.");
        }

        private static void AddItem(string[] p)
        {
            RequireConfigAccess();
            if (p.Length < 1) throw new Exception("Usage: casprep additem <item> [price]");
            if (!LoadoutCatalog.IsLoaded) throw new Exception("The item list is not loaded yet.");
            var rules = ItemRulesStore.Local;
            var id = ResolveItem(p[0], LoadoutCatalog.AllItemIds, out _) ?? throw new Exception(UnknownItem(p[0]));
            if (LoadoutCatalog.TryGetBase(id, out _))
                throw new Exception(rules.IsBanned(id)
                    ? $"'{id}' is a trader item that you hid. Use: casprep unban {id}"
                    : $"'{id}' is already in the list (traders sell it).");
            if (rules.IsAdded(id)) throw new Exception($"'{id}' is already added.");
            if (!LoadoutCatalog.IsSpawnable(id)) throw new Exception($"'{id}' cannot be spawned.");

            float? price = p.Length >= 2 ? ParsePrice(p[1]) : (float?)null;
            int trader = LoadoutCatalog.TraderValueOf(id);
            if (price == null && trader <= 0)
                throw new Exception($"'{id}' has no trader price. Give one: casprep additem {id} <price>");

            rules.Add(id);
            if (price != null) rules.SetPrice(id, price.Value);
            ItemRulesStore.Save();
            Log($"{LoadoutCatalog.NameOf(id)} ({id}) added, base price {Num(price ?? trader)}.");
        }

        private static void RemoveItem(string[] p)
        {
            RequireConfigAccess();
            if (p.Length < 1) throw new Exception("Usage: casprep removeitem <item>");
            var id = ResolveItem(p[0], ItemRulesStore.Local.Added, out _) ?? throw new Exception($"'{p[0]}' was not added with casprep additem.");
            ItemRulesStore.Local.Remove(id);
            ItemRulesStore.Save();
            Log($"{LoadoutCatalog.NameOf(id)} ({id}) removed from the list.");
        }

        private static void ListRules()
        {
            var rules = ItemRulesStore.Local;
            if (rules.Prices.Count == 0 && rules.BannedCount == 0 && rules.AddedCount == 0)
            {
                Log("No item rules: the list matches the traders.");
                Log("File: " + ItemRulesStore.FilePath);
                return;
            }
            if (rules.AddedCount > 0)
            {
                Log("Added:");
                foreach (var id in rules.Added.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    Log($"  {LoadoutCatalog.NameOf(id)} ({id})" + (rules.IsBanned(id) ? " - hidden" : string.Empty));
            }
            if (rules.Prices.Count > 0)
            {
                Log("Custom prices (before the multiplier):");
                foreach (var pair in rules.Prices.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                {
                    var trader = LoadoutCatalog.TryGetBase(pair.Key, out var b) ? b.VanillaValue : LoadoutCatalog.TraderValueOf(pair.Key);
                    Log($"  {LoadoutCatalog.NameOf(pair.Key)} ({pair.Key}): {Num(pair.Value)} (trader {Num(trader)})");
                }
            }
            if (rules.BannedCount > 0)
            {
                Log("Hidden:");
                foreach (var id in rules.Banned.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    Log($"  {LoadoutCatalog.NameOf(id)} ({id})");
            }
            Log("File: " + ItemRulesStore.FilePath);
        }

        /// <summary>
        /// Exact id, then a unique case-insensitive match in <paramref name="pool"/>.
        /// <paramref name="known"/> is set when the text names a game item that is not in the pool.
        /// </summary>
        private static string ResolveItem(string text, IEnumerable<string> pool, out string known)
        {
            known = null;
            if (!LoadoutCatalog.IsLoaded) throw new Exception("The item list is not loaded yet.");
            var ids = pool.Distinct().ToList();
            if (ids.Contains(text)) return text;
            var matches = ids.Where(id => id.Equals(text, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 1) return matches[0];
            known = LoadoutCatalog.AllItemIds.FirstOrDefault(id => id.Equals(text, StringComparison.OrdinalIgnoreCase));
            return null;
        }

        private static string UnknownItem(string text) => $"Unknown item '{text}'. Type the start of its id and press Tab.";

        private static bool IsAll(string s) => s.Equals("all", StringComparison.OrdinalIgnoreCase);

        private static float ParsePrice(string s)
        {
            if (!float.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
                !ItemRules.IsValidPrice(value))
                throw new Exception($"\"{s}\" is not a valid price (0 - {ItemRules.MaxPrice:0}).");
            return value;
        }

        private static string Num(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        // ---- status ----

        private static string RulesSummary()
        {
            var r = ItemRulesStore.Local;
            return $"Item rules: {r.Prices.Count} custom price(s), {r.BannedCount} hidden, {r.AddedCount} added (casprep items).";
        }

        private static void Status()
        {
#if STANDALONE
            Log("Loadout menu: " + (ModConfig.Enabled.Value ? "ON" : "OFF"));
            Log($"Budget {ModConfig.Budget.Value}, multiplier x{Num(ModConfig.PriceMultiplier.Value)}, " +
                $"radius {ModConfig.MaxDistance.Value.ToString("0.#", CultureInfo.InvariantCulture)}, crate {ModConfig.CrateLifetimeSeconds.Value}s");
            Log(RulesSummary());
            Log("This run: " + LoadoutClient.DescribeState());
#else
            RequireHost();
            Log("Loadout menu: " + (ModConfig.Enabled.Value ? "ON" : "OFF"));
            Log($"Budget {ModConfig.Budget.Value}, multiplier x{Num(ModConfig.PriceMultiplier.Value)}, " +
                $"window {(ModConfig.WindowSeconds.Value <= 0 ? "off" : ModConfig.WindowSeconds.Value + "s")}, radius {ModConfig.MaxDistance.Value.ToString("0.#", CultureInfo.InvariantCulture)}, crate {ModConfig.CrateLifetimeSeconds.Value}s");
            Log(RulesSummary());
            foreach (var line in LoadoutServer.DescribePlayers()) Log(line);
#endif
        }

#if !STANDALONE
        private static void Cheats()
        {
            Log($"sv_cheats: {OnOff(Multiplayer.rules.sv_cheats)}, AllowClientCheatCommands: {OnOff(Multiplayer.rules.AllowClientCheatCommands)}");
            if (!Net.IsRunning)
            {
                Log("Not in a multiplayer session. casprep giveloadout works once you host or join one.");
                return;
            }
            var blocked = LoadoutServer.CheatBlockReason(fromHost: Net.IsServer);
            Log(blocked == null ? "casprep giveloadout: available." : "casprep giveloadout: blocked - " + blocked);
        }
#endif

        // ---- settings helpers ----

        private static void IntSetting(string[] p, string label, Action<int> set, Func<int> get)
        {
            if (p.Length >= 1)
            {
                RequireConfigAccess();
                if (!int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                    throw new Exception($"\"{p[0]}\" is not a valid integer.");
                set(value); // AcceptableValueRange clamps out-of-range values
            }
            Log($"{label}: {get()}");
        }

        private static void FloatSetting(string[] p, string label, Action<float> set, Func<float> get)
        {
            if (p.Length >= 1)
            {
                RequireConfigAccess();
                if (!float.TryParse(p[0].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    throw new Exception($"\"{p[0]}\" is not a valid number.");
                set(value);
            }
            Log($"{label}: {get().ToString("0.###", CultureInfo.InvariantCulture)}");
        }

        private static bool ParseBool(string s)
        {
            switch (s.Trim().ToLowerInvariant())
            {
                case "1": case "on": case "true": case "yes": case "enable": return true;
                case "0": case "off": case "false": case "no": case "disable": return false;
                default: throw new Exception($"\"{s}\" is not on/off.");
            }
        }

#if !STANDALONE
        private static bool IsOff(string s)
        {
            switch (s.Trim().ToLowerInvariant())
            {
                case "off": case "false": case "no": case "disable": case "none": return true;
                default: return false;
            }
        }

        private static string OnOff(bool v) => v ? "on" : "off";
#endif

#if STANDALONE
        /// <summary>Settings change only outside a run (main menu / character select).</summary>
        public static string ConfigLockReason()
        {
            if (WorldGeneration.world != null)
                return "Settings are locked during a run. Change them in the main menu.";
            return null;
        }
#else
        /// <summary>
        /// Settings can change freely in the main menu. During a running multiplayer campaign
        /// they are locked unless sv_cheats is on (same gate as Together's cheat commands).
        /// </summary>
        public static string ConfigLockReason()
        {
            if (Net.IsRunning && !Net.IsServer) return "Host only.";
            if (Net.IsRunning && WorldGeneration.world != null && !Multiplayer.rules.sv_cheats)
                return "Settings are locked during a running campaign. Change them in the main menu or enable sv_cheats.";
            return null;
        }

        private static void RequireHost()
        {
            if (Net.IsRunning && !Net.IsServer) throw new Exception("Host only.");
        }
#endif

        private static void RequireConfigAccess()
        {
            var reason = ConfigLockReason();
            if (reason != null) throw new Exception(reason);
        }

        private static void Log(string text)
        {
            ConsoleScript.instance?.LogToConsole(text);
        }
    }

    /// <summary>Sub-command aware hints for "casprep" (descriptions and Tab completion).</summary>
    [HarmonyPatch]
    internal static class ConsoleHintPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(ConsoleScript), nameof(ConsoleScript.HandleDescriptionText))]
        private static void BeforeDescription(string[] args) => Safe(args);

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ConsoleScript), nameof(ConsoleScript.TryFinishCommandPart))]
        private static void BeforeComplete(string[] args) => Safe(args);

        private static void Safe(string[] args)
        {
            try
            {
                LoadoutCommands.PrepareHints(args);
            }
            catch
            {
                // hints are cosmetic
            }
        }
    }
}
