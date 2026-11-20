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
#if !STANDALONE
        public static ConfigEntry<int> WindowSeconds; // standalone: no time limit, only the distance limit
#endif
        public static ConfigEntry<float> MaxDistance;
        public static ConfigEntry<int> CrateLifetimeSeconds;
        public static ConfigEntry<KeyCode> MenuKey;

#if STANDALONE
        private const string Section = "Loadout";
        private const string KeySection = "Loadout";
#else
        private const string Section = "Host";
        private const string KeySection = "Client";
#endif

        public static void Init(ConfigFile cfg)
        {
#if STANDALONE
            Enabled = cfg.Bind(Section, "Enabled", true,
                "Loadout menu after spawning in a new run. When on, achievements are disabled for the run. " +
                "Also toggled on the character select screen or with casprep_enable (main menu only).");
#else
            Enabled = cfg.Bind(Section, "Enabled", true,
                "Loadout menu for multiplayer sessions you host. When on, achievements are disabled for the session. " +
                "Also toggled from Together's Server settings or with casprep_enable.");
#endif

            Budget = cfg.Bind(Section, "Budget", 100,
                new ConfigDescription("Loadout budget per player (in trader value units).",
                    new AcceptableValueRange<int>(0, 1000000)));

            PriceMultiplier = cfg.Bind(Section, "PriceMultiplier", 1.0f,
                new ConfigDescription("Multiplier applied to base trader prices.",
                    new AcceptableValueRange<float>(0f, 100f)));

#if !STANDALONE
            WindowSeconds = cfg.Bind(Section, "WindowSeconds", 300,
                new ConfigDescription("How long after spawning the loadout menu stays available. 0 = no time limit.",
                    new AcceptableValueRange<int>(0, 86400)));
#endif

            MaxDistance = cfg.Bind(Section, "MaxDistance", 100f,
                new ConfigDescription("How far (tiles) a player may move from the spawn point before the menu closes.",
                    new AcceptableValueRange<float>(1f, 10000f)));

            CrateLifetimeSeconds = cfg.Bind(Section, "CrateLifetimeSeconds", 300,
                new ConfigDescription("Supply crate lifetime. When it breaks, its contents drop on the floor.",
                    new AcceptableValueRange<int>(10, 86400)));

            MenuKey = cfg.Bind(KeySection, "MenuKey", KeyCode.F6, "Key that toggles the loadout menu.");
        }
    }
}
