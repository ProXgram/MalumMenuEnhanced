using UnityEngine;

namespace MalumMenu;

public class KeybindListener : MonoBehaviour
{
    public void Update()
    {
        if (Input.GetKeyDown(KeyCode.F8))
        {
            MovementAutomation.Stop();
            ColorCycleHandler.Stop();
        }
        if (!Application.isFocused)
        {
            MovementAutomation.Stop();
            ColorCycleHandler.Stop();
        }
        // Run before chat/panic early returns so a held boost cannot get stuck.
        SprintHandler.Tick();
        ColorCycleHandler.Tick();
        NicknameEntry.Tick();
        if (MalumMenu.isPanicked) return;
        // Typing a nickname in the menu must not trigger cheat keybinds.
        if (MenuUI.isGUIActive) return;

        // Keybinds aren't triggered from typing in the chat
        if (HudManager.InstanceExists && HudManager.Instance.Chat && HudManager.Instance.Chat.IsOpenOrOpening) return;

        // Check each keybind to see if the user pressed it and toggle the corresponding cheat
        foreach (var (name, key) in CheatToggles.Keybinds)
        {
            if (key == KeyCode.None) continue;
            if (!Input.GetKeyDown(key)) continue;

            if (!CheatToggles.ToggleFields.TryGetValue(name, out var field)) continue;

            var current = (bool)field.GetValue(null);
            field.SetValue(null, !current);
        }
    }

    public void OnApplicationFocus(bool focused)
    {
        if (!focused)
        {
            MovementAutomation.Stop();
            ColorCycleHandler.Stop();
            NicknameEntry.StopEditing();
            SprintHandler.Reset();
        }
    }

    public void OnDisable()
    {
        MovementAutomation.Stop();
        ColorCycleHandler.Stop();
        NicknameEntry.StopEditing();
        SprintHandler.Reset();
    }

    public void OnDestroy()
    {
        MovementAutomation.Stop();
        ColorCycleHandler.Stop();
        NicknameEntry.StopEditing();
        SprintHandler.Reset();
    }
}
