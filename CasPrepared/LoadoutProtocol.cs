namespace CasPrepared
{
    internal static class LoadoutProtocol
    {
        // client -> host request channels (CUCoreLib MultiplayerApi envelope)
        public const string StatusChannel = "casprepared.status";
        public const string BuyChannel = "casprepared.buy";
        public const string GiveChannel = "casprepared.give"; // cheat: grant another loadout window

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
    }
}
