using System;
using System.Collections.Generic;
using AmongUs.GameOptions;
using UnityEngine;

namespace MalumMenu;

// A local ability bundle. The player's real role and native ability button
// remain untouched; no role-assignment or tracking messages are sent.
public static class MultiRoleHandler
{
    private static PlayerControl _target;
    private static VitalsMinigame _vitals;
    private static IntPtr _playerPointer;
    private static IntPtr _shipPointer;
    private static bool _wasEnabled;
    private static string _trackingText = "Choose a player to track.";
    private static string _lastError;
    private static float _nextTrackingUpdate;

    public static bool PanelOpen { get; set; } = true;

    public static bool Active
    {
        get
        {
            var local = PlayerControl.LocalPlayer;
            return CheatToggles.multiRole && !MalumMenu.isPanicked &&
                (Utils.isInGame || Utils.isFreePlay) && ShipStatus.Instance &&
                local && local.AmOwner && local.Data != null && local.Data.Role &&
                !local.Data.IsDead && !local.Data.Disconnected &&
                GameOptionsManager.Instance != null && GameOptionsManager.Instance.CurrentGameOptions != null &&
                Utils.isNormalGame;
        }
    }

    public static bool VentAccess => Active && !Utils.isMeeting && !Utils.isExiling &&
        HudManager.InstanceExists && !HudManager.Instance.IsIntroDisplayed;
    public static bool HasTarget => Active && IsValidTarget(_target);
    public static string TrackingText => _trackingText;
    public static string StatusText => _lastError ?? (!CheatToggles.multiRole
        ? "Multi Role is off."
        : !Active ? "Ready for a living player in a Normal round or Freeplay."
        : "Use the normal Vent button. Your assigned role stays the same; the host checks vent requests.");

    public static bool VitalsAvailable => CanOpenTool && GetVitalsPrefab();
    public static bool DetectiveAvailable => CanOpenTool && MultiRoleDetectiveHandler.Available;
    public static bool CanInterrogate => CanOpenTool && HasTarget &&
        MultiRoleDetectiveHandler.CanInterrogate(_target);
    public static string DetectiveStatusText => MultiRoleDetectiveHandler.StatusText;

    public static bool CanOpenTool => Active && !Utils.isMeeting && !Utils.isExiling &&
        !Minigame.Instance && HudManager.InstanceExists && !HudManager.Instance.IsIntroDisplayed &&
        !(HudManager.Instance.Chat && HudManager.Instance.Chat.IsOpenOrOpening) &&
        PlayerControl.LocalPlayer.CanMove &&
        !PlayerControl.LocalPlayer.inVent && !PlayerControl.LocalPlayer.onLadder &&
        !PlayerControl.LocalPlayer.inMovingPlat;

    public static void SetEnabled(bool enabled)
    {
        CheatToggles.multiRole = enabled;
        ResetSession();
        PanelOpen = true;
        _wasEnabled = enabled;
        _lastError = null;
    }

    public static void Tick()
    {
        try
        {
            if (_wasEnabled != CheatToggles.multiRole)
            {
                ResetSession();
                PanelOpen = true;
                _lastError = null;
                _wasEnabled = CheatToggles.multiRole;
            }
            if (!Active)
            {
                if (_playerPointer != IntPtr.Zero || _shipPointer != IntPtr.Zero || _target || _vitals)
                    ResetSession();
                MultiRoleDetectiveHandler.Tick();
                return;
            }

            var local = PlayerControl.LocalPlayer;
            var ship = ShipStatus.Instance;
            if (_playerPointer != local.Pointer || _shipPointer != ship.Pointer)
            {
                ResetSession();
                _playerPointer = local.Pointer;
                _shipPointer = ship.Pointer;
            }
            if (_vitals && (Utils.isMeeting || Utils.isExiling)) CloseOwnedVitals();
            MultiRoleDetectiveHandler.Tick();

            if (_target && !IsValidTarget(_target))
            {
                ClearTarget();
                _trackingText = "Target unavailable. Choose another player.";
            }
            if (HasTarget && Time.unscaledTime >= _nextTrackingUpdate)
            {
                RefreshTrackingText();
                _nextTrackingUpdate = Time.unscaledTime + 0.2f;
            }
        }
        catch (Exception error)
        {
            SetEnabled(false);
            ReportError("Multi Role stopped: " + error.Message);
        }
    }

    public static void CycleTarget()
    {
        if (!CanOpenTool) return;
        var players = new List<PlayerControl>();
        foreach (var player in PlayerControl.AllPlayerControls)
            if (IsValidTarget(player)) players.Add(player);
        players.Sort((left, right) => left.PlayerId.CompareTo(right.PlayerId));
        if (players.Count == 0)
        {
            ClearTarget();
            _trackingText = "No living players available to track.";
            return;
        }

        int previous = _target ? players.FindIndex(player => player.Pointer == _target.Pointer) : -1;
        _target = players[(previous + 1) % players.Count];
        _lastError = null;
        RefreshTrackingText();
    }

    public static void ClearTarget()
    {
        _target = null;
        _trackingText = "Choose a player to track.";
        _nextTrackingUpdate = 0f;
    }

    public static bool IsTrackedPlayer(PlayerControl player) => HasTarget && player &&
        player.Pointer == _target.Pointer;

    public static void OpenTrackingMap()
    {
        if (!CanOpenTool || !HasTarget) return;
        HudManager.Instance.ToggleMapVisible(new MapOptions { Mode = MapOptions.Modes.Normal });
    }

    public static void OpenVitals()
    {
        if (!CanOpenTool || _vitals) return;
        var prefab = GetVitalsPrefab();
        if (!prefab || !Camera.main)
        {
            ReportError("Vitals are unavailable in this scene.");
            return;
        }
        try
        {
            _vitals = UnityEngine.Object.Instantiate(prefab, Camera.main.transform, false);
            _vitals.transform.localPosition = new Vector3(0f, 0f, -50f);
            _vitals.Begin(null);
            if (_vitals.BatteryText) _vitals.BatteryText.gameObject.SetActive(false);
            _lastError = null;
        }
        catch (Exception error)
        {
            CloseOwnedVitals();
            ReportError("Could not open Vitals: " + error.Message);
        }
    }

    public static void OpenDetectiveNotes()
    {
        if (!DetectiveAvailable) return;
        MultiRoleDetectiveHandler.OpenNotes();
    }

    public static void InterrogateTarget()
    {
        if (!CanInterrogate) return;
        MultiRoleDetectiveHandler.Interrogate(_target);
    }

    private static VitalsMinigame GetVitalsPrefab()
    {
        if (!RoleManager.Instance) return null;
        foreach (var role in RoleManager.Instance.AllRoles)
        {
            if (!role || role.Role != RoleTypes.Scientist) continue;
            var scientist = role.TryCast<ScientistRole>();
            return scientist ? scientist.VitalsPrefab : null;
        }
        return null;
    }

    private static bool IsValidTarget(PlayerControl player) => player &&
        PlayerControl.LocalPlayer && player.Pointer != PlayerControl.LocalPlayer.Pointer &&
        player.Data != null && !player.Data.Disconnected && !player.Data.IsDead;

    private static void RefreshTrackingText()
    {
        if (!HasTarget) return;
        var delta = _target.GetTruePosition() - PlayerControl.LocalPlayer.GetTruePosition();
        var room = Utils.GetRoomFromPosition(_target.GetTruePosition());
        var roomName = room ? room.RoomId.ToString() : "between rooms";
        var direction = Mathf.Abs(delta.x) < 0.5f && Mathf.Abs(delta.y) < 0.5f ? "beside you"
            : Mathf.Abs(delta.x) > Mathf.Abs(delta.y) ? delta.x > 0f ? "right" : "left"
            : delta.y > 0f ? "up" : "down";
        _trackingText = $"{_target.Data.PlayerName}: {roomName}\n{delta.magnitude:0.0}m, {direction}";
    }

    private static void ResetSession()
    {
        ClearTarget();
        CloseOwnedVitals();
        MultiRoleDetectiveHandler.Reset();
        _playerPointer = IntPtr.Zero;
        _shipPointer = IntPtr.Zero;
    }

    private static void CloseOwnedVitals()
    {
        var owned = _vitals;
        _vitals = null;
        if (!owned) return;
        try { owned.ForceClose(); }
        catch { UnityEngine.Object.Destroy(owned.gameObject); }
    }

    private static void ReportError(string message)
    {
        if (_lastError != message) MalumMenu.Log?.LogWarning(message);
        _lastError = message;
    }
}
