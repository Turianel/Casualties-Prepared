using UnityEngine;

namespace CasPrepared
{
    /// <summary>
    /// Menu-side UI of the standalone build:
    /// a notice on the title screen while the mod is on, and an on/off switch on the character select
    /// (run settings) screen, where the run that will be affected is being set up.
    /// </summary>
    internal static class SoloMenu
    {
        private const string TitleNotice = "Achievements are disabled while the loadout mod is active.";
        private const string SwitchOn = "LOADOUT MENU: ON";
        private const string SwitchOff = "LOADOUT MENU: OFF";
        private const string NoteOn = "Achievements will be disabled for this run.";
        private const string NoteOff = "No loadout menu this run.";

        public static void Draw()
        {
            if (WorldGeneration.world != null) return;
            var pre = PreRunScript.instance;
            if (pre == null) return;

            if (IsRunSettingsOpen(pre)) DrawSwitch();
            else if (ModConfig.Enabled.Value && !AnyOverlayOpen(pre)) DrawTitleNotice();
        }

        private static bool IsRunSettingsOpen(PreRunScript pre) =>
            pre.runSettingsScreen != null && pre.runSettingsScreen.activeInHierarchy && !pre.startedLoading;

        private static bool AnyOverlayOpen(PreRunScript pre) =>
            (pre.creditsScreen != null && pre.creditsScreen.activeInHierarchy) ||
            (pre.tutorialWarning != null && pre.tutorialWarning.activeInHierarchy) ||
            (pre.contentWarning != null && pre.contentWarning.activeInHierarchy);

        private static void DrawTitleNotice()
        {
            if (Event.current.type != EventType.Repaint) return;
            PixelSkin.Ensure();
            var style = PixelSkin.Banner;
            var size = style.CalcSize(new GUIContent(TitleNotice));
            float u = PixelSkin.Unit;
            var rect = new Rect((Screen.width - size.x) / 2f, u * 0.6f, size.x, Mathf.Max(size.y, u * 1.8f));
            GUI.Label(rect, TitleNotice, style);
        }

        /// <summary>Framed switch in the top-left corner of the character select screen (keeps the screen title clear).</summary>
        private static void DrawSwitch()
        {
            PixelSkin.Ensure();
            float u = PixelSkin.Unit;
            bool on = ModConfig.Enabled.Value;
            string note = on ? NoteOn : NoteOff;

            var noteStyle = on ? PixelSkin.Warning : PixelSkin.Muted;
            float w = Mathf.Max(PixelSkin.BigButton.CalcSize(new GUIContent(SwitchOff)).x + u * 2f,
                                noteStyle.CalcSize(new GUIContent(note)).x + u * 2f);
            float h = u * 4.6f;
            var panel = new Rect(u * 0.6f, u * 0.6f, w, h);

            var prevSkin = GUI.skin;
            GUI.skin = PixelSkin.Skin;
            try
            {
                GUI.Box(panel, GUIContent.none, PixelSkin.Panel);
                var button = new Rect(panel.x + u * 0.5f, panel.y + u * 0.5f, panel.width - u, u * 2.2f);
                var content = new GUIContent(on ? SwitchOn : SwitchOff);
                if (GUI.Button(button, content, PixelSkin.BigButton))
                {
                    ModConfig.Enabled.Value = !on;
                    try { PlayerCamera.PlayUISound("click"); } catch { /* cosmetic */ }
                }
                var noteRect = new Rect(panel.x, button.yMax + u * 0.2f, panel.width, u * 1.4f);
                var centered = new GUIStyle(noteStyle) { alignment = TextAnchor.MiddleCenter };
                GUI.Label(noteRect, note, centered);
            }
            finally
            {
                GUI.skin = prevSkin;
            }
        }
    }
}
