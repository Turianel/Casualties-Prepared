using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace CasPrepared
{
    /// <summary>
    /// Single-player build: no Casualties: Together needed. If Together is installed anyway,
    /// this build stays idle during multiplayer sessions (use the multiplayer build there).
    /// </summary>
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    [BepInDependency("net.cucorelib", BepInDependency.DependencyFlags.HardDependency)]
    // Both builds draw the same menu and register the same commands: never run them together.
    [BepInIncompatibility(MultiplayerGUID)]
    public class Plugin : BaseUnityPlugin
    {
        public const string ModGUID = "com.casprepared.standalone";
        public const string MultiplayerGUID = "com.casprepared.loadout";
        public const string ModName = "CasPrepared Standalone";
        public const string ModVersion = "1.0.0";

        internal static new ManualLogSource Logger;
        private readonly Harmony _harmony = new Harmony(ModGUID);

        private float _nextErrorLogTime;
        private float _nextGuiErrorLogTime;

        public void Awake()
        {
            Logger = base.Logger;

            ModConfig.Init(Config);
            SupplyCrate.Register();
            LoadoutClient.Init();
            LoadoutCommands.Register();
            _harmony.PatchAll();

            Logger.LogInfo($"{ModName} v{ModVersion} loaded.");
        }

        public void Update()
        {
            try
            {
                LoadoutClient.Tick();
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
                SoloMenu.Draw();
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
    }
}
