using UnityEngine;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

namespace MalumMenu;

public class MenuUI : MonoBehaviour
{
    public static int windowHeight = 590;
    public static int windowWidth = 700;
    public static Rect windowRect;

    public static bool isGUIActive = false;
    private List<ITab> _tabs = new();
    private int _selectedTab;
    private Vector2 _tabScrollPosition;
    private Vector2 _contentScrollPosition;
    public static string ActiveTabName => MalumMenu.menuUI &&
        MalumMenu.menuUI._selectedTab >= 0 && MalumMenu.menuUI._selectedTab < MalumMenu.menuUI._tabs.Count
        ? MalumMenu.menuUI._tabs[MalumMenu.menuUI._selectedTab].name : "";
    public static float hue; // For RGB mode

    private void Start()
    {
        // Add all tabs on start
        _tabs.Add(new MovementTab());
        _tabs.Add(new FunTab());
        _tabs.Add(new ColorsTab());
        _tabs.Add(new ESPTab());
        _tabs.Add(new TasksTab());
        _tabs.Add(new HostOnlyTab());
        _tabs.Add(new RolesTab());
        _tabs.Add(new ShipTab());
        _tabs.Add(new ChatTab());
        _tabs.Add(new AnimationsTab());
        _tabs.Add(new ConsoleTab());
        _tabs.Add(new PracticeTab());
        _tabs.Add(new ConfigTab());
#if GUEST_KILL_EXPERIMENT
        _tabs.Add(new GuestRoundTestTab());
#endif
        // _tabs.Add(new OverloadTab());

        // Instantiate 2D area of MenuUI
        windowRect = new(
            Screen.width / 2f - windowWidth / 2f,
            Screen.height / 2f - windowHeight / 2f,
            windowWidth,
            windowHeight
        );
    }

    public void InitStyles()
    {
        GUI.skin.toggle.fontSize = GUI.skin.button.fontSize = GUI.skin.label.fontSize = 15;
        GUI.skin.label.wordWrap = true;
    }

    private void Update()
    {
        MultiRoleHandler.Tick();
        if (Input.GetKeyDown(Utils.StringToKeycode(MalumMenu.menuKeybind.Value)))
        {
            // Enable or disable GUI with DELETE key
            isGUIActive = !isGUIActive;
            if (!isGUIActive) NicknameEntry.StopEditing();

            if (MalumMenu.menuOpenOnMouse.Value)
            {
                // Teleport the window to the mouse for immediate use
                Vector2 mousePosition = Input.mousePosition;
                windowRect.position = new Vector2(mousePosition.x, Screen.height - mousePosition.y);
            }
        }

        if (isGUIActive)
        {
            MovementAutomation.Pause();
            SprintHandler.Reset();
        }

        if (CheatToggles.rgbMode)
        {
            hue += Time.deltaTime * 0.3f; // Adjust speed of color change, higher multiplier = faster
            if (hue > 1f) hue -= 1f; // Loop hue back to 0 when it exceeds 1
        }

        if (CheatToggles.stealthMode != MalumMenu.inStealthMode)
        {
            MalumMenu.inStealthMode = CheatToggles.stealthMode;

            Scene scene = SceneManager.GetActiveScene();

            if (scene.name == "MainMenu" || scene.name == "MatchMaking")
            {
                SceneManager.LoadScene(scene.name);
            }
        }

        if (CheatToggles.panicMode) Utils.Panic();

        var stamp = ModManager.Instance.ModStamp;
        if (stamp) stamp.enabled = !(MalumMenu.inStealthMode || MalumMenu.isPanicked);

        if (CheatToggles.openConfig)
        {
            Utils.OpenConfigFile();
            CheatToggles.openConfig = false;
        }

        if (CheatToggles.reloadConfig)
        {
            MalumMenu.Plugin.Config.Reload();
            CheatToggles.reloadConfig = false;
        }

        if (CheatToggles.saveProfile)
        {
            CheatToggles.saveProfile = false; // Disable first to avoid saving it to profile
            CheatToggles.SaveTogglesToProfile();
        }

        if (CheatToggles.loadProfile)
        {
            CheatToggles.LoadTogglesFromProfile();
            CheatToggles.loadProfile = false;
        }

        // Some cheats only work if the LocalPlayer exists, so they are turned off if it does not
        if(!Utils.isPlayer)
        {
            CheatToggles.setFakeRole = false;
            CheatToggles.setFakeAlive = false;
            CheatToggles.killAll = false;
            CheatToggles.telekillPlayer = false;
            CheatToggles.killAllCrew = false;
            CheatToggles.killAllImps = false;
            CheatToggles.teleportPlayer = false;
            CheatToggles.spectate = false;
            CheatToggles.freecam = false;
            CheatToggles.killPlayer = false;
            CheatToggles.callMeeting = false;

            if (CheatToggles.runOverload)
            {
                OverloadUI.StopOverload();
            }
        }

        // Some cheats only work if the ship exists, so they are turned off if it does not
        if(!Utils.isShip)
        {
            CheatToggles.sabotageMap = false;
            CheatToggles.unfixableLights = false;
            CheatToggles.completeMyTasks = false;
            CheatToggles.kickVents = false;
            CheatToggles.reportBody = false;
            CheatToggles.closeMeeting = false;
            CheatToggles.reactorSab = false;
            CheatToggles.oxygenSab = false;
            CheatToggles.commsSab = false;
            CheatToggles.elecSab = false;
            CheatToggles.mushSab = false;
            CheatToggles.closeAllDoors = false;
            CheatToggles.openAllDoors = false;
            CheatToggles.spamCloseAllDoors = false;
            CheatToggles.spamOpenAllDoors = false;
            CheatToggles.mushSpore = false;

            MalumCheats.StopShipAnimCheats();
        }

        if(!Utils.isHost && !Utils.isFreePlay)
        {
            CheatToggles.killAll = false;
            CheatToggles.telekillPlayer = false;
            CheatToggles.killAllCrew = false;
            CheatToggles.killAllImps = false;
            CheatToggles.killPlayer = false;
            CheatToggles.ejectPlayer = false;
            CheatToggles.noKillCd = false;
            CheatToggles.killAnyone = false;
            CheatToggles.killVanished = false;
            CheatToggles.forceStartGame = false;
            CheatToggles.skipMeeting = false;
            CheatToggles.voteImmune = false;
            CheatToggles.noGameEnd = false;
            CheatToggles.showProtectMenu = false;
            CheatToggles.showRolesMenu = false;
            CheatToggles.noOptionsLimits = false;
            CheatToggles.unfixableLights = false;
            CheatToggles.kickVents = false;
        }

        // Some cheats only work if in a meeting, so they are turned off if it does not
        if (!Utils.isMeeting)
        {
            CheatToggles.skipMeeting = false;
            CheatToggles.ejectPlayer = false;
        }
    }

    public void OnGUI()
    {
        if (!isGUIActive || MalumMenu.isPanicked) return;

        InitStyles();

        UIHelpers.ApplyUIColor();

        // Keep navigation and the close button visible after dragging or a
        // window-size change. The native GUILayout may resize the window.
        windowRect.height = Mathf.Min(windowHeight, Mathf.Max(140f, Screen.height - 12f));
        windowRect.x = Mathf.Clamp(windowRect.x, 0f, Mathf.Max(0f, Screen.width - windowRect.width));
        windowRect.y = Mathf.Clamp(windowRect.y, 0f, Mathf.Max(0f, Screen.height - windowRect.height));

        windowRect = GUI.Window((int)WindowId.MenuUI, windowRect, (GUI.WindowFunction)WindowFunction, ModBranding.Name + " v" + MalumMenu.malumVersion);
    }

    public void WindowFunction(int windowID)
    {
        GUILayout.Label(Utils.isFreePlay ? "Practice | Role experiments are in Practice."
            : Utils.isHost ? "Hosting | Shared game controls are in Host Only."
            : Utils.isLobby || Utils.isInGame ? "Guest | Shared game controls need host support."
            : "Player tools work locally | Create a lobby to use host controls.");
        float contentHeight = Mathf.Max(60f, windowRect.height - 100f);
        GUILayout.BeginHorizontal();

        // Left tab selector (15% width)
        _tabScrollPosition = GUILayout.BeginScrollView(_tabScrollPosition, false, false,
            GUILayout.Width(windowWidth * 0.15f + 15f), GUILayout.Height(contentHeight));
        GUILayout.BeginVertical(GUILayout.Width(windowWidth * 0.15f));
        for (var i = 0; i < _tabs.Count; i++)
        {
            Color standardColor = GUI.backgroundColor;

            if (_selectedTab == i)
            {
                GUI.backgroundColor = new Color(0.2f, 0.2f, 0.2f);
            }

            if (GUILayout.Button(_tabs[i].name, GUIStylePreset.TabButton, GUILayout.Height(35)))
            {
                _selectedTab = i;
                _contentScrollPosition = Vector2.zero;
                NicknameEntry.StopEditing();
            }

            GUI.backgroundColor = standardColor;

        }
        GUILayout.EndVertical();
        GUILayout.EndScrollView();

        // Vertical separator line + invisible space to create gap between the tab selector and the content
        GUILayout.Box("", GUIStylePreset.Separator, GUILayout.Width(1f), GUILayout.ExpandHeight(true));
        GUILayout.Space(10f);

        // Right tab content and controls (85% width)
        _contentScrollPosition = GUILayout.BeginScrollView(_contentScrollPosition, false, true,
            GUILayout.Width(windowWidth - 150f), GUILayout.Height(contentHeight));
        GUILayout.BeginVertical(GUILayout.Width(windowWidth - 170f));

        // Tab-specific content
        if (_selectedTab >= 0 && _selectedTab < _tabs.Count)
        {
            GUILayout.Label(_tabs[_selectedTab].name, GUIStylePreset.TabTitle);
            _tabs[_selectedTab].Draw();
        }

        GUILayout.EndVertical();
        GUILayout.EndScrollView();

        GUILayout.EndHorizontal();

        if (GUILayout.Button("Close menu (Delete)", GUILayout.Width(190f)))
        {
            isGUIActive = false;
            NicknameEntry.StopEditing();
        }

        // Make the window draggable
        GUI.DragWindow();
    }
}
