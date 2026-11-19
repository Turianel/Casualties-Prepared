using System;
using System.Globalization;
using CUCoreLib.Registries;
using Together;

namespace CasPrepared
{
    /// <summary>In-game console commands. Setters are host-only; values persist in the BepInEx config.</summary>
    internal static class LoadoutCommands
    {
        public static void Register()
        {
            ConsoleCommandRegistry.Register("casprep_enable", "CasPrepared (host): show or set whether the loadout menu is on for sessions you host.",
                args =>
                {
                    if (args.Length >= 2)
                    {
                        RequireConfigAccess();
                        ModConfig.Enabled.Value = ParseBool(args[1]);
                    }
                    Log("Loadout menu: " + (ModConfig.Enabled.Value ? "ON (achievements are disabled while hosting)" : "OFF"));
                },
                null, ("bool value", "on/off, true/false or 1/0."));

            ConsoleCommandRegistry.Register("casprep_cheats", "CasPrepared: show whether Together cheat rules allow casprep_giveloadout.",
                args =>
                {
                    Log($"sv_cheats: {OnOff(Multiplayer.rules.sv_cheats)}, AllowClientCheatCommands: {OnOff(Multiplayer.rules.AllowClientCheatCommands)}");
                    if (!Net.IsRunning)
                    {
                        Log("Not in a multiplayer session. casprep_giveloadout works once you host or join one.");
                        return;
                    }
                    var blocked = LoadoutServer.CheatBlockReason(fromHost: Net.IsServer);
                    Log(blocked == null ? "casprep_giveloadout: available." : "casprep_giveloadout: blocked - " + blocked);
                });

            ConsoleCommandRegistry.Register("casprep_giveloadout", "CasPrepared (cheat): open the loadout menu again. Needs sv_cheats (and AllowClientCheatCommands for clients).",
                args =>
                {
                    if (!Net.IsRunning) throw new Exception("Only works in a multiplayer session.");
                    // Fast local check; the host validates again with its own rules.
                    var blocked = LoadoutServer.CheatBlockReason(fromHost: Net.IsServer);
                    if (blocked != null) throw new Exception(blocked);
                    var target = args.Length >= 2 ? string.Join(" ", args, 1, args.Length - 1) : null;
                    LoadoutClient.Give(target);
                },
                null, ("target", "Optional player name or SteamID. Omit for yourself."));

            ConsoleCommandRegistry.Register("casprep_budget", "CasPrepared (host): show or set the loadout budget.",
                args => IntSetting(args, "Budget", v => ModConfig.Budget.Value = v, () => ModConfig.Budget.Value),
                null, ("int value", "New budget. Omit to show the current value."));

            ConsoleCommandRegistry.Register("casprep_mult", "CasPrepared (host): show or set the price multiplier.",
                args => FloatSetting(args, "Price multiplier", v => ModConfig.PriceMultiplier.Value = v, () => ModConfig.PriceMultiplier.Value),
                null, ("float value", "Multiplier for trader prices (1.0 = trader price)."));

            ConsoleCommandRegistry.Register("casprep_window", "CasPrepared (host): show or set how long (seconds) the menu stays available after spawn. 0 or off disables the timer.",
                args =>
                {
                    if (args.Length >= 2 && IsOff(args[1])) args = new[] { args[0], "0" };
                    IntSetting(args, "Window seconds", v => ModConfig.WindowSeconds.Value = v, () => ModConfig.WindowSeconds.Value);
                    if (ModConfig.WindowSeconds.Value <= 0) Log("Time limit is off: only the distance limit applies.");
                },
                null, ("int seconds", "Window length in seconds. 0 / off = no time limit."));

            ConsoleCommandRegistry.Register("casprep_radius", "CasPrepared (host): show or set how far (tiles) a player may move from spawn before the menu closes.",
                args => FloatSetting(args, "Max distance", v => ModConfig.MaxDistance.Value = v, () => ModConfig.MaxDistance.Value),
                null, ("float distance", "Distance in world units."));

            ConsoleCommandRegistry.Register("casprep_cratetime", "CasPrepared (host): show or set the supply crate lifetime in seconds.",
                args => IntSetting(args, "Crate lifetime", v => ModConfig.CrateLifetimeSeconds.Value = v, () => ModConfig.CrateLifetimeSeconds.Value),
                null, ("int seconds", "Lifetime in seconds."));

            ConsoleCommandRegistry.Register("casprep_status", "CasPrepared (host): list players and their loadout state.",
                args =>
                {
                    RequireHost();
                    Log("Loadout menu: " + (ModConfig.Enabled.Value ? "ON" : "OFF"));
                    Log($"Budget {ModConfig.Budget.Value}, multiplier x{ModConfig.PriceMultiplier.Value.ToString("0.##", CultureInfo.InvariantCulture)}, " +
                        $"window {(ModConfig.WindowSeconds.Value <= 0 ? "off" : ModConfig.WindowSeconds.Value + "s")}, radius {ModConfig.MaxDistance.Value.ToString("0.#", CultureInfo.InvariantCulture)}, crate {ModConfig.CrateLifetimeSeconds.Value}s");
                    foreach (var line in LoadoutServer.DescribePlayers()) Log(line);
                });

            ConsoleCommandRegistry.Register("casprep_reset", "CasPrepared (host): let a player pick a loadout again (opens a new window on their next spawn check).",
                args =>
                {
                    RequireConfigAccess();
                    if (args.Length < 2) throw new Exception("Usage: casprep_reset <player name | steamid | all>");
                    var target = string.Join(" ", args, 1, args.Length - 1);
                    Log($"Reset {LoadoutServer.Reset(target)} record(s) for '{target}'.");
                },
                null, ("target", "Player name, SteamID or 'all'."));

            ConsoleCommandRegistry.Register("casprep_menu", "CasPrepared: open/close the loadout menu.",
                args => LoadoutClient.ToggleMenu());
        }

        private static void IntSetting(string[] args, string label, Action<int> set, Func<int> get)
        {
            if (args.Length >= 2)
            {
                RequireConfigAccess();
                if (!int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                    throw new Exception($"\"{args[1]}\" is not a valid integer.");
                set(value); // AcceptableValueRange clamps out-of-range values
            }
            Log($"{label}: {get()}");
        }

        private static void FloatSetting(string[] args, string label, Action<float> set, Func<float> get)
        {
            if (args.Length >= 2)
            {
                RequireConfigAccess();
                if (!float.TryParse(args[1].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    throw new Exception($"\"{args[1]}\" is not a valid number.");
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

        private static bool IsOff(string s)
        {
            switch (s.Trim().ToLowerInvariant())
            {
                case "off": case "false": case "no": case "disable": case "none": return true;
                default: return false;
            }
        }

        private static string OnOff(bool v) => v ? "on" : "off";

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

        private static void RequireConfigAccess()
        {
            var reason = ConfigLockReason();
            if (reason != null) throw new Exception(reason);
        }

        private static void RequireHost()
        {
            if (Net.IsRunning && !Net.IsServer) throw new Exception("Host only.");
        }

        private static void Log(string text)
        {
            ConsoleScript.instance?.LogToConsole(text);
        }
    }
}
