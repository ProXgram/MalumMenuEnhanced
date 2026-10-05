using UnityEngine;

namespace MalumMenu;

public class PracticeTab : ITab
{
    public string name => "Practice";

    public void Draw()
    {
        GUILayout.BeginVertical(GUILayout.Width(270f));
        GUILayout.Label("Freeplay only", GUIStylePreset.TabSubtitle);
        GUILayout.Label("Open Freeplay to use these\npractice role controls.");
        GUILayout.Space(15f);

        bool wasEnabled = GUI.enabled;
        GUI.enabled = wasEnabled && Utils.isFreePlay;
        try
        {
            GUILayout.Label("Impostor", GUIStylePreset.TabSubtitle);
            RolePreviewControls.DrawAlwaysImpostor(" Always Impostor (Freeplay)");

            GUILayout.Space(15f);
            GUILayout.Label("Judge", GUIStylePreset.TabSubtitle);
            CheatToggles.infiniteJudge = GUILayout.Toggle(CheatToggles.infiniteJudge,
                " Infinite Judge (Freeplay only)");
        }
        finally
        {
            GUI.enabled = wasEnabled;
        }

        GUILayout.Label(InfiniteJudgeHandler.StatusText, GUIStylePreset.TabSubtitle,
            GUILayout.Width(270f));
        if (Utils.isFreePlay)
            GUILayout.Label("Practice Judge; one use per meeting.", GUIStylePreset.TabSubtitle,
                GUILayout.Width(270f));
        GUILayout.EndVertical();
    }
}
