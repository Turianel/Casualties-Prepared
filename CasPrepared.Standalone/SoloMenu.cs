using UnityEngine;

namespace CasPrepared
{
    /// <summary>
    /// Menu-side UI of the standalone build: an on/off switch on the character select
    /// (run settings) screen, where the run that will be affected is being set up.
    /// </summary>
    internal static class SoloMenu
    {
        private const string SwitchOn = "LOADOUT MENU: ON";
        private const string SwitchOff = "LOADOUT MENU: OFF";
        private const string NoteOn = "Loadout menu opens after you spawn.";
        private const string NoteOff = "No loadout menu this run.";

        public static void Draw()
        {
            if (WorldGeneration.world != null) return;
            var pre = PreRunScript.instance;
            if (pre == null) return;

            if (IsRunSettingsOpen(pre)) DrawSwitch();
        }

        private static bool IsRunSettingsOpen(PreRunScript pre) =>
            pre.runSettingsScreen != null && pre.runSettingsScreen.activeInHierarchy && !pre.startedLoading;

        /// <summary>Framed switch in the top-left corner of the character select screen (keeps the screen title clear).</summary>
        private static void DrawSwitch()
        {
            PixelSkin.Ensure();
            float u = PixelSkin.Unit;
            bool on = ModConfig.Enabled.Value;
            string note = on ? NoteOn : NoteOff;

            var noteStyle = on ? PixelSkin.Accent : PixelSkin.Muted;
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
