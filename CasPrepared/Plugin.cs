using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace CasPrepared
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    [BepInDependency("net.cucorelib", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("CasualtiesMP", BepInDependency.DependencyFlags.HardDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string ModGUID = "com.casprepared.loadout";
        public const string ModName = "CasPrepared";
        public const string ModVersion = "1.1.0";

        internal static new ManualLogSource Logger;
        private readonly Harmony _harmony = new Harmony(ModGUID);

        // Avoid spamming the log every frame if a tick keeps throwing.
        private float _nextErrorLogTime;

        public void Awake()
        {
            Logger = base.Logger;

            ModConfig.Init(Config);
            ItemRulesStore.Load();
            LoadoutCatalog.RulesProvider = () => LoadoutClient.ActiveRules;
            SupplyCrate.Register();
            LoadoutServer.Init();
            LoadoutCommands.Register();
            _harmony.PatchAll();

            Logger.LogInfo($"{ModName} v{ModVersion} loaded.");
        }

        public void Update()
        {
            try
            {
                LoadoutServer.Tick();
                LoadoutClient.Tick();
                LoadoutUI.RangeTick();
            }
            catch (Exception e)
            {
                if (UnityEngine.Time.unscaledTime >= _nextErrorLogTime)
                {
                    _nextErrorLogTime = UnityEngine.Time.unscaledTime + 5f;
                    Logger.LogError("Tick failed: " + e);
                }
            }
        }

        public void OnGUI()
        {
            try
            {
                LoadoutUI.Draw();
            }
            catch (Exception e)
            {
                if (UnityEngine.Time.unscaledTime >= _nextGuiErrorLogTime)
                {
                    _nextGuiErrorLogTime = UnityEngine.Time.unscaledTime + 5f;
                    Logger.LogError("GUI failed: " + e);
                }
            }
        }

        private float _nextGuiErrorLogTime;
    }
}
