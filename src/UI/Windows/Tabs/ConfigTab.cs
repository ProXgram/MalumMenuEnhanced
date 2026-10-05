using UnityEngine;

namespace MalumMenu;

public class ConfigTab : ITab
{
    public string name => "Settings";

    public void Draw()
    {
        GUILayout.BeginVertical(GUILayout.Width(MenuUI.windowWidth * 0.425f));

        DrawGeneral();
        GUILayout.Space(18f);
        GUILayout.Label("Convenience", GUIStylePreset.TabSubtitle);
        CheatToggles.avoidPenalties = GUILayout.Toggle(CheatToggles.avoidPenalties, " Avoid Penalties");
        CheatToggles.unlockFeatures = GUILayout.Toggle(CheatToggles.unlockFeatures, " Unlock Extra Features");
        CheatToggles.copyLobbyCodeOnDisconnect = GUILayout.Toggle(CheatToggles.copyLobbyCodeOnDisconnect, " Copy Lobby Code on Disconnect");
        CheatToggles.spoofAprilFoolsDate = GUILayout.Toggle(CheatToggles.spoofAprilFoolsDate, " Spoof Date to April 1st");
        GUILayout.Space(18f);
        GUILayout.Label("Menu display", GUIStylePreset.TabSubtitle);
        CheatToggles.rgbMode = GUILayout.Toggle(CheatToggles.rgbMode, " RGB Menu");
        CheatToggles.stealthMode = GUILayout.Toggle(CheatToggles.stealthMode, " Stealth Mode");
        CheatToggles.panicMode = GUILayout.Toggle(CheatToggles.panicMode, " Panic Mode");

        GUILayout.EndVertical();
    }

    private void DrawGeneral()
    {
        GUILayout.Label("Profiles and configuration", GUIStylePreset.TabSubtitle);
        CheatToggles.openConfig = GUILayout.Toggle(CheatToggles.openConfig, " Open Config");

        CheatToggles.reloadConfig = GUILayout.Toggle(CheatToggles.reloadConfig, " Reload Config");

        CheatToggles.saveProfile = GUILayout.Toggle(CheatToggles.saveProfile, " Save to Profile");

        CheatToggles.loadProfile = GUILayout.Toggle(CheatToggles.loadProfile, " Load from Profile");

        GUILayout.Space(18f);
        GUILayout.Label(ModBranding.Name + " v" + MalumMenu.malumVersion);
        GUILayout.Label("Your edition by " + ModBranding.Creator);
        GUILayout.Label(ModBranding.Attribution);
        GUILayout.Label("Original authors: " + ModBranding.OriginalAuthors);

        GUILayout.Space(18f);
        GUILayout.Label("Mod updates", GUIStylePreset.TabSubtitle);
        if (MalumMenu.automaticUpdates != null)
            MalumMenu.automaticUpdates.Value = GUILayout.Toggle(MalumMenu.automaticUpdates.Value, " Automatic updates (next launch)");
        GUILayout.Label(MalumMenuEnhanced.Updates.AutomaticUpdateHandler.Status);
        GUILayout.Label("Compatible updates install after you close Among Us.");
    }
}
