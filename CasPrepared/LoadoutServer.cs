using System;
using System.Collections.Generic;
using System.Linq;
using CUCoreLib.Networking;
using CUCoreLib.Registries;
using CUCoreLib.Saving;
using Newtonsoft.Json.Linq;
using Together;
using UnityEngine;
using P = CasPrepared.LoadoutProtocol;

namespace CasPrepared
{
    /// <summary>
    /// Host-authoritative loadout logic. Runs only where Net.IsServer.
    /// Tracks each player (by SteamID, falling back to name) from the moment their body first appears,
    /// enforces time window + distance limit, validates purchases and spawns the supply crate.
    /// </summary>
    internal static class LoadoutServer
    {
        private enum RecordState { Used, Expired }

        private sealed class Pending
        {
            public ushort ClientId;
            public Vector2 Origin;
            public float StartTime;
        }

        // Persisted per campaign (save file): players who already used / lost their chance.
        private static readonly Dictionary<string, RecordState> Records = new Dictionary<string, RecordState>();
        // Session only: players whose menu window is currently open.
        private static readonly Dictionary<string, Pending> PendingByKey = new Dictionary<string, Pending>();

        private static float _nextTick;

        public static void Init()
        {
            SaveRegistry.RegisterGlobalProvider("casprepared.players", new PlayersSaveProvider());

            // Sender-aware overload (internal in CUCoreLib, reachable through the publicizer):
            // the host must know who asks, never trust an id written by the client.
            MultiplayerBridge.RegisterServerHandler(P.StatusChannel,
                (uint sender, JToken payload) => HandleStatus(sender, payload));
            MultiplayerBridge.RegisterServerHandler(P.BuyChannel,
                (uint sender, JToken payload) => HandleBuy(sender, payload));
            MultiplayerBridge.RegisterServerHandler(P.GiveChannel,
                (uint sender, JToken payload) => HandleGive(sender, payload, fromHost: false));
        }

        public static bool Enabled => ModConfig.Enabled.Value;

        /// <summary>Local entry point for the host's own player (no network round-trip).</summary>
        public static JToken HandleLocal(string channel, uint sender, JToken payload)
        {
            switch (channel)
            {
                case P.BuyChannel: return HandleBuy(sender, payload);
                case P.GiveChannel: return HandleGive(sender, payload, fromHost: true);
                default: return HandleStatus(sender, payload);
            }
        }

        public static bool WorldReady()
        {
            return WorldGeneration.world != null && WorldGeneration.world.worldExists;
        }

        public static void Tick()
        {
            // Records belong to the campaign (world/save), not to the server session - kept across sessions.
            if (!Net.IsRunning || !Net.IsServer || !WorldReady() || !Enabled) return;
            if (Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + 0.5f;

            TrackPlayers();
        }

        private static void TrackPlayers()
        {
            var present = new HashSet<string>();
            foreach (var plr in ScavPlayer.AllPlayers.ToList())
            {
                if (plr == null) continue;
                var key = KeyOf(plr);
                present.Add(key);
                if (Records.ContainsKey(key)) continue;
                // Windows are opened by the player's own "ready" status request (see TryOpenWindow),
                // because during the loading screen the body sits at a temporary position.
                if (!PendingByKey.TryGetValue(key, out var pending)) continue;

                pending.ClientId = plr.playerId.id;
                var reason = Evaluate(pending, plr, out _, out _);
                if (reason != null) Expire(key, plr.playerName, reason);
            }

            // Leaving while the window is open burns the chance (prevents rejoin to reset it).
            foreach (var key in PendingByKey.Keys.Where(k => !present.Contains(k)).ToList())
                Expire(key, key, "left the game");
        }

        /// <summary>Returns null when the window is still valid, otherwise a human-readable reason.</summary>
        private static string Evaluate(Pending pending, ScavPlayer plr, out float remaining, out float distance)
        {
            distance = 0f;
            if (ModConfig.WindowSeconds.Value <= 0)
            {
                remaining = -1f; // no time limit
            }
            else
            {
                remaining = ModConfig.WindowSeconds.Value - (Time.realtimeSinceStartup - pending.StartTime);
                if (remaining <= 0f) return "time ran out";
            }

            var body = plr.body;
            // body can briefly be null during layer transitions - don't punish that
            if (body == null) return null;
            if (!body.alive) return "died";

            distance = Vector2.Distance(body.transform.position, pending.Origin);
            if (distance > ModConfig.MaxDistance.Value) return "moved too far from spawn";
            return null;
        }

        private static void Expire(string key, string name, string reason)
        {
            PendingByKey.Remove(key);
            Records[key] = RecordState.Expired;
            Plugin.Logger.LogInfo($"Loadout window closed for {name}: {reason}.");
        }

        private static string KeyOf(ScavPlayer plr)
        {
            return plr.SteamId != 0 ? "steam:" + plr.SteamId : "name:" + plr.playerName;
        }

        private static ScavPlayer FindPlayer(uint clientId)
        {
            ScavPlayer.ClientIdToPlayerDict.TryGetValue(new knetid((ushort)clientId), out var plr);
            return plr;
        }

        private static JObject BuildStatus(ScavPlayer plr)
        {
            var status = new JObject
            {
                [P.KeyBudget] = ModConfig.Budget.Value,
                [P.KeyMultiplier] = ModConfig.PriceMultiplier.Value,
                [P.KeyMaxDistance] = ModConfig.MaxDistance.Value,
                [P.KeyState] = P.StateWaiting
            };
            if (!Enabled)
            {
                status[P.KeyState] = P.StateDisabled;
                return status;
            }
            if (plr == null) return status;

            var key = KeyOf(plr);
            if (Records.TryGetValue(key, out var record))
            {
                status[P.KeyState] = record == RecordState.Used ? P.StateUsed : P.StateExpired;
                return status;
            }

            if (PendingByKey.TryGetValue(key, out var pending))
            {
                var reason = Evaluate(pending, plr, out var remaining, out var distance);
                if (reason != null)
                {
                    Expire(key, plr.playerName, reason);
                    status[P.KeyState] = P.StateExpired;
                    status[P.KeyReason] = reason;
                    return status;
                }
                status[P.KeyState] = P.StateAvailable;
                status[P.KeyRemaining] = remaining;
                status[P.KeyDistance] = distance;
            }
            return status;
        }

        private static JToken HandleStatus(uint sender, JToken payload)
        {
            if (!Net.IsServer) return null;
            var plr = FindPlayer(sender);
            if (payload?.Value<bool?>(P.KeyReady) == true) TryOpenWindow(plr);
            return BuildStatus(plr);
        }

        /// <summary>
        /// Opens the window when the player reports their loading screen is gone and their body settled.
        /// The spawn point is taken from the host's copy of the body at that moment.
        /// </summary>
        private static void TryOpenWindow(ScavPlayer plr)
        {
            if (!Enabled || plr == null || plr.body == null || !plr.body.alive) return;
            if (!WorldReady() || WorldGeneration.world.generatingWorld) return;
            var key = KeyOf(plr);
            if (Records.ContainsKey(key) || PendingByKey.ContainsKey(key)) return;

            PendingByKey[key] = new Pending
            {
                ClientId = plr.playerId.id,
                Origin = plr.body.transform.position,
                StartTime = Time.realtimeSinceStartup
            };
            Plugin.Logger.LogInfo($"Loadout window opened for {plr.playerName} ({key}) at {plr.body.transform.position}.");
        }

        /// <summary>A new WorldGeneration means a new run or a loaded save: start a fresh campaign.
        /// For a loaded save the save provider restores the records right after.</summary>
        public static void OnNewWorld()
        {
            Records.Clear();
            PendingByKey.Clear();
        }

        private static JToken HandleBuy(uint sender, JToken payload)
        {
            if (!Net.IsServer) return null;
            var plr = FindPlayer(sender);
            try
            {
                var error = TryBuy(plr, payload);
                return new JObject
                {
                    [P.KeyOk] = error == null,
                    [P.KeyError] = error,
                    [P.KeyStatus] = BuildStatus(plr)
                };
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError("Loadout purchase failed: " + e);
                return new JObject
                {
                    [P.KeyOk] = false,
                    [P.KeyError] = "Host error while spawning the loadout.",
                    [P.KeyStatus] = BuildStatus(plr)
                };
            }
        }

        /// <summary>Validates and executes a purchase. Returns null on success or an error message.</summary>
        private static string TryBuy(ScavPlayer plr, JToken payload)
        {
            if (!Enabled) return "The host has turned the loadout menu off.";
            if (plr == null) return "Player not found on host.";
            var key = KeyOf(plr);
            if (Records.TryGetValue(key, out var record))
                return record == RecordState.Used ? "Loadout already taken this campaign." : "Loadout window has closed.";
            if (!PendingByKey.TryGetValue(key, out var pending)) return "Loadout is not available yet.";

            var reason = Evaluate(pending, plr, out _, out _);
            if (reason != null)
            {
                Expire(key, plr.playerName, reason);
                return "Loadout window has closed: " + reason + ".";
            }
            if (plr.body == null || !plr.body.alive) return "Your character is not in the world.";

            if (!(payload?[P.KeyItems] is JObject items) || !items.HasValues) return "Nothing selected.";

            // The client's payload is untrusted: parse amounts here, the shared check does the rest.
            var selection = new List<KeyValuePair<string, int>>();
            foreach (var prop in items.Properties())
            {
                int count;
                try { count = prop.Value.Value<int>(); }
                catch { return "Bad amount for " + (LoadoutCatalog.TryGet(prop.Name, out var bad) ? bad.Name : prop.Name); }
                selection.Add(new KeyValuePair<string, int>(prop.Name, count));
            }
            var error = LoadoutOrder.Build(selection, out var order, out var cost, out var total);
            if (error != null) return error;

            LoadoutOrder.Spawn(plr.body, order);

            PendingByKey.Remove(key);
            Records[key] = RecordState.Used;
            var msg = $"{plr.playerName} received a loadout ({total} items, cost {cost}/{ModConfig.Budget.Value}).";
            Plugin.Logger.LogInfo(msg);
            if (ConsoleScript.instance != null) ConsoleScript.instance.LogToConsole(msg);
            return null;
        }

        // ---- GiveLoadout (cheat) ----

        /// <summary>
        /// Grants a fresh loadout window. Gated like Together's cheat commands:
        /// sv_cheats must be on, and for clients AllowClientCheatCommands too.
        /// </summary>
        private static JToken HandleGive(uint sender, JToken payload, bool fromHost)
        {
            if (!Net.IsServer) return null;
            var requester = FindPlayer(sender);
            string error = null, message = null;
            try
            {
                error = TryGive(requester, payload?.Value<string>(P.KeyTarget), fromHost, out message);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError("GiveLoadout failed: " + e);
                error = "Host error while granting the loadout.";
            }
            return new JObject
            {
                [P.KeyOk] = error == null,
                [P.KeyError] = error,
                [P.KeyMessage] = message,
                [P.KeyStatus] = BuildStatus(requester)
            };
        }

        public static string CheatBlockReason(bool fromHost)
        {
            if (!Multiplayer.rules.sv_cheats) return "Server rule 'sv_cheats' is disabled.";
            if (!fromHost && !Multiplayer.rules.AllowClientCheatCommands) return "Server rule 'AllowClientCheatCommands' is disabled.";
            return null;
        }

        private static string TryGive(ScavPlayer requester, string target, bool fromHost, out string message)
        {
            message = null;
            if (!Enabled) return "The loadout menu is turned off on the host.";
            var blocked = CheatBlockReason(fromHost);
            if (blocked != null) return blocked;

            ScavPlayer plr = requester;
            if (!string.IsNullOrWhiteSpace(target))
            {
                plr = ScavPlayer.AllPlayers.FirstOrDefault(p => p != null &&
                    (string.Equals(p.playerName, target, StringComparison.OrdinalIgnoreCase) || p.SteamId.ToString() == target));
                if (plr == null) return $"No player named '{target}'.";
            }
            if (plr == null) return "Player not found on host.";

            var key = KeyOf(plr);
            Records.Remove(key);
            PendingByKey.Remove(key);
            // Open right away at the current position; if the body is not in the world yet,
            // the player's next "ready" status check opens it after spawning.
            TryOpenWindow(plr);

            message = PendingByKey.ContainsKey(key)
                ? $"Loadout menu granted to {plr.playerName}."
                : $"Loadout menu granted to {plr.playerName}; it opens once they are in the world.";
            Plugin.Logger.LogInfo($"GiveLoadout: {message} (requested by {requester?.playerName ?? "?"})");
            return null;
        }

        // ---- console helpers ----

        public static IEnumerable<string> DescribePlayers()
        {
            foreach (var plr in ScavPlayer.AllPlayers.ToList())
            {
                if (plr == null) continue;
                var key = KeyOf(plr);
                string state;
                if (Records.TryGetValue(key, out var rec)) state = rec.ToString().ToLowerInvariant();
                else if (PendingByKey.TryGetValue(key, out var p))
                    state = ModConfig.WindowSeconds.Value <= 0 ? "available (no time limit)"
                        : $"available ({Mathf.Max(0f, ModConfig.WindowSeconds.Value - (Time.realtimeSinceStartup - p.StartTime)):0}s left)";
                else state = "waiting for spawn";
                yield return $"{plr.playerName} [{key}] - {state}";
            }
        }

        /// <summary>Resets the record for players matching name / steam id / "all". Returns how many were reset.</summary>
        public static int Reset(string target)
        {
            if (string.Equals(target, "all", StringComparison.OrdinalIgnoreCase))
            {
                var n = Records.Count + PendingByKey.Count;
                Records.Clear();
                PendingByKey.Clear();
                return n;
            }

            var keys = new HashSet<string>();
            foreach (var plr in ScavPlayer.AllPlayers.ToList())
            {
                if (plr == null) continue;
                if (string.Equals(plr.playerName, target, StringComparison.OrdinalIgnoreCase) ||
                    plr.SteamId.ToString() == target)
                    keys.Add(KeyOf(plr));
            }
            keys.Add("steam:" + target);
            keys.Add("name:" + target);

            int count = 0;
            foreach (var key in keys)
            {
                if (Records.Remove(key)) count++;
                if (PendingByKey.Remove(key)) count++;
            }
            return count;
        }

        // ---- persistence (stored once in the shared multiplayer save) ----

        private sealed class PlayersSaveProvider : ICustomSaveProvider
        {
            public int GetVersion() => 1;

            public JToken Capture()
            {
                if (Net.IsRunning && !Net.IsServer) return null;
                var players = new JObject();
                foreach (var pair in Records)
                    players[pair.Key] = pair.Value == RecordState.Used ? P.StateUsed : P.StateExpired;
                // A window still open at save time counts as spent: reloading must not grant a fresh one.
                foreach (var key in PendingByKey.Keys)
                    if (players[key] == null) players[key] = P.StateExpired;
                return new JObject { ["players"] = players };
            }

            public void Restore(JToken payload, int version, SaveRestoreContext context)
            {
                if (Net.IsRunning && !Net.IsServer) return;
                // Merge, never clear: Together also force-loads per-player checkpoint saves mid-run,
                // which must not wipe the campaign records. Clearing happens in OnNewWorld().
                if (!(payload?["players"] is JObject players)) return;
                foreach (var prop in players.Properties())
                {
                    var state = prop.Value.Value<string>() == P.StateUsed ? RecordState.Used : RecordState.Expired;
                    if (!Records.TryGetValue(prop.Name, out var existing) || existing != RecordState.Used)
                        Records[prop.Name] = state;
                    PendingByKey.Remove(prop.Name);
                }
                Plugin.Logger.LogInfo($"Restored loadout records for {Records.Count} player(s).");
            }
        }
    }
}
