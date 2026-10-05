using System;
using System.Collections.Generic;
using AmongUs.GameOptions;

namespace MalumMenu;

public static class AlwaysImpostorHandler
{
    private static IntPtr _freeplayPlayer;
    private static bool _freeplayAttempted;
    private static bool _freeplayFailed;
    private static string _lastHostedResult = "Armed for the next hosted round.";

    private static bool IsNormalMode => GameOptionsManager.Instance != null &&
        GameOptionsManager.Instance.CurrentGameOptions != null && Utils.isNormalGame;

    public static string StatusText
    {
        get
        {
            if (!Utils.isClient) return "Ready: host / Freeplay, or local-only as a guest.";
            if (!IsNormalMode) return "Available in Normal mode only.";
            if (!Utils.isFreePlay && !Utils.isHost) return LocalImpostorHandler.StatusText;

            var local = PlayerControl.LocalPlayer;
            if (local && local.Data != null)
            {
                if (local.Data.IsDead) return "Inactive while dead; no revival.";
                if (!Utils.isLobby && local.Data.Role && local.Data.Role.TeamType == RoleTeamTypes.Impostor)
                    return "Active: " + local.Data.RoleType;
            }

            if (Utils.isFreePlay) return _freeplayFailed
                ? "Assignment failed; toggle off/on to retry."
                : "Waiting for your practice player.";
            return Utils.isLobby ? "Armed for the next hosted round." : _lastHostedResult;
        }
    }

    public static void ResetFreeplayAttempt()
    {
        _freeplayPlayer = IntPtr.Zero;
        _freeplayAttempted = false;
        _freeplayFailed = false;
        _lastHostedResult = "Armed for the next hosted round.";
    }

    // Practice assignment is separate from the guest's local-only presentation.
    public static void TickFreeplay()
    {
        if (!CheatToggles.alwaysImpostor || !Utils.isFreePlay)
        {
            _freeplayPlayer = IntPtr.Zero;
            _freeplayAttempted = false;
            _freeplayFailed = false;
            return;
        }

        var local = PlayerControl.LocalPlayer;
        // Freeplay has a loaded ship and player but does not use the online
        // client's Started state. Wait for the practice scene instead.
        if (!IsNormalMode || !ShipStatus.Instance || !local || local.Data == null ||
            local.Data.IsDead || local.Data.Disconnected || !local.Data.Role)
            return;

        if (_freeplayAttempted && _freeplayPlayer == local.Pointer) return;
        _freeplayPlayer = local.Pointer;
        _freeplayAttempted = true;

        if (local.Data.Role.TeamType == RoleTeamTypes.Impostor) return;

        try
        {
            CheatToggles.setFakeRole = false;
            RoleManager.Instance.SetRole(local, RoleTypes.Impostor);
            MalumMenu.Log.LogInfo("Always Impostor: assigned Impostor in Freeplay.");
        }
        catch (Exception error)
        {
            _freeplayFailed = true;
            MalumMenu.Log.LogError("Always Impostor: Freeplay assignment failed: " + error);
        }
    }

    // Run once at the normal round's intro, after all original roles exist.
    // Swap the exact roles so special-role and impostor counts stay unchanged.
    public static void AssignHostedRole()
    {
        var local = PlayerControl.LocalPlayer;
        if (!CheatToggles.alwaysImpostor || !Utils.isHost || Utils.isFreePlay ||
            !IsNormalMode || !local || local.Data == null || !local.Data.Role ||
            local.Data.IsDead || local.Data.Disconnected)
            return;

        if (local.Data.Role.TeamType == RoleTeamTypes.Impostor)
        {
            _lastHostedResult = "Active: " + local.Data.RoleType;
            return;
        }

        var slots = new List<AlwaysImpostorPlanner.RoleSlot>();
        var controls = new Dictionary<byte, PlayerControl>();
        foreach (var player in PlayerControl.AllPlayerControls)
        {
            if (!player || player.Data == null || !player.Data.Role) continue;
            var data = player.Data;
            slots.Add(new AlwaysImpostorPlanner.RoleSlot(data.PlayerId, (ushort)data.RoleType,
                data.Role.TeamType == RoleTeamTypes.Impostor, !data.IsDead, !data.Disconnected));
            controls[data.PlayerId] = player;
        }

        if (!AlwaysImpostorPlanner.TryPlan(true, true, true, local.PlayerId, slots, out var swap) ||
            !controls.TryGetValue(swap.DonorPlayerId, out var donor))
        {
            _lastHostedResult = "No living impostor available to swap.";
            MalumMenu.Log.LogWarning("Always Impostor: no valid role swap; original roles retained.");
            return;
        }

        var originalLocalRole = (RoleTypes)swap.LocalOriginalRole;
        var originalDonorRole = (RoleTypes)swap.DonorOriginalRole;
        try
        {
            CheatToggles.setFakeRole = false;
            ApplyNetworkRole(donor, originalLocalRole);
            ApplyNetworkRole(local, originalDonorRole);
            _lastHostedResult = "Active: " + originalDonorRole;
            MalumMenu.Log.LogInfo("Always Impostor: swapped assigned roles for the hosted round.");
        }
        catch (Exception error)
        {
            _lastHostedResult = "Assignment failed; see Console log.";
            MalumMenu.Log.LogError("Always Impostor: role swap failed: " + error);
            try
            {
                ApplyNetworkRole(donor, originalDonorRole);
                ApplyNetworkRole(local, originalLocalRole);
            }
            catch (Exception rollbackError)
            {
                MalumMenu.Log.LogError("Always Impostor: role rollback failed: " + rollbackError);
            }
        }
    }

    private static void ApplyNetworkRole(PlayerControl player, RoleTypes role)
    {
        player.RpcSetRole(role, true);
        // The RPC's local role setter can be a coroutine. Ensure the host's intro
        // sees the new role immediately; the RPC keeps the other clients in sync.
        if (!player.Data.Role || player.Data.RoleType != role)
            RoleManager.Instance.SetRole(player, role);
    }
}
