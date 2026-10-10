using System.IO;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using UnityEngine.SceneManagement;
using System;
using UnityEngine;
using UnityEngine.Analytics;
using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using MalumMenuEnhanced.Updates;

namespace MalumMenu;

[BepInPlugin(Id, ModBranding.Name, ModBranding.Version)]
[BepInProcess("Among Us.exe")]
public partial class MalumMenu : BasePlugin
{
    // Keep the existing plugin/config identity when changing this edition's name.
    public const string Id = "MalumMenu";
    public const string Name = ModBranding.Name;
    public const string Version = ModBranding.Version;
    public Harmony Harmony { get; } = new(Id);
    public static MalumMenu Plugin;
    public new static ManualLogSource Log;
    public static readonly string ProfilePath = Path.Combine(Paths.ConfigPath, "MalumProfile.txt");

    public static MenuUI menuUI;
    public static ConsoleUI consoleUI;
    public static RolesUI rolesUI;
    public static OverloadUI overloadUI;
    public static DoorsUI doorsUI;
    public static TasksUI tasksUI;
    public static ProtectUI protectUI;
    public static MultiRoleUI multiRoleUI;
    public static KeybindListener keybindListener;

    public static string malumVersion = ModBranding.Version;
    public static List<string> supportedAU = new List<string> { "2026.9.29", "19.0.0" };
    public static bool isPanicked = false;
    public static bool inStealthMode = false;

    public static ConfigEntry<string> menuKeybind;
    public static ConfigEntry<string> sprintKeybind;
    public static ConfigEntry<float> sprintMultiplier;
    public static ConfigEntry<float> stuntMultiplier;
    public static ConfigEntry<float> targetMovementMultiplier;
    public static ConfigEntry<float> yoyoInterval;
    public static ConfigEntry<float> lagInterval;
    public static ConfigEntry<float> lagJumpDistance;
    public static ConfigEntry<float> rainbowColorInterval;
    public static ConfigEntry<float> lobbyColorInterval;
    public static ConfigEntry<string> menuHtmlColor;
    public static ConfigEntry<bool> menuOpenOnMouse;
    public static ConfigEntry<bool> menuKeepSubwindowsOpen;
    public static ConfigEntry<bool> menuAllowClickThrough;
    public static ConfigEntry<string> spoofLevel;
    public static ConfigEntry<string> spoofPlatform;
    public static ConfigEntry<bool> spoofDeviceId;
    public static ConfigEntry<bool> noTelemetry;
    public static ConfigEntry<string> guestFriendCode;
    public static ConfigEntry<bool> guestMode;
    public static ConfigEntry<bool> autoLoadProfile;
    public static ConfigEntry<string> configEditor;
    public static ConfigEntry<int> adaptMaxStrength;
    public static ConfigEntry<float> adaptMaxCooldown;
    public static ConfigEntry<float> attackLogDelay;
    public static ConfigEntry<int> defaultStrength;
    public static ConfigEntry<float> defaultCooldown;
    public static ConfigEntry<int> killSwitchLvl;
    public static ConfigEntry<bool> automaticUpdates;

    public override void Load()
    {
        Log = base.Log;
        Plugin = this;
        automaticUpdates = Config.Bind("MalumMenu.Updates", "AutomaticUpdates", true,
            "Check for signed mod updates when the game starts. A compatible update is applied after you close Among Us normally.");
        // Capture Unity-owned values here; background update work receives only managed values.
        var gameVersion = Application.version;
        var gameDirectory = Paths.GameRootPath;
        int gameProcessId;
        using (var process = System.Diagnostics.Process.GetCurrentProcess()) gameProcessId = process.Id;
        AutomaticUpdateHandler.Start(new UpdateStartupSnapshot(gameDirectory, gameVersion, ModBranding.Version, gameProcessId),
            automaticUpdates.Value, message => Log?.LogInfo(message));
        if (!supportedAU.Contains(gameVersion))
        {
            Log.LogWarning("This Among Us version is not supported by the installed mod. Gameplay features are paused. " +
                (automaticUpdates.Value ? "A compatible published update can be prepared automatically; close Among Us normally to apply it."
                    : "Automatic updates are disabled. Use the latest installer for a compatible mod release."));
            return;
        }
        NavigationRouter.FailureTrace = message => Log?.LogInfo("Movement route failed: " + message);

        // Loads config settings
        menuKeybind = Config.Bind("MalumMenu.GUI",
                                "Keybind",
                                "Delete",
                                "The keyboard key used to toggle the GUI on and off. List of supported keycodes: https://docs.unity3d.com/Packages/com.unity.tiny@0.16/api/Unity.Tiny.Input.KeyCode.html");

        sprintKeybind = Config.Bind("MalumMenu.Movement",
                                "SprintKey",
                                "LeftShift",
                                "Hold this key to sprint in lobbies or rounds. Sprint pauses in menus, chat, meetings and when the game loses focus.");

        sprintMultiplier = Config.Bind("MalumMenu.Movement",
                                "SprintMultiplier",
                                2f,
                                new ConfigDescription("Sprint multiplies your chosen movement speed while the key is held.",
                                    new AcceptableValueRange<float>(1f, SprintHandler.MaximumMultiplier)));

        stuntMultiplier = Config.Bind("MalumMenu.Movement",
                                "StuntMultiplier",
                                3f,
                                new ConfigDescription("Temporary speed multiplier for Zigzag Dash.",
                                    new AcceptableValueRange<float>(1f, 4f)));

        targetMovementMultiplier = Config.Bind("MalumMenu.Movement",
                                "TargetMovementMultiplier",
                                3f,
                                new ConfigDescription("Follow Player and Turbo Orbit speed. Distant targets use at least 2x for catch-up; walking remains collision checked.",
                                    new AcceptableValueRange<float>(1f, 4f)));

        yoyoInterval = Config.Bind("MalumMenu.Movement",
                                "YoYoInterval",
                                1.5f,
                                new ConfigDescription("Seconds between Teleport Yo-Yo jumps. Runs at most 12 jumps per activation.",
                                    new AcceptableValueRange<float>(1f, 5f)));

        lagInterval = Config.Bind("MalumMenu.Movement",
                                "LagInterval",
                                0.75f,
                                new ConfigDescription("Base time between Lag Mode snaps. Normal walking alternates with brief stationary teleport bursts; timing varies slightly.",
                                    new AcceptableValueRange<float>(0.5f, 2f)));

        lagJumpDistance = Config.Bind("MalumMenu.Movement",
                                "LagJumpDistance",
                                1.25f,
                                new ConfigDescription("Maximum Lag Mode forward or backward correction distance. Every correction checks the ground path.",
                                    new AcceptableValueRange<float>(0.25f, 2f)));

        rainbowColorInterval = Config.Bind("MalumMenu.Appearance",
                                "RainbowColorInterval",
                                0.6f,
                                new ConfigDescription("Seconds between body colors in the local rainbow preview.",
                                    new AcceptableValueRange<float>(0.15f, 3f)));

        lobbyColorInterval = Config.Bind("MalumMenu.Appearance",
                                "LobbyColorInterval",
                                3f,
                                new ConfigDescription("Seconds between normal own-player color requests in a lobby.",
                                    new AcceptableValueRange<float>(2f, 10f)));

        menuHtmlColor = Config.Bind("MalumMenu.GUI",
                                "Color",
                                "",
                                $"A custom color for your {ModBranding.Name} GUI. Supports html color codes");

        menuOpenOnMouse = Config.Bind("MalumMenu.GUI",
                                "OpenOnMouse",
                                false,
                                $"When enabled, the {ModBranding.Name} GUI will always be opened at the current mouse position");

        menuKeepSubwindowsOpen = Config.Bind("MalumMenu.GUI",
                                "KeepSubwindowsOpen",
                                false,
                                $"When enabled, closing the {ModBranding.Name} GUI will not automatically close its subwindows");

        menuAllowClickThrough = Config.Bind("MalumMenu.GUI",
                                "AllowClicksThrough",
                                true,
                                $"When enabled, clicks pass through the {ModBranding.Name} GUI, letting you interact with Among Us GUI elements behind it");

        autoLoadProfile = Config.Bind("MalumMenu.Profile",
                                "AutoLoadProfile",
                                false,
                                "When enabled, your saved keybind and toggle profile will be automatically loaded at game startup");

        configEditor = Config.Bind("MalumMenu.Config",
                                "ConfigEditor",
                                "notepad.exe",
                                "The program used to open the config file when using the Open Config toggle. Can be any executable, but using a text editor is recommended");

        // GuestMode config settings are commented out as the cheats are broken in latest updates

        // guestMode = Config.Bind("MalumMenu.GuestMode",
        //                         "GuestMode",
        //                         false,
        //                         "When enabled, a new guest account will generate every time you start the game, allowing you to bypass account bans and PUID detection");

        // guestFriendCode = Config.Bind("MalumMenu.GuestMode",
        //                         "FriendName",
        //                         "",
        //                         "The username that will be used when setting a friend code for your guest account. IMPORTANT: Can only be used with GuestMode, needs to be ≤ 10 characters, and cannot include special characters/discriminator (#1234)");

        spoofLevel = Config.Bind("MalumMenu.Spoofing",
                                "Level",
                                "",
                                "A custom player level to display to others in online games to hide your actual platform. IMPORTANT: Custom levels can only be within 1 and 100001. Decimal numbers will not work");

        spoofPlatform = Config.Bind("MalumMenu.Spoofing",
                                "Platform",
                                "",
                                "A custom gaming platform to display to others in online lobbies to hide your actual platform. List of supported platforms: https://skeld.js.org/enums/_skeldjs_constant.Platform.html");

        spoofDeviceId = Config.Bind("MalumMenu.Privacy",
                                "HideDeviceId",
                                true,
                                "When enabled, it will hide your unique deviceId from Among Us, which could potentially help bypass hardware bans in the future");

        noTelemetry = Config.Bind("MalumMenu.Privacy",
                                "NoTelemetry",
                                true,
                                "When enabled, it will stop Among Us from collecting analytics of your games and sending them to Innersloth using Unity Analytics");

        // adaptMaxStrength = Config.Bind("MalumMenu.Overload",
        //                         "AdaptMaxStrength",
        //                         18000,
        //                         new ConfigDescription(
        //                             "Maximum total number of RPCs sent during one overload cycle in AutoAdapt mode. Automatically divided between targets and reduced based on ping. IMPORTANT: Only goes from 1 to 100K RPCs",
        //                             new AcceptableValueRange<int>(1, 100000)
        //                         ));

        // adaptMaxCooldown = Config.Bind("MalumMenu.Overload",
        //                         "AdaptMaxCooldown",
        //                         1f,
        //                         new ConfigDescription(
        //                             "Maximum time (in seconds) for one full overload cycle to complete in AutoAdapt mode. Automatically distributed across targets (more targets = shorter delay per target). IMPORTANT: Only goes from 0s to 10s",
        //                             new AcceptableValueRange<float>(0f, 10f)
        //                         ));

        // attackLogDelay = Config.Bind("MalumMenu.Overload",
        //                         "AttackLogDelay",
        //                         2f,
        //                         "Minimum time (in seconds) between attack logs in normal (non-verbose) mode");

        // defaultStrength = Config.Bind("MalumMenu.Overload",
        //                         "DefaultStrength",
        //                         18000,
        //                         new ConfigDescription(
        //                             "Default number of malformed RPCs sent to each target during an overload cycle. Overridden if AutoAdapt mode is enabled. IMPORTANT: Only goes from 1 to 100K RPCs",
        //                             new AcceptableValueRange<int>(1, 100000)
        //                         ));

        // defaultCooldown = Config.Bind("MalumMenu.Overload",
        //                         "DefaultCooldown",
        //                         1f,
        //                         new ConfigDescription(
        //                             "Default cooldown (in seconds) between each target during an overload cycle. Overridden if AutoAdapt mode is enabled. IMPORTANT: Only goes from 0s to 10s",
        //                             new AcceptableValueRange<float>(0f, 10f)
        //                         ));

        // killSwitchLvl = Config.Bind("MalumMenu.Overload",
        //                         "DefaultKillSwitchLevel",
        //                         1,
        //                         new ConfigDescription(
        //                             "Default level used by kill switch. Each level adds 500 ms to the max allowed ping before overload stops. Helps avoid lagging / disconnects. IMPORTANT: Only goes from level 1 (500 ms) to 6 (3000 ms)",
        //                             new AcceptableValueRange<int>(1, 6)
        //                         ));

        // Enabled by default
        CheatToggles.unlockFeatures = true;
        CheatToggles.freeCosmetics = true;
        CheatToggles.avoidPenalties = true;

        // Enabled by default
        CheatToggles.olAutoAdapt = true;
        CheatToggles.olKillSwitch = true;
        CheatToggles.olAutoStop = true;
        CheatToggles.olAutoClear = true;
        CheatToggles.olLogStartStop = true;
        CheatToggles.olLogAttack = true;
        CheatToggles.olLogAddRemove = true;
        CheatToggles.olLogDisconnect = true;

        Harmony.PatchAll();

        // UI
        menuUI = AddComponent<MenuUI>();
        consoleUI = AddComponent<ConsoleUI>();
        doorsUI = AddComponent<DoorsUI>();
        tasksUI = AddComponent<TasksUI>();
        protectUI = AddComponent<ProtectUI>();
        multiRoleUI = AddComponent<MultiRoleUI>();
        // overloadUI = AddComponent<OverloadUI>();
        // rolesUI = AddComponent<RolesUI>();

        // Components
        keybindListener = AddComponent<KeybindListener>();

        // Disables Telemetry (haven't fully tested if it works, but according to Unity docs it should)
        if (noTelemetry.Value)
        {
            Analytics.enabled = false;
            Analytics.deviceStatsEnabled = false;
            PerformanceReporting.enabled = false;
        }

        // Create profile file if it is missing
        if (!File.Exists(ProfilePath))
        {
            CheatToggles.SaveTogglesToProfile();
        }

        // Auto load profile on start if needed
        if (autoLoadProfile.Value)
        {
            CheatToggles.LoadTogglesFromProfile();
        }
        CheatToggles.automaticTasks = false;
        AutomaticTasksHandler.Reset();

        SceneManager.add_sceneLoaded((Action<Scene, LoadSceneMode>) ((scene, _) =>
        {
            if (scene.name == "MainMenu" && !(inStealthMode || isPanicked))
            {
                // Warns about unsupported AU versions
                if (!supportedAU.Contains(Application.version))
                {
                    Utils.ShowPopup($"\nThis version of {ModBranding.Name} and this version of Among Us are incompatible\n\nInstall the right version to avoid problems");
                }
            }
        }));
    }
}
