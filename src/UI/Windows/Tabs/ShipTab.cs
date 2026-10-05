using UnityEngine;

namespace MalumMenu;

public class ShipTab : ITab
{
    public string name => "Ship";

    public void Draw()
    {
        GUILayout.BeginHorizontal();

        GUILayout.BeginVertical(GUILayout.Width(MenuUI.windowWidth * 0.425f));

        DrawGeneral();

        GUILayout.Space(15);

        DrawSabotage();

        GUILayout.EndVertical();

        GUILayout.BeginVertical();

        DrawVents();

        GUILayout.EndVertical();

        GUILayout.EndHorizontal();
    }

    private void DrawGeneral()
    {
        GUILayout.Label("Own-player requests", GUIStylePreset.TabSubtitle);
        if (!Utils.isHost && !Utils.isFreePlay)
        {
            CheatToggles.callMeeting = GUILayout.Toggle(CheatToggles.callMeeting, " Request emergency meeting");
            GUILayout.Label("The host checks this request.");
        }
        CheatToggles.closeMeeting = GUILayout.Toggle(CheatToggles.closeMeeting, " Close meeting on my screen");
        GUILayout.Label("Does not end everyone else's meeting.");

        CheatToggles.autoReportBodies = GUILayout.Toggle(CheatToggles.autoReportBodies, " Auto-Report Dead Bodies");

        CheatToggles.autoOpenDoorsOnUse = GUILayout.Toggle(CheatToggles.autoOpenDoorsOnUse, " Auto-Open Doors On Use");
        GUILayout.Space(10f);
        GUILayout.Label("Ghost Voting and shared ship rules\nare in Host Only.");
    }

    private void DrawSabotage()
    {
        GUILayout.Label("Sabotage", GUIStylePreset.TabSubtitle);
        GUILayout.Label("Game requests; host and role checks apply.");

        CheatToggles.reactorSab = GUILayout.Toggle(CheatToggles.reactorSab, " Reactor");

        CheatToggles.oxygenSab = GUILayout.Toggle(CheatToggles.oxygenSab, " Oxygen");

        CheatToggles.elecSab = GUILayout.Toggle(CheatToggles.elecSab, " Lights");

        CheatToggles.commsSab = GUILayout.Toggle(CheatToggles.commsSab, " Comms");

        CheatToggles.showDoorsMenu = GUILayout.Toggle(CheatToggles.showDoorsMenu, " Show Doors Menu");

        CheatToggles.mushSab = GUILayout.Toggle(CheatToggles.mushSab, " Mushroom Mixup");

        CheatToggles.mushSpore = GUILayout.Toggle(CheatToggles.mushSpore, " Trigger Spores");

        CheatToggles.sabotageMap = GUILayout.Toggle(CheatToggles.sabotageMap, " Open Sabotage Map");
    }

    private void DrawVents()
    {
        GUILayout.Label("Vents", GUIStylePreset.TabSubtitle);

        CheatToggles.unlockVents = GUILayout.Toggle(CheatToggles.unlockVents, " Unlock Vents");

        CheatToggles.walkInVents = GUILayout.Toggle(CheatToggles.walkInVents, " Walk In Vents");
    }
}
