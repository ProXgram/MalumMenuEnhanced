using UnityEngine;

namespace MalumMenu;

public class ColorsTab : ITab
{
    public string name => "Appearance";

    public void Draw()
    {
        GUILayout.BeginVertical(GUILayout.Width(MenuUI.windowWidth * 0.66f));
        GUILayout.Label("Changing body color", GUIStylePreset.TabSubtitle);
        DrawMode("Rainbow body (my screen)", ColorCycleMode.LocalRainbow);
        GUILayout.Label("Cycles your body through the game's colors.\nWorks in lobbies and rounds; only your screen sees this preview.");
        var previewInterval = Mathf.Round(GUILayout.HorizontalSlider(MalumMenu.rainbowColorInterval.Value,
            0.15f, 3f, GUILayout.Width(280f)) * 100f) / 100f;
        if (!Mathf.Approximately(previewInterval, MalumMenu.rainbowColorInterval.Value))
            MalumMenu.rainbowColorInterval.Value = previewInterval;
        GUILayout.Label($"Preview color every {previewInterval:0.00}s");

        GUILayout.Space(18f);
        DrawMode("Cycle lobby colors (shared)", ColorCycleMode.LobbyCycle);
        GUILayout.Label("Requests available colors for your own player before a round.\nUses normal color selection; other players' view still needs testing.\nStops when the round starts.");
        var lobbyInterval = Mathf.Round(GUILayout.HorizontalSlider(MalumMenu.lobbyColorInterval.Value,
            2f, 10f, GUILayout.Width(280f)) * 10f) / 10f;
        if (!Mathf.Approximately(lobbyInterval, MalumMenu.lobbyColorInterval.Value))
            MalumMenu.lobbyColorInterval.Value = lobbyInterval;
        GUILayout.Label($"Lobby color every {lobbyInterval:0.0}s");

        GUILayout.Space(18f);
        if (GUILayout.Button("Stop changing colors (F8)", GUILayout.Width(280f)))
            ColorCycleHandler.Stop();
        GUILayout.Label(ColorCycleHandler.StatusText);
        GUILayout.Label("Changes your player only. Keeps your chosen skin, hat, pet and visor.");

        GUILayout.Space(18f);
        GUILayout.Label("Random outfit", GUIStylePreset.TabSubtitle);
        CheatToggles.freeCosmetics = GUILayout.Toggle(CheatToggles.freeCosmetics,
            " Include Free Cosmetics choices");
        if (GUILayout.Button("Pick random outfit", GUILayout.Width(280f)))
            RandomOutfitHandler.Apply();
        if (RandomOutfitHandler.CanRestore &&
            GUILayout.Button("Restore previous outfit", GUILayout.Width(280f)))
            RandomOutfitHandler.Restore();
        GUILayout.Label("Picks a hat, skin, visor and pet before a round or in practice.\nUses the choices available with your Free Cosmetics setting.");
        GUILayout.Label(RandomOutfitHandler.StatusText);
        GUILayout.EndVertical();
    }

    private static void DrawMode(string label, ColorCycleMode mode)
    {
        if (GUILayout.Button((ColorCycleHandler.Mode == mode ? "Stop " : "") + label,
            GUILayout.Width(320f)))
        {
            if (ColorCycleHandler.Mode == mode) ColorCycleHandler.Stop();
            else ColorCycleHandler.Start(mode);
        }
    }
}
