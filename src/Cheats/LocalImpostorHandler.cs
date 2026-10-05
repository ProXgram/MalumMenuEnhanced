using System;
using AmongUs.GameOptions;

namespace MalumMenu;

// This changes only the local game's role presentation. The host retains its
// original role assignment and can reject actions requested by this client.
public static class LocalImpostorHandler
{
    private static bool _roundReady;
    private static IntPtr _playerPointer;
    private static RoleTypes? _originalRole;
    private static IntPtr _appliedRolePointer;
    private static bool _changedLocally;
    private static bool _attempted;
    private static bool _restoreAttempted;
    private static bool _enabledLastTick;
    private static bool _diedThisRound;
    private static bool _failed;
    private static bool _blockedKillReported;
    private const string LocalRoleStatus = "Local-only Impostor; kills disabled.";
    private static string _lastResult = "Local only: armed for the next round.";

    private static bool IsNormalMode => GameOptionsManager.Instance != null &&
        GameOptionsManager.Instance.CurrentGameOptions != null && Utils.isNormalGame;

    // Ownership of our local change, not a claim about the host's current role.
    // Do not use the toggle here: a failed restoration can leave our role applied.
    public static bool BlocksKillRequests(PlayerControl actor)
    {
        if (!_roundReady || !_changedLocally || !Utils.isClient ||
            !Utils.isInGame || Utils.isHost || Utils.isFreePlay)
            return false;

        var local = PlayerControl.LocalPlayer;
        return actor && local && actor.Pointer == local.Pointer &&
            _playerPointer == local.Pointer && local.Data != null &&
            IsOurAppliedRole(local);
    }

    public static bool BlockOwnKill(PlayerControl actor)
    {
        if (!BlocksKillRequests(actor)) return false;
        if (!_blockedKillReported)
        {
            _blockedKillReported = true;
            const string message = "Kill blocked: your Impostor role is local only. " +
                "Accepted kills require a real assigned Impostor role.";
            MalumMenu.Log.LogWarning(message);
            ConsoleUI.Log(message);
        }
        return true;
    }

    public static string StatusText
    {
        get
        {
            if (!Utils.isClient || Utils.isLobby)
                return "Local only: armed for the next round.";
            if (!IsNormalMode) return "Local-only mode requires Normal mode.";

            var local = PlayerControl.LocalPlayer;
            if (local && local.Data != null &&
                (local.Data.IsDead || (_playerPointer == local.Pointer && _diedThisRound)))
                return "Inactive after death; no revival or role restoration.";

            if (!CheatToggles.alwaysImpostor)
                return _failed ? _lastResult : "Local-only mode disabled.";
            if (!_roundReady || !Utils.isInGame || !local || local.Data == null ||
                local.Data.Disconnected || !local.Data.Role)
                return "Local only: waiting for your round's assigned role.";
            if (_attempted && !_failed && _changedLocally && !IsOurAppliedRole(local))
                return "Local-only role replaced; no automatic retry.";
            return _lastResult;
        }
    }

    public static void OnRoundIntro()
    {
        Reset();
        _roundReady = true;
    }

    // Do not restore here: the next round's native role assignment owns roles.
    public static void Reset()
    {
        _roundReady = false;
        _playerPointer = IntPtr.Zero;
        ClearPlayerAttempt();
        _enabledLastTick = false;
        _diedThisRound = false;
        _lastResult = "Local only: armed for the next round.";
    }

    public static void Tick()
    {
        if (!_roundReady || Utils.isFreePlay || !Utils.isInGame || !IsNormalMode)
            return;

        var local = PlayerControl.LocalPlayer;
        if (!local || local.Data == null || local.Data.Disconnected) return;

        // A guest can become host when the previous host leaves. Undo only our
        // living local change before handing authority back to the host path.
        if (Utils.isHost)
        {
            if (_enabledLastTick != CheatToggles.alwaysImpostor)
            {
                _enabledLastTick = CheatToggles.alwaysImpostor;
                _restoreAttempted = false;
            }
            if (_playerPointer == local.Pointer && !local.Data.IsDead &&
                !_diedThisRound && local.Data.Role)
                RestoreOriginalRole(local);
            if (local.Data.IsDead || _diedThisRound)
            {
                _diedThisRound = true;
                _changedLocally = false;
            }
            if (!_changedLocally)
            {
                ClearPlayerAttempt();
                _roundReady = false;
            }
            return;
        }

        if (_playerPointer != local.Pointer)
        {
            ClearPlayerAttempt();
            _playerPointer = local.Pointer;
            _enabledLastTick = CheatToggles.alwaysImpostor;
            _diedThisRound = false;
        }

        // Remember death even if another setting later changes the alive flag.
        // Never set a living role, or restore a saved one, after this point.
        if (local.Data.IsDead || _diedThisRound)
        {
            _diedThisRound = true;
            _changedLocally = false;
            _originalRole = null;
            _lastResult = "Inactive after death; no revival or role restoration.";
            return;
        }
        if (!local.Data.Role) return;

        if (_enabledLastTick != CheatToggles.alwaysImpostor)
        {
            _enabledLastTick = CheatToggles.alwaysImpostor;
            _attempted = false;
            _restoreAttempted = false;
            _failed = false;
        }

        if (!CheatToggles.alwaysImpostor)
        {
            RestoreOriginalRole(local);
            return;
        }
        if (_attempted) return;
        _attempted = true;

        // A prior failed restoration can leave our own local role applied.
        // Keep its original snapshot so a later disable can still restore it.
        if (_changedLocally && IsOurAppliedRole(local))
        {
            _lastResult = LocalRoleStatus;
            return;
        }

        _changedLocally = false;
        _originalRole = null;
        _appliedRolePointer = IntPtr.Zero;

        // Retain the host's assigned Impostor, Shapeshifter, Phantom, Viper,
        // or any future special impostor role instead of replacing it.
        if (local.Data.Role.TeamType == RoleTeamTypes.Impostor)
        {
            _lastResult = "Host-assigned " + local.Data.RoleType + "; role unchanged.";
            return;
        }

        _originalRole = local.Data.RoleType;
        try
        {
            CancelPendingRolePicker();
            RoleManager.Instance.SetRole(local, RoleTypes.Impostor);
            if (!local.Data.Role || local.Data.RoleType != RoleTypes.Impostor)
                throw new InvalidOperationException("The local native role setter did not apply Impostor.");

            _appliedRolePointer = local.Data.Role.Pointer;
            _changedLocally = true;
            _blockedKillReported = false;
            _lastResult = LocalRoleStatus;
            MalumMenu.Log.LogInfo("Always Impostor: applied local-only role; host assignment is unchanged.");
        }
        catch (Exception error)
        {
            _failed = true;
            _lastResult = "Local-only assignment failed; toggle off/on to retry.";
            MalumMenu.Log.LogError("Always Impostor: local-only assignment failed: " + error);

            // Keep enough information to undo a partial native assignment.
            try
            {
                if (local && local.Data != null && local.Data.Role &&
                    local.Data.RoleType == RoleTypes.Impostor)
                {
                    _appliedRolePointer = local.Data.Role.Pointer;
                    _changedLocally = true;
                }
            }
            catch { }
        }
    }

    private static bool IsOurAppliedRole(PlayerControl local) =>
        local.Data.Role && local.Data.RoleType == RoleTypes.Impostor &&
        local.Data.Role.Pointer == _appliedRolePointer;

    private static void RestoreOriginalRole(PlayerControl local)
    {
        if (!_changedLocally || !_originalRole.HasValue || _restoreAttempted) return;
        _restoreAttempted = true;

        // Preserve any role assigned later by the host or another menu setting.
        if (!IsOurAppliedRole(local))
        {
            _changedLocally = false;
            _originalRole = null;
            _lastResult = "Local-only mode disabled; current role retained.";
            return;
        }

        try
        {
            RoleManager.Instance.SetRole(local, _originalRole.Value);
            if (!local.Data.Role || local.Data.RoleType != _originalRole.Value)
                throw new InvalidOperationException("The local native role setter did not restore the original role.");

            _changedLocally = false;
            _originalRole = null;
            _appliedRolePointer = IntPtr.Zero;
            _lastResult = "Local-only mode disabled; original local role restored.";
            MalumMenu.Log.LogInfo("Always Impostor: restored the original local role.");
        }
        catch (Exception error)
        {
            _failed = true;
            _lastResult = "Local role restoration failed; see Console log.";
            MalumMenu.Log.LogError("Always Impostor: local role restoration failed: " + error);
        }
    }

    private static void CancelPendingRolePicker()
    {
        if (!CheatToggles.setFakeRole) return;
        CheatToggles.setFakeRole = false;
        if (PlayerPickMenu.playerpickMenu != null)
        {
            // Closing is animated; any late click must be harmless.
            PlayerPickMenu.customAction = (Action)(() => { });
            PlayerPickMenu.playerpickMenu.Close();
        }
    }

    private static void ClearPlayerAttempt()
    {
        _originalRole = null;
        _appliedRolePointer = IntPtr.Zero;
        _changedLocally = false;
        _attempted = false;
        _restoreAttempted = false;
        _failed = false;
        _blockedKillReported = false;
    }
}
