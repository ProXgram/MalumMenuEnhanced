using UnityEngine;

namespace MalumMenu;

public class HostOnlyTab : ITab
{
    public string name => "Host Only";

    public void Draw()
    {
        GUILayout.BeginVertical(GUILayout.Width(MenuUI.windowWidth - 190f));
        GUILayout.Label("Lobby rule: ghost voting", GUIStylePreset.TabSubtitle);
        // The ghost's copy enables local vote selection; only the host counts it.
        CheatToggles.ghostVoting = GUILayout.Toggle(CheatToggles.ghostVoting, " Ghost Voting (host support required)");
        GUILayout.Label("The host and each voting ghost must enable this mod.\nGuests can enable their vote controls here; the host must allow the votes.");
        GUILayout.Space(15f);
        GUILayout.Label(Utils.isFreePlay ? "Host tools available in practice."
            : Utils.isHost ? "You are hosting: host controls are available."
            : "Host controls unlock when you host a lobby or enter practice.");
        bool wasEnabled = GUI.enabled;
        GUI.enabled = wasEnabled && (Utils.isHost || Utils.isFreePlay);
        try
        {
            GUILayout.Label("Role assignment", GUIStylePreset.TabSubtitle);
            RolePreviewControls.DrawAlwaysImpostor(" Always Impostor (real host / practice role)");
            GUILayout.Space(15f);
            DrawMeetings();
            GUILayout.Space(15f);
            GUILayout.Label("Kill rules", GUIStylePreset.TabSubtitle);
            DrawGeneral();
            GUILayout.Space(15f);
            DrawMurder();
            GUILayout.Space(15f);
            GUILayout.Label("Ship rules", GUIStylePreset.TabSubtitle);
            CheatToggles.unfixableLights = GUILayout.Toggle(CheatToggles.unfixableLights, " Unfixable Lights");
            CheatToggles.kickVents = GUILayout.Toggle(CheatToggles.kickVents, " Kick All From Vents");
            GUILayout.Space(15f);
            DrawGameState();
        }
        finally { GUI.enabled = wasEnabled; }
        GUILayout.EndVertical();
    }

    private void DrawGeneral()
    {
        CheatToggles.killVanished = GUILayout.Toggle(CheatToggles.killVanished, " Kill While Vanished");

        CheatToggles.killAnyone = GUILayout.Toggle(CheatToggles.killAnyone, " Kill Anyone");

        CheatToggles.noKillCd = GUILayout.Toggle(CheatToggles.noKillCd, " No Kill Cooldown");

        CheatToggles.showProtectMenu = GUILayout.Toggle(CheatToggles.showProtectMenu, " Show Protect Menu");

        // CheatToggles.forceRole = GUILayout.Toggle(CheatToggles.forceRole, " Force Role");

        // CheatToggles.noOptionsLimits = GUILayout.Toggle(CheatToggles.noOptionsLimits, " No Options Limits");
    }

    private void DrawMurder()
    {
        GUILayout.Label("Player actions", GUIStylePreset.TabSubtitle);

        CheatToggles.killPlayer = GUILayout.Toggle(CheatToggles.killPlayer, " Kill Player");

        CheatToggles.telekillPlayer = GUILayout.Toggle(CheatToggles.telekillPlayer, " Telekill Player");

        CheatToggles.killAllCrew = GUILayout.Toggle(CheatToggles.killAllCrew, " Kill All Crewmates");

        CheatToggles.killAllImps = GUILayout.Toggle(CheatToggles.killAllImps, " Kill All Impostors");

        CheatToggles.killAll = GUILayout.Toggle(CheatToggles.killAll, " Kill Everyone");
    }

    private void DrawGameState()
    {
        GUILayout.Label("Game State", GUIStylePreset.TabSubtitle);

        CheatToggles.forceStartGame = GUILayout.Toggle(CheatToggles.forceStartGame, " Force Start Game");

        CheatToggles.noGameEnd = GUILayout.Toggle(CheatToggles.noGameEnd, " No Game End");
    }

    private void DrawMeetings()
    {
        GUILayout.Label("Meetings", GUIStylePreset.TabSubtitle);
        CheatToggles.callMeeting = GUILayout.Toggle(CheatToggles.callMeeting, " Force emergency meeting");
        CheatToggles.skipMeeting = GUILayout.Toggle(CheatToggles.skipMeeting, " End meeting for everyone");

        CheatToggles.voteImmune = GUILayout.Toggle(CheatToggles.voteImmune, " Vote Immune");

        CheatToggles.ejectPlayer = GUILayout.Toggle(CheatToggles.ejectPlayer, " Eject Player");
    }
}
