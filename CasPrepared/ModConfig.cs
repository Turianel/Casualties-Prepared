using BepInEx.Configuration;
using UnityEngine;

namespace CasPrepared
{
    /// <summary>
    /// Host-side tuning values. Only the host's values matter: clients receive them with every status reply.
    /// Changed through the in-game console (see LoadoutCommands) and persisted in the BepInEx config file.
    /// </summary>
    internal static class ModConfig
    {
        public static ConfigEntry<bool> Enabled;
        public static ConfigEntry<int> Budget;
        public static ConfigEntry<float> PriceMultiplier;
        public static ConfigEntry<int> WindowSeconds;
        public static ConfigEntry<float> MaxDistance;
        public static ConfigEntry<int> CrateLifetimeSeconds;
        public static ConfigEntry<KeyCode> MenuKey;

        public static void Init(ConfigFile cfg)
        {
            Enabled = cfg.Bind("Host", "Enabled", true,
                "Loadout menu for multiplayer sessions you host. When on, achievements are disabled for the session. " +
                "Also toggled from Together's Server settings or with casprep_enable.");

            Budget = cfg.Bind("Host", "Budget", 100,
                new ConfigDescription("Loadout budget per player (in trader value units).",
                    new AcceptableValueRange<int>(0, 1000000)));

            PriceMultiplier = cfg.Bind("Host", "PriceMultiplier", 1.0f,
                new ConfigDescription("Multiplier applied to base trader prices.",
                    new AcceptableValueRange<float>(0f, 100f)));

            WindowSeconds = cfg.Bind("Host", "WindowSeconds", 300,
                new ConfigDescription("How long after spawning the loadout menu stays available. 0 = no time limit.",
                    new AcceptableValueRange<int>(0, 86400)));

            MaxDistance = cfg.Bind("Host", "MaxDistance", 100f,
                new ConfigDescription("How far (tiles) a player may move from the spawn point before the menu closes.",
                    new AcceptableValueRange<float>(1f, 10000f)));

            CrateLifetimeSeconds = cfg.Bind("Host", "CrateLifetimeSeconds", 300,
                new ConfigDescription("Supply crate lifetime. When it breaks, its contents drop on the floor.",
                    new AcceptableValueRange<int>(10, 86400)));

            MenuKey = cfg.Bind("Client", "MenuKey", KeyCode.F6, "Key that toggles the loadout menu.");
        }
    }
}
