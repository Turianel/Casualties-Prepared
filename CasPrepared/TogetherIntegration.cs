using System;
using System.Reflection;
using HarmonyLib;
using Together;
using UnityEngine;

namespace CasPrepared
{
    /// <summary>
    /// "CasPrepared: Loadout menu" row in Together's Server settings (rules) panel.
    /// Pinned under the rules list instead of being slotted into Together's categories:
    /// the value lives in our BepInEx config (Together's rules struct is sent by memcpy and can't
    /// get new fields), and the category layout changes between Together versions.
    /// Only the host machine can change it; clients see the state the host reported.
    /// </summary>
    internal static class RulesPanelRow
    {
        private const string RuleName = "CasPrepared: Loadout menu";
        private const string TooltipKey = "casprep_rule_enabled";
        private const string Tooltip =
            "Enables the loadout menu on spawn. Budget, range and other settings: casprep_* console commands.";

        private static bool IsHostMachine => !Net.IsRunning || Net.IsServer;
        // locked mid-campaign without sv_cheats, like the console setters
        private static bool CanEdit => IsHostMachine && LoadoutCommands.ConfigLockReason() == null;

        private static GUIStyle _label;
        private static GUIStyle _source;

        /// <summary>Own copy of the skin's label style, so Together's alignment/width state can't hide our text.</summary>
        private static GUIStyle LabelStyle()
        {
            if (_label == null || _source != GUI.skin.label)
            {
                _source = GUI.skin.label;
                _label = new GUIStyle(_source) { wordWrap = false, alignment = TextAnchor.MiddleLeft };
            }
            return _label;
        }

        /// <summary>Draws the whole row (its own horizontal group) at the current layout position.</summary>
        internal static void Draw(float panelWidth)
        {
            var style = LabelStyle();
            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            bool hover;
            if (CanEdit)
            {
                GUILayout.Label(RuleName, style, GUILayout.ExpandWidth(false));
                hover = UIBullshit.CheckCursorOverlap(GUIUtility.GUIToScreenRect(GUILayoutUtility.GetLastRect()));

                bool current = ModConfig.Enabled.Value;
                bool next = GUILayout.Toggle(current, "", GUILayout.ExpandWidth(false));
                hover |= UIBullshit.CheckCursorOverlap(GUIUtility.GUIToScreenRect(GUILayoutUtility.GetLastRect()));
                GUILayout.Label(next ? "on" : "off", style, GUILayout.ExpandWidth(false),
                    GUILayout.Width(Mathf.Max(40f, panelWidth * 0.1f)));
                if (next != current)
                {
                    ModConfig.Enabled.Value = next;
                    try { CasualtiesTogetherUtils.Util.PlayUISound(PlayerCamera.UISoundType.MiniClick); } catch { /* cosmetic */ }
                }
            }
            else
            {
                string value = IsHostMachine ? (ModConfig.Enabled.Value ? "on" : "off")
                    : !LoadoutClient.HasStatus ? "?" : LoadoutClient.IsDisabled ? "off" : "on";
                GUILayout.Label($"{RuleName}: {value}", style, GUILayout.ExpandWidth(false));
                hover = UIBullshit.CheckCursorOverlap(GUIUtility.GUIToScreenRect(GUILayoutUtility.GetLastRect()));
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            if (hover) UIBullshit._GUI_SetTooltip(TooltipKey, Tooltip);
        }
    }

    [HarmonyPatch]
    internal static class UIMainMenu_RulesList_Patch
    {
        private static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(UIMainMenu), "_GUI___ConnectionSecondPanel_ContentRender_RenderRulesList");

        // Runs after Together closed its scroll view: the row stays fixed under the list.
        private static void Postfix(Rect r2)
        {
            try
            {
                var panel = r2; // copy: reading a field of a non-ref patch parameter trips Harmony003
                RulesPanelRow.Draw(panel.width);
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
        private const string Text = "Achievements are disabled in multiplayer while the loadout mod is active.";

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
