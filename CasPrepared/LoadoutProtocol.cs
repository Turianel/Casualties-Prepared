namespace CasPrepared
{
    internal static class LoadoutProtocol
    {
        // client -> host request channels (CUCoreLib MultiplayerApi envelope).
        // The protocol version is part of the channel name: CUCoreLib ignores requests on unknown channels,
        // so a host and a client on different protocol versions see each other as "no mod" (no answer).
        // Bump it whenever host and client must agree (1.1: item rules change what a purchase may contain).
        public const string Version = "v2";
        public const string StatusChannel = "casprepared." + Version + ".status";
        public const string BuyChannel = "casprepared." + Version + ".buy";
        public const string GiveChannel = "casprepared." + Version + ".give"; // cheat: grant another loadout window

        // status values
        public const string StateWaiting = "waiting";     // body not spawned yet / not tracked yet
        public const string StateAvailable = "available"; // menu may be used
        public const string StateUsed = "used";           // loadout already taken this campaign
        public const string StateExpired = "expired";     // window time ran out / moved too far / left
        public const string StateDisabled = "disabled";   // host turned the mod off

        // payload keys
        public const string KeyState = "state";
        public const string KeyBudget = "budget";
        public const string KeyMultiplier = "mult";
        public const string KeyRemaining = "remaining";
        public const string KeyDistance = "distance";
        public const string KeyMaxDistance = "maxDistance";
        public const string KeyReason = "reason";
        public const string KeyItems = "items";
        public const string KeyReady = "ready"; // client: loading screen gone, body settled
        public const string KeyOk = "ok";
        public const string KeyError = "error";
        public const string KeyStatus = "status";
        public const string KeyTarget = "target";
        public const string KeyMessage = "message";
        // item rules sync (1.1): the client sends the hash it has, the host sends the rules when it differs
        public const string KeyRulesHash = "rulesHash";
        public const string KeyRules = "rules";
    }
}
