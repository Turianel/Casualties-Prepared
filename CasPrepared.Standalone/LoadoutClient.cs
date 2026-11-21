using System.Collections.Generic;
using CUCoreLib.Networking;
using CUCoreLib.Registries;
using CUCoreLib.Saving;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CasPrepared
{
    /// <summary>
    /// Single-player loadout logic: the local game is its own "host".
    /// One loadout per run (saved with the run). The menu opens once the player has spawned
    /// and stays available until they move MaxDistance away from the spawn point or die (no time limit).
    /// Exposes the same members the shared UI and commands use from the multiplayer client.
    /// </summary>
    internal static class LoadoutClient
    {
        private enum RunState { None, Used, Expired }

        private const float SettleSeconds = 3f;
        private const float TeleportJump = 5f; // per-frame move bigger than this = teleport, not walking
        private const string StateUsed = "used";
        private const string StateExpired = "expired";

        // Persisted with the run.
        private static RunState _record;
        // The menu is currently available.
        private static bool _pending;
        private static Vector2 _origin;

        private static float _settledSince = -1f;
        private static Vector2? _lastBodyPos;
        private static bool _mpNoticeShown;

        // ---- members used by the shared UI ----
        public static bool IsAvailable => _pending;
        public static bool BuyInFlight => false;
        public static int Budget => ModConfig.Budget.Value;
        public static float Multiplier => ModConfig.PriceMultiplier.Value;
        public static float MaxDistance => ModConfig.MaxDistance.Value;
        public static float Distance { get; private set; }

        public static void Init()
        {
            SaveRegistry.RegisterGlobalProvider("casprepared.solo", new RunSaveProvider());
        }

        private static bool WorldReady()
        {
            var world = WorldGeneration.world;
            return world != null && world.worldExists;
        }

        /// <summary>Tutorial runs are left alone.</summary>
        private static bool IsTutorial =>
            WorldGeneration.world != null && WorldGeneration.world.biomeOverride == WorldGeneration.OverrideSceneType.Tutorial;

        public static void Tick()
        {
            if (!WorldReady() || IsTutorial) return;

            // Together installed and a multiplayer session running: that is the multiplayer build's job.
            if (MultiplayerBridge.IsRunning)
            {
                if (_pending) LoadoutUI.Close();
                if (!_mpNoticeShown)
                {
                    _mpNoticeShown = true;
                    Plugin.Logger.LogInfo("Multiplayer session detected: the standalone build stays idle. Use the multiplayer build of CasPrepared.");
                }
                return;
            }
            if (!ModConfig.Enabled.Value) return;

            var body = PlayerCamera.main != null ? PlayerCamera.main.body : null;
            if (body == null)
            {
                _settledSince = -1f;
                return;
            }

            var now = Time.realtimeSinceStartup;
            UpdateSettled(body, now);

            if (_pending)
            {
                var reason = Evaluate(body);
                if (reason != null) Expire(reason);
            }
            else if (_record == RunState.None && IsSettled(now))
            {
                OpenWindow(body);
            }

            if (Input.GetKeyDown(ModConfig.MenuKey.Value) && !LoadoutUI.IsSearchFocused) ToggleMenu();
        }

        private static void OpenWindow(Body body)
        {
            if (!body.alive || WorldGeneration.world.generatingWorld) return;
            _pending = true;
            _origin = body.transform.position;
            Distance = 0f;
            Plugin.Logger.LogInfo($"Loadout window opened at {_origin}.");
            LoadoutUI.Open();
            Alert($"Loadout menu available ({ModConfig.MenuKey.Value}). It closes if you move {ModConfig.MaxDistance.Value:0} tiles away.");
        }

        /// <summary>Returns null while the window is valid, otherwise the reason it closes.</summary>
        private static string Evaluate(Body body)
        {
            if (!body.alive) return "died";
            Distance = Vector2.Distance(body.transform.position, _origin);
            if (Distance > ModConfig.MaxDistance.Value) return "moved too far from spawn";
            return null;
        }

        private static void Expire(string reason)
        {
            _pending = false;
            _record = RunState.Expired;
            LoadoutUI.Close();
            Plugin.Logger.LogInfo("Loadout window closed: " + reason + ".");
            Alert("Loadout window closed: " + reason + ".");
        }

        public static void ToggleMenu()
        {
            if (LoadoutUI.IsOpen)
            {
                LoadoutUI.Close();
                return;
            }
            if (_pending)
            {
                LoadoutUI.Open();
                return;
            }
            Alert(!ModConfig.Enabled.Value ? "The loadout menu is turned off."
                : MultiplayerBridge.IsRunning ? "The standalone build does not work in multiplayer."
                : _record == RunState.Used ? "Loadout already taken this run."
                : _record == RunState.Expired ? "Loadout window has closed."
                : "Loadout is not available yet.");
        }

        public static void Buy(Dictionary<string, int> selection)
        {
            var body = PlayerCamera.main != null ? PlayerCamera.main.body : null;
            if (!_pending)
            {
                LoadoutUI.SetMessage("Loadout is not available.");
                return;
            }
            if (body == null || !body.alive)
            {
                LoadoutUI.SetMessage("Your character is not in the world.");
                return;
            }
            var reason = Evaluate(body);
            if (reason != null)
            {
                Expire(reason);
                return;
            }

            var error = LoadoutOrder.Build(selection, out var order, out var cost, out var total);
            if (error != null)
            {
                LoadoutUI.SetMessage(error);
                return;
            }

            try
            {
                LoadoutOrder.Spawn(body, order);
            }
            catch (System.Exception e)
            {
                Plugin.Logger.LogError("Loadout spawn failed: " + e);
                LoadoutUI.SetMessage("Error while spawning the loadout.");
                return;
            }

            _pending = false;
            _record = RunState.Used;
            Plugin.Logger.LogInfo($"Loadout received ({total} items, cost {cost}/{ModConfig.Budget.Value}).");
            LoadoutUI.Close();
            LoadoutUI.ClearSelection();
            Alert("Supply crate delivered at your feet.");
        }

        public static string DescribeState()
        {
            if (!WorldReady()) return "not in a run";
            if (IsTutorial) return "tutorial (mod inactive)";
            if (!ModConfig.Enabled.Value) return "loadout menu is off";
            if (_pending) return $"available ({Distance:0.0}/{ModConfig.MaxDistance.Value:0} tiles from spawn)";
            switch (_record)
            {
                case RunState.Used: return "loadout taken";
                case RunState.Expired: return "window closed";
                default: return "waiting for spawn";
            }
        }

        // ---- spawn detection (same rules as the multiplayer client) ----

        private static bool LocalLoadingDone()
        {
            var world = WorldGeneration.world;
            if (world == null || !world.worldExists || world.generatingWorld) return false;
            if (world.loadingObject != null && world.loadingObject.activeSelf) return false;
            return true;
        }

        private static void UpdateSettled(Body body, float now)
        {
            Vector2 pos = body.transform.position;
            bool jumped = _lastBodyPos.HasValue && Vector2.Distance(pos, _lastBodyPos.Value) > TeleportJump;
            _lastBodyPos = pos;

            if (!LocalLoadingDone() || !body.alive || jumped) _settledSince = -1f;
            else if (_settledSince < 0f) _settledSince = now;
        }

        private static bool IsSettled(float now) => _settledSince >= 0f && now - _settledSince >= SettleSeconds;

        private static void Alert(string text)
        {
            if (PlayerCamera.main != null) PlayerCamera.main.DoAlert(text);
            if (ConsoleScript.instance != null) ConsoleScript.instance.LogToConsole("[CasPrepared] " + text);
        }

        /// <summary>New run or loaded save (WorldGeneration.Awake). A loaded save restores its record right after.</summary>
        public static void ResetLocal()
        {
            _record = RunState.None;
            _pending = false;
            Distance = 0f;
            _settledSince = -1f;
            _lastBodyPos = null;
            _mpNoticeShown = false;
            LoadoutUI.Close();
            LoadoutUI.ClearSelection();
        }

        // ---- persistence ----

        private sealed class RunSaveProvider : ICustomSaveProvider
        {
            public int GetVersion() => 1;

            public JToken Capture()
            {
                // A window still open at save time counts as spent: reloading must not grant a fresh one.
                var state = _record == RunState.Used ? StateUsed
                    : _record == RunState.Expired || _pending ? StateExpired
                    : null;
                return state == null ? null : new JObject { ["state"] = state };
            }

            public void Restore(JToken payload, int version, SaveRestoreContext context)
            {
                var state = payload?.Value<string>("state");
                if (state == StateUsed) _record = RunState.Used;
                else if (state == StateExpired && _record != RunState.Used) _record = RunState.Expired;
                else return;
                _pending = false;
                LoadoutUI.Close();
                Plugin.Logger.LogInfo("Restored loadout state for this run: " + state + ".");
            }
        }
    }
}
