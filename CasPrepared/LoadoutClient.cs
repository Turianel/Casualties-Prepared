using System;
using System.Collections.Generic;
using CUCoreLib.Networking;
using Newtonsoft.Json.Linq;
using Together;
using UnityEngine;
using P = CasPrepared.LoadoutProtocol;

namespace CasPrepared
{
    /// <summary>
    /// Local player side (clients and the host's own player). Polls the host for status,
    /// opens the menu automatically when the window opens, sends purchase requests.
    /// </summary>
    internal static class LoadoutClient
    {
        private const float PollInterval = 2f;
        private const float IdlePollInterval = 10f; // after use / while disabled: still notice GiveLoadout or re-enable
        private const float RequestTimeout = 10f;
        private const int MissedRepliesForNoMod = 3; // unanswered status requests before we assume the host lacks the mod
        private const float NoModPollInterval = 60f;  // keep checking slowly in case the host restarts with the mod
        private const float SettleSeconds = 3f;
        private const float TeleportJump = 5f; // per-frame move bigger than this = teleport, not walking

        private static float _settledSince = -1f;
        private static Vector2? _lastBodyPos;

        public static JObject Status { get; private set; }
        public static float StatusReceivedAt { get; private set; }
        public static bool BuyInFlight { get; private set; }

        private static bool _statusInFlight;
        private static float _statusSentAt;
        private static float _buySentAt;
        private static float _nextPoll;
        private static bool _wasRunning;
        private static int _missedReplies;
        /// <summary>The host never answered: it most likely does not have CasPrepared.</summary>
        public static bool HostLacksMod { get; private set; }

        public static string State => Status?.Value<string>(P.KeyState) ?? P.StateWaiting;
        public static bool IsAvailable => State == P.StateAvailable;
        public static bool IsFinished => State == P.StateUsed || State == P.StateExpired;
        public static bool HasStatus => Status != null;
        /// <summary>Known to be off on the host (null status = unknown yet).</summary>
        public static bool IsDisabled => State == P.StateDisabled;
        public static int Budget => Status?.Value<int?>(P.KeyBudget) ?? 0;
        public static float Multiplier => Status?.Value<float?>(P.KeyMultiplier) ?? 1f;
        public static float MaxDistance => Status?.Value<float?>(P.KeyMaxDistance) ?? 0f;
        public static float Distance => Status?.Value<float?>(P.KeyDistance) ?? 0f;

        /// <summary>False when the host set the window timer to 0 (off).</summary>
        public static bool HasTimeLimit => (Status?.Value<float?>(P.KeyRemaining) ?? 0f) >= 0f;

        public static float RemainingSeconds
        {
            get
            {
                var remaining = Status?.Value<float?>(P.KeyRemaining) ?? 0f;
                return Mathf.Max(0f, remaining - (Time.realtimeSinceStartup - StatusReceivedAt));
            }
        }

        public static void Tick()
        {
            if (!Net.IsRunning)
            {
                if (_wasRunning) ResetLocal();
                _wasRunning = false;
                return;
            }
            _wasRunning = true;

            if (!LoadoutServer.WorldReady()) return;

            // Every machine with the mod locks itself too (in case the host broadcast was missed),
            // but only once the host confirmed the loadout menu is on for this session.
            if (HasStatus && !IsDisabled && !SteamManager.disableAchievements) AchievementBreakSync.ApplyLocal(showAlert: true);

            var local = ScavPlayer.LocalPlayer;
            if (local == null || local.body == null) // dedicated server or not spawned yet
            {
                _settledSince = -1f;
                return;
            }

            var now = Time.realtimeSinceStartup;
            UpdateSettled(local.body, now);
            if (_statusInFlight && now - _statusSentAt > RequestTimeout) OnStatusTimeout(now);
            if (BuyInFlight && now - _buySentAt > RequestTimeout)
            {
                BuyInFlight = false;
                LoadoutUI.SetMessage("Host did not answer. Try again.");
            }

            // Don't talk to the host until the loading screen is gone and the body stopped teleporting:
            // during loading the body sits at a temporary spot (map centre) before being moved to the pod.
            if (IsSettled(now) && !_statusInFlight && now >= _nextPoll)
            {
                _nextPoll = now + (IsFinished || IsDisabled ? IdlePollInterval : PollInterval);
                RequestStatus();
            }

            if (Input.GetKeyDown(ModConfig.MenuKey.Value) && !LoadoutUI.IsSearchFocused) ToggleMenu();
        }

        public static void ToggleMenu()
        {
            if (LoadoutUI.IsOpen)
            {
                LoadoutUI.Close();
                return;
            }
            if (IsAvailable)
            {
                LoadoutUI.Open();
                return;
            }
            Alert(HostLacksMod && !HasStatus ? "The host does not have CasPrepared installed."
                : IsDisabled ? "The host has turned the loadout menu off."
                : State == P.StateUsed ? "Loadout already taken this campaign."
                : State == P.StateExpired ? "Loadout window has closed."
                : "Loadout is not available yet.");
        }

        public static void RequestStatus()
        {
            _statusInFlight = true;
            _statusSentAt = Time.realtimeSinceStartup;
            if (!Send(P.StatusChannel, new JObject { [P.KeyReady] = true }, OnStatus)) _statusInFlight = false;
        }

        private static bool LocalLoadingDone()
        {
            var world = WorldGeneration.world;
            if (world == null || !world.worldExists || world.generatingWorld) return false;
            if (world.loadingObject != null && world.loadingObject.activeSelf) return false;
            return true;
        }

        /// <summary>Tracks how long the local body has stayed put (no teleport jumps) after loading finished.</summary>
        private static void UpdateSettled(Body body, float now)
        {
            Vector2 pos = body.transform.position;
            bool jumped = _lastBodyPos.HasValue && Vector2.Distance(pos, _lastBodyPos.Value) > TeleportJump;
            _lastBodyPos = pos;

            if (!LocalLoadingDone() || !body.alive || jumped) _settledSince = -1f;
            else if (_settledSince < 0f) _settledSince = now;
        }

        private static bool IsSettled(float now) => _settledSince >= 0f && now - _settledSince >= SettleSeconds;

        public static void Buy(Dictionary<string, int> selection)
        {
            if (BuyInFlight) return;
            var items = new JObject();
            foreach (var pair in selection)
                if (pair.Value > 0) items[pair.Key] = pair.Value;

            BuyInFlight = true;
            _buySentAt = Time.realtimeSinceStartup;
            if (!Send(P.BuyChannel, new JObject { [P.KeyItems] = items }, OnBuy))
            {
                BuyInFlight = false;
                LoadoutUI.SetMessage("Could not reach the host.");
            }
        }

        /// <summary>Host's own player is handled locally; clients go through Cas:Together via CUCoreLib.</summary>
        private static bool Send(string channel, JObject payload, Action<JToken> onResponse)
        {
            if (Net.IsServer)
            {
                var local = ScavPlayer.LocalPlayer;
                if (local == null) return false;
                onResponse(LoadoutServer.HandleLocal(channel, local.playerId.id, payload));
                return true;
            }
            return MultiplayerApi.RequestServer(channel, payload, onResponse);
        }

        private static void OnStatusTimeout(float now)
        {
            _statusInFlight = false;
            if (HasStatus) return; // the host answered before: just a lost packet
            _missedReplies++;
            if (_missedReplies >= MissedRepliesForNoMod && !HostLacksMod)
            {
                HostLacksMod = true;
                Alert("The host does not have CasPrepared installed. Loadout menu is unavailable.");
            }
            if (HostLacksMod) _nextPoll = now + NoModPollInterval;
        }

        private static void OnStatus(JToken response)
        {
            _statusInFlight = false;
            _missedReplies = 0;
            HostLacksMod = false;
            if (response is JObject status) ApplyStatus(status);
        }

        private static void OnBuy(JToken response)
        {
            BuyInFlight = false;
            if (!(response is JObject result)) return;
            if (result[P.KeyStatus] is JObject status) ApplyStatus(status);

            if (result.Value<bool?>(P.KeyOk) == true)
            {
                LoadoutUI.Close();
                LoadoutUI.ClearSelection();
                Alert("Supply crate delivered at your feet.");
            }
            else
            {
                LoadoutUI.SetMessage(result.Value<string>(P.KeyError) ?? "Purchase failed.");
            }
        }

        private static void ApplyStatus(JObject status)
        {
            var wasAvailable = IsAvailable;
            Status = status;
            StatusReceivedAt = Time.realtimeSinceStartup;

            if (IsAvailable && !wasAvailable)
            {
                // first window after spawn, or a new one granted by GiveLoadout
                LoadoutUI.Open();
                Alert($"Loadout menu available ({ModConfig.MenuKey.Value}).");
            }
            else if (wasAvailable && !IsAvailable && !BuyInFlight)
            {
                LoadoutUI.Close();
                var reason = status.Value<string>(P.KeyReason);
                if (State == P.StateExpired)
                    Alert("Loadout window closed" + (string.IsNullOrEmpty(reason) ? "." : ": " + reason + "."));
                else if (IsDisabled)
                    Alert("The host has turned the loadout menu off.");
            }
        }

        /// <summary>GiveLoadout: ask the host for a fresh window (for us, or for the named player).</summary>
        public static void Give(string target)
        {
            var payload = new JObject();
            if (!string.IsNullOrWhiteSpace(target)) payload[P.KeyTarget] = target.Trim();
            if (!Send(P.GiveChannel, payload, OnGive)) Log("Could not reach the host.");
        }

        private static void OnGive(JToken response)
        {
            if (!(response is JObject result)) return;
            if (result.Value<bool?>(P.KeyOk) == true)
            {
                Log(result.Value<string>(P.KeyMessage) ?? "Loadout menu granted.");
                // allow the next poll right away so the menu opens without waiting
                _nextPoll = 0f;
            }
            else
            {
                Log("GiveLoadout failed: " + (result.Value<string>(P.KeyError) ?? "unknown error"));
            }
            if (result[P.KeyStatus] is JObject status) ApplyStatus(status);
        }

        private static void Log(string text)
        {
            if (ConsoleScript.instance != null) ConsoleScript.instance.LogToConsole("[CasPrepared] " + text);
        }

        private static void Alert(string text)
        {
            if (PlayerCamera.main != null) PlayerCamera.main.DoAlert(text);
            if (ConsoleScript.instance != null) ConsoleScript.instance.LogToConsole("[CasPrepared] " + text);
        }

        public static void ResetLocal()
        {
            Status = null;
            _missedReplies = 0;
            HostLacksMod = false;
            _settledSince = -1f;
            _lastBodyPos = null;
            _statusInFlight = false;
            BuyInFlight = false;
            _nextPoll = 0f;
            LoadoutUI.Close();
            LoadoutUI.ClearSelection();
        }
    }
}
