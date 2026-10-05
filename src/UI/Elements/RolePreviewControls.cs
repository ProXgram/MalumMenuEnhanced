using UnityEngine;

namespace MalumMenu;

public static class RolePreviewControls
{
    public static void DrawAlwaysImpostor(string label)
    {
        bool wasEnabled = CheatToggles.alwaysImpostor;
        CheatToggles.alwaysImpostor = GUILayout.Toggle(CheatToggles.alwaysImpostor, label);
        if (wasEnabled != CheatToggles.alwaysImpostor)
            AlwaysImpostorHandler.ResetFreeplayAttempt();

        if (CheatToggles.alwaysImpostor)
            GUILayout.Label(AlwaysImpostorHandler.StatusText, GUIStylePreset.TabSubtitle,
                GUILayout.Width(270f));
    }
}
