using UnityEngine;

namespace MalumMenu;

public class FunTab : ITab
{
    public string name => "Fun";

    public void Draw()
    {
        MovementAutomation.Pause();
        GUILayout.BeginHorizontal();
        GUILayout.BeginVertical(GUILayout.Width(270f));
        GUILayout.Label("Player stunts", GUIStylePreset.TabSubtitle);
        GUILayout.Label("Target: " + MovementAutomation.TargetName);
        if (GUILayout.Button("Choose next player")) MovementAutomation.CycleTarget();
        DrawMode("Turbo Orbit", MovementMode.TurboOrbit);
        DrawMode("Follow Player", MovementMode.ShadowFollow);
        var targetBoost = Mathf.Round(GUILayout.HorizontalSlider(MalumMenu.targetMovementMultiplier.Value,
            1f, 4f, GUILayout.Width(250f)) * 10f) / 10f;
        if (!Mathf.Approximately(targetBoost, MalumMenu.targetMovementMultiplier.Value))
            MalumMenu.targetMovementMultiplier.Value = targetBoost;
        GUILayout.Label($"Follow / orbit speed: {targetBoost:0.0}x");
        GUILayout.Label("Distant players use at least 2x to catch up.");
        DrawMode("Zigzag Dash", MovementMode.ZigzagDash);
        DrawMode("Lag Mode", MovementMode.LagWalk);
        GUILayout.Label("Hold a movement key: walk with snaps, briefly freeze and teleport, then walk again.");
        var lagPause = Mathf.Round(GUILayout.HorizontalSlider(MalumMenu.lagInterval.Value,
            0.5f, 2f, GUILayout.Width(250f)) * 20f) / 20f;
        if (!Mathf.Approximately(lagPause, MalumMenu.lagInterval.Value))
            MalumMenu.lagInterval.Value = lagPause;
        GUILayout.Label($"Correction interval (varies): {lagPause:0.00}s");
        var lagDistance = Mathf.Round(GUILayout.HorizontalSlider(MalumMenu.lagJumpDistance.Value,
            0.25f, 2f, GUILayout.Width(250f)) * 20f) / 20f;
        if (!Mathf.Approximately(lagDistance, MalumMenu.lagJumpDistance.Value))
            MalumMenu.lagJumpDistance.Value = lagDistance;
        GUILayout.Label($"Maximum teleport: {lagDistance:0.00}m (shorter near walls)");
        GUILayout.Space(10f);
        NicknameEntry.Draw();
        if (GUILayout.Button("Set my nickname"))
        {
            NicknameEntry.StopEditing();
            NicknamePreset.Apply();
        }
        GUILayout.Label(NicknamePreset.StatusText);
        GUILayout.Label("Set your own nickname before the round.\nThe game checks whether it accepts the name.");
        GUILayout.Space(10f);
        var boost = Mathf.Round(GUILayout.HorizontalSlider(MalumMenu.stuntMultiplier.Value,
            1f, 4f, GUILayout.Width(250f)) * 10f) / 10f;
        if (!Mathf.Approximately(boost, MalumMenu.stuntMultiplier.Value))
            MalumMenu.stuntMultiplier.Value = boost;
        GUILayout.Label($"Dash boost: {boost:0.0}x");
        GUILayout.EndVertical();

        GUILayout.Space(12f);
        GUILayout.BeginVertical(GUILayout.Width(240f));
        GUILayout.Label("Teleport Yo-Yo", GUIStylePreset.TabSubtitle);
        if (GUILayout.Button(MovementAutomation.HasPointA ? "Replace spot A" : "Save spot A"))
            MovementAutomation.SavePointA();
        if (GUILayout.Button(MovementAutomation.HasPointB ? "Replace spot B" : "Save spot B"))
            MovementAutomation.SavePointB();
        DrawMode("Start Yo-Yo", MovementMode.TeleportYoYo);
        var interval = Mathf.Round(GUILayout.HorizontalSlider(MalumMenu.yoyoInterval.Value,
            1f, 5f, GUILayout.Width(230f)) * 10f) / 10f;
        if (!Mathf.Approximately(interval, MalumMenu.yoyoInterval.Value))
            MalumMenu.yoyoInterval.Value = interval;
        GUILayout.Label($"Jump every {interval:0.0}s, up to 12 jumps.");
        GUILayout.Space(10f);
        GUILayout.Label("Task automation is in Tasks.");
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();

        GUILayout.Space(15f);
        if (GUILayout.Button("STOP ALL  (F8)", GUILayout.Width(270f)))
        {
            MovementAutomation.Stop();
            ColorCycleHandler.Stop();
        }
        GUILayout.Label(MovementAutomation.StatusText);
        if (!string.IsNullOrEmpty(MovementAutomation.LastNavigationIssue))
            GUILayout.Label("Last route issue: " + MovementAutomation.LastNavigationIssue);
        GUILayout.Label("Close the menu and chat to start moving. One mode runs at a time.");
    }

    private static void DrawMode(string label, MovementMode mode)
    {
        if (GUILayout.Button((MovementAutomation.Mode == mode ? "Stop " : "") + label))
        {
            if (MovementAutomation.Mode == mode) MovementAutomation.Stop();
            else MovementAutomation.Start(mode);
        }
    }
}
