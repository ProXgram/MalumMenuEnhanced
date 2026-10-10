using System;
using UnityEngine;

namespace MalumMenu;

public static class SprintHandler
{
    public const float MaximumMultiplier = 8f;
    public const float MaximumSpeed = 40f;

    private static PlayerPhysics _physics;
    private static IntPtr _playerPointer;
    private static IntPtr _shipPointer;
    private static float _baseSpeed;
    private static string _statusText = "Sprint: hold the sprint key";

    public static bool IsSprinting => _physics;
    public static string StatusText => _statusText;

    public static string Indicator => CheatToggles.sprint && !MalumMenu.isPanicked &&
        Input.GetKey(Utils.StringToKeycode(MalumMenu.sprintKeybind.Value)) ? _statusText : "";

    public static void Tick()
    {
        try
        {
            var local = PlayerControl.LocalPlayer;
            var ship = ShipStatus.Instance;
            var physics = local ? local.MyPhysics : null;
            var shipPointer = ship ? ship.Pointer : IntPtr.Zero;

            if (_physics && (!local || !physics ||
                _playerPointer != local.Pointer || _shipPointer != shipPointer ||
                _physics.Pointer != physics.Pointer))
                Reset();

            var key = Utils.StringToKeycode(MalumMenu.sprintKeybind.Value);
            if (!CheatToggles.sprint || MalumMenu.isPanicked || MovementAutomation.Active || !Application.isFocused ||
                MenuUI.isGUIActive || key == KeyCode.None ||
                !Utils.isClient || (!Utils.isInGame && !Utils.isFreePlay && !Utils.isLobby) ||
                !local || (!ship && !Utils.isLobby) || !physics || !local.AmOwner || local.Data == null ||
                local.Data.IsDead || local.Data.Disconnected || !local.CanMove ||
                local.inVent || local.onLadder || local.inMovingPlat ||
                Utils.isMeeting || Utils.isExiling || !HudManager.InstanceExists)
            {
                Reset();
                _statusText = MenuUI.isGUIActive ? "Sprint: close the mod menu" :
                    !Application.isFocused ? "Sprint: focus the game" :
                    Utils.isMeeting || Utils.isExiling ? "Sprint: paused during meeting" :
                    HudManager.InstanceExists && HudManager.Instance.Chat && HudManager.Instance.Chat.IsOpenOrOpening
                        ? "Sprint: close chat" :
                    local && local.Data != null && local.Data.IsDead ? "Sprint: living players only" :
                    "Sprint: movement is paused";
                return;
            }

            var hud = HudManager.Instance;
            if (!hud || hud.IsIntroDisplayed || (hud.Chat && hud.Chat.IsOpenOrOpening))
            {
                Reset();
                _statusText = hud && hud.Chat && hud.Chat.IsOpenOrOpening
                    ? "Sprint: close chat" : "Sprint: waiting for gameplay";
                return;
            }

            if (!Input.GetKey(key))
            {
                Reset();
                _statusText = "Sprint: hold " + MalumMenu.sprintKeybind.Value;
                return;
            }

            if (!_physics)
            {
                if (float.IsNaN(physics.Speed) || float.IsInfinity(physics.Speed)) return;
                _physics = physics;
                _playerPointer = local.Pointer;
                _shipPointer = shipPointer;
                _baseSpeed = physics.Speed;
            }

            var multiplier = MalumMenu.sprintMultiplier.Value;
            if (float.IsNaN(multiplier) || float.IsInfinity(multiplier)) multiplier = 2f;
            multiplier = Mathf.Clamp(multiplier, 1f, MaximumMultiplier);
            // Always multiply the captured speed, never the previously boosted value.
            // Preserve the sign used by Invert Controls within the sprint speed limit.
            physics.Speed = Mathf.Clamp(_baseSpeed * multiplier, -MaximumSpeed, MaximumSpeed);
            _statusText = $"Sprint active: {Mathf.Abs(physics.Speed):0.##} speed";
        }
        catch (Exception exception)
        {
            Reset();
            MalumMenu.Log?.LogWarning("Sprint paused: " + exception.Message);
        }
    }

    public static void Reset()
    {
        var previous = _physics;
        var speed = _baseSpeed;
        _physics = null;
        _playerPointer = IntPtr.Zero;
        _shipPointer = IntPtr.Zero;
        _baseSpeed = 0f;

        // Restore the captured object only, never a replacement player's physics.
        try
        {
            if (previous) previous.Speed = speed;
        }
        catch (Exception exception)
        {
            MalumMenu.Log?.LogWarning("Sprint cleanup: " + exception.Message);
        }
    }
}
