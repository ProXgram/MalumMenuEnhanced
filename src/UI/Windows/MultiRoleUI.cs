using UnityEngine;

namespace MalumMenu;

public class MultiRoleUI : MonoBehaviour
{
    private const float WindowWidth = 300f;
    private const float ExpandedHeight = 446f;
    private const float CollapsedHeight = 54f;
    private const float ScreenMargin = 8f;

    public static Rect windowRect;
    private GUIStyle _wrappedLabel;
    // Keep the notebook control below the native map's upper-left Close button.
    private static readonly Rect NotesCloseRect = new(12f, 180f, 174f, 32f);

    public static bool IsNotesCloseVisible => MultiRoleDetectiveHandler.NotesOpen &&
        MultiRoleHandler.Active && Minigame.Instance &&
        MultiRoleDetectiveHandler.Owns(Minigame.Instance.TryCast<DetectiveNotesMinigame>()) &&
        !MenuUI.isGUIActive && !Utils.isMeeting && !Utils.isExiling &&
        HudManager.InstanceExists && !HudManager.Instance.IsIntroDisplayed &&
        !(HudManager.Instance.Chat && HudManager.Instance.Chat.IsOpenOrOpening);

    public static bool BlocksNativeInput(Vector2 position) =>
        (IsVisible && windowRect.Contains(position)) ||
        (IsNotesCloseVisible && NotesCloseRect.Contains(position));

    public static bool IsVisible
    {
        get
        {
            if (!MultiRoleHandler.Active || MalumMenu.isPanicked || MenuUI.isGUIActive ||
                Utils.isMeeting || Utils.isExiling || Minigame.Instance ||
                !HudManager.InstanceExists) return false;

            var hud = HudManager.Instance;
            return hud && !hud.IsIntroDisplayed && !(hud.Chat && hud.Chat.IsOpenOrOpening);
        }
    }

    private void Start()
    {
        // Leave space below the panel for the game's native ability buttons.
        windowRect = new Rect(Screen.width - WindowWidth - 16f,
            Screen.height - ExpandedHeight - 118f, WindowWidth, ExpandedHeight);
        ClampWindow();
    }

    private static void ClampWindow()
    {
        windowRect.width = Mathf.Min(WindowWidth, Mathf.Max(160f, Screen.width - ScreenMargin * 2f));
        windowRect.height = MultiRoleHandler.PanelOpen ? ExpandedHeight : CollapsedHeight;
        windowRect.x = Mathf.Clamp(windowRect.x, ScreenMargin,
            Mathf.Max(ScreenMargin, Screen.width - windowRect.width - ScreenMargin));
        windowRect.y = Mathf.Clamp(windowRect.y, ScreenMargin,
            Mathf.Max(ScreenMargin, Screen.height - windowRect.height - ScreenMargin));
    }

    private void OnGUI()
    {
        if (IsNotesCloseVisible)
        {
            var savedBackground = GUI.backgroundColor;
            try
            {
                UIHelpers.ApplyUIColor();
                if (GUI.Button(NotesCloseRect, "Close Detective Notes"))
                    MultiRoleDetectiveHandler.CloseNotes();
            }
            finally { GUI.backgroundColor = savedBackground; }
            return;
        }
        if (!IsVisible) return;

        _wrappedLabel ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            wordWrap = true,
            richText = false,
            alignment = TextAnchor.UpperLeft
        };

        var backgroundColor = GUI.backgroundColor;
        var previousEnabled = GUI.enabled;
        try
        {
            UIHelpers.ApplyUIColor();
            ClampWindow();
            windowRect = GUI.Window((int)WindowId.MultiRoleUI, windowRect,
                (GUI.WindowFunction)DrawWindow, "Multi Role");
            ClampWindow();
        }
        finally
        {
            GUI.backgroundColor = backgroundColor;
            GUI.enabled = previousEnabled;
        }
    }

    private void DrawWindow(int windowId)
    {
        var innerWidth = windowRect.width - 24f;
        if (GUI.Button(new Rect(windowRect.width - 34f, 2f, 28f, 20f),
            MultiRoleHandler.PanelOpen ? "-" : "+"))
            MultiRoleHandler.PanelOpen = !MultiRoleHandler.PanelOpen;

        GUI.Label(new Rect(12f, 28f, innerWidth, 22f), "Vent + Track + Vitals + Detective", _wrappedLabel);

        if (MultiRoleHandler.PanelOpen)
        {
            var enabled = GUI.enabled;
            GUI.enabled = enabled && MultiRoleHandler.VitalsAvailable;
            if (GUI.Button(new Rect(12f, 56f, innerWidth, 27f), "Vitals"))
                MultiRoleHandler.OpenVitals();
            GUI.enabled = enabled;

            GUI.Label(new Rect(12f, 89f, innerWidth, 43f),
                MultiRoleHandler.TrackingText, _wrappedLabel);

            var halfWidth = (innerWidth - 8f) / 2f;
            GUI.enabled = enabled && MultiRoleHandler.CanOpenTool;
            if (GUI.Button(new Rect(12f, 137f, halfWidth, 27f),
                MultiRoleHandler.HasTarget ? "Next target" : "Choose target"))
                MultiRoleHandler.CycleTarget();

            GUI.enabled = enabled && MultiRoleHandler.CanOpenTool && MultiRoleHandler.HasTarget;
            if (GUI.Button(new Rect(20f + halfWidth, 137f, halfWidth, 27f), "Clear target"))
                MultiRoleHandler.ClearTarget();
            if (GUI.Button(new Rect(12f, 170f, innerWidth, 27f), "Tracking map"))
                MultiRoleHandler.OpenTrackingMap();
            GUI.enabled = enabled;

            GUI.enabled = enabled && MultiRoleHandler.DetectiveAvailable;
            if (GUI.Button(new Rect(12f, 204f, halfWidth, 27f), "Detective Notes"))
                MultiRoleHandler.OpenDetectiveNotes();
            GUI.enabled = enabled && MultiRoleHandler.CanInterrogate;
            if (GUI.Button(new Rect(20f + halfWidth, 204f, halfWidth, 27f), "Interrogate target"))
                MultiRoleHandler.InterrogateTarget();
            GUI.enabled = enabled;

            GUI.Label(new Rect(12f, 237f, innerWidth, 95f),
                MultiRoleHandler.DetectiveStatusText, _wrappedLabel);
            GUI.Label(new Rect(12f, 339f, innerWidth, 66f),
                MultiRoleHandler.StatusText, _wrappedLabel);
            if (GUI.Button(new Rect(12f, 411f, innerWidth, 27f), "Turn off Multi Role"))
                MultiRoleHandler.SetEnabled(false);
        }

        GUI.DragWindow(new Rect(0f, 0f, windowRect.width - 38f, 24f));
    }
}
