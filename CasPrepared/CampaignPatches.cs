using HarmonyLib;

namespace CasPrepared
{
    /// <summary>
    /// WorldGeneration is created once per run scene: a new game or loading a save.
    /// Layer descents reuse the same instance (RegenerateWorld), so this marks a campaign boundary.
    /// A loaded save restores its records right after via the CUCoreLib save provider.
    /// </summary>
    [HarmonyPatch(typeof(WorldGeneration), "Awake")]
    internal static class WorldGeneration_Awake_CampaignPatch
    {
        private static void Postfix()
        {
            LoadoutServer.OnNewWorld();
            LoadoutClient.ResetLocal();
        }
    }
}
