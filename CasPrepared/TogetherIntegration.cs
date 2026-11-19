using System;
using System.Reflection;
using HarmonyLib;
using Together;
using UnityEngine;

namespace CasPrepared
{
    /// <summary>
    /// "CasPrepared: Loadout menu" row in Together's Server settings (rules) list.
    /// Drawn with Together's own skin so it looks like a native rule. The value lives in our
    /// BepInEx config (Together's rules struct is sent by memcpy and can't get new fields),
    /// so only the host machine can change it; clients see the state the host reported.
    /// </summary>
    internal static class RulesPanelRow
    {
        private const string RuleName = "CasPrepared: Loadout menu";
        private const string TooltipKey = "casprep_rule_enabled";
        private const string Tooltip =
            "Enables the loadout menu on spawn. Budget, range and other settings: casprep_* console commands.";
        // our row sits right after the cheat rules
        internal const string AnchorField = "AllowClientCheatCommands";

        internal static bool DrawnThisPass;

        internal static bool MatchesSearch()
        {
            var search = UIMainMenu._GUI____RenderRuleField_search;
            if (string.IsNullOrWhiteSpace(search)) return true;
            return RuleName.IndexOf(search.Trim(), StringComparison.OrdinalIgnoreCase) >= 0 ||
                   "loadout casprepared".IndexOf(search.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsHostMachine => !Net.IsRunning || Net.IsServer;
        // locked mid-campaign without sv_cheats, like the console setters
        private static bool CanEdit => IsHostMachine && LoadoutCommands.ConfigLockReason() == null;

        /// <summary>Draws the row content. The caller provides the surrounding horizontal group.</summary>
        internal static void DrawContent()
        {
            DrawnThisPass = true;
            GUI.skin.label.wordWrap = false;

            var size = GUI.skin.label.CalcSize(new GUIContent(RuleName));
            if (size.x > UIMainMenu._GUI____RenderRuleField_cur_max_width)
                UIMainMenu._GUI____RenderRuleField_cur_max_width = size.x;

            bool hover;
            if (CanEdit)
            {
                GUI.skin.label.alignment = TextAnchor.MiddleRight;
                GUILayout.Label(RuleName, GUILayout.Width(UIMainMenu._GUI____RenderRuleField_last_max_width));
                hover = UIBullshit.CheckCursorOverlap(GUIUtility.GUIToScreenRect(GUILayoutUtility.GetLastRect()));
                GUI.skin.label.alignment = TextAnchor.MiddleLeft;

                bool current = ModConfig.Enabled.Value;
                bool next = GUILayout.Toggle(current, "");
                hover |= UIBullshit.CheckCursorOverlap(GUIUtility.GUIToScreenRect(GUILayoutUtility.GetLastRect()));
                if (next != current)
                {
                    ModConfig.Enabled.Value = next;
                    try { CasualtiesTogetherUtils.Util.PlayUISound(PlayerCamera.UISoundType.MiniClick); } catch { /* cosmetic */ }
                }
            }
            else
            {
                GUI.skin.label.alignment = TextAnchor.UpperLeft;
                string value = IsHostMachine ? (ModConfig.Enabled.Value ? "True" : "False")
                    : !LoadoutClient.HasStatus ? "?" : LoadoutClient.IsDisabled ? "False" : "True";
                GUILayout.Label($"{RuleName}:  {value}");
                hover = UIBullshit.CheckCursorOverlap(GUIUtility.GUIToScreenRect(GUILayoutUtility.GetLastRect()));
            }

            if (hover) UIBullshit._GUI_SetTooltip(TooltipKey, Tooltip);
        }
    }

    [HarmonyPatch]
    internal static class UIMainMenu_RulesList_Patch
    {
        private static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(UIMainMenu), "_GUI___ConnectionSecondPanel_ContentRender_RenderRulesList");

        private static void Prefix() => RulesPanelRow.DrawnThisPass = false;

        // Search hid the anchor rule but matches ours: show it under the list instead.
        private static void Postfix()
        {
            if (RulesPanelRow.DrawnThisPass || !RulesPanelRow.MatchesSearch()) return;
            try
            {
                GUILayout.BeginHorizontal();
                RulesPanelRow.DrawContent();
                GUILayout.EndHorizontal();
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("Rules row (fallback) failed: " + e.Message);
            }
        }
    }

    [HarmonyPatch]
    internal static class UIMainMenu_RuleField_Patch
    {
        private static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(UIMainMenu), "_GUI____RenderRuleField");

        // The caller wraps every rule in Begin/EndHorizontal: close the anchor's row,
        // open ours, and let the caller's EndHorizontal close it.
        private static void Postfix(FieldInfo fld)
        {
            if (fld == null || fld.Name != RulesPanelRow.AnchorField || !RulesPanelRow.MatchesSearch()) return;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            try
            {
                RulesPanelRow.DrawContent();
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("Rules row failed: " + e.Message);
            }
        }
    }

    /// <summary>Notice on the game's title screen while the mod is on.</summary>
    internal static class MenuBanner
    {
        private const string Text = "Achievements disabled: loadout mod is active.";

        public static void Draw()
        {
            if (!ModConfig.Enabled.Value || WorldGeneration.world != null) return;
            if (Event.current.type != EventType.Repaint) return;
            PixelSkin.Ensure();

            var style = PixelSkin.Banner;
            var size = style.CalcSize(new GUIContent(Text));
            float u = PixelSkin.Unit;
            var rect = new Rect((Screen.width - size.x) / 2f, u * 0.6f, size.x, Mathf.Max(size.y, u * 1.8f));
            GUI.Label(rect, Text, style);
        }
    }
}
