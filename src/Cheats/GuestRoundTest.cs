#if GUEST_KILL_EXPERIMENT
using System;
using System.Collections.Generic;
using AmongUs.GameOptions;

namespace MalumMenu;

// Separate manual experiment. The normal release excludes this entire class.
// A local role/body is never evidence that another client accepted the action.
public static class GuestRoundTest
{
#if JUDGE_ROLE_EXPERIMENT
    public const RoleTypes RequestedRole = RoleTypes.Judge;
#else
    public const RoleTypes RequestedRole = RoleTypes.Impostor;
#endif
    private static bool _bound;
    private static int _gameId;
    private static int _hostId;
    private static bool _wasPublic;
    private static IntPtr _playerPointer;
    private static IntPtr _shipPointer;
    private static bool _roleAttempted;
#if !JUDGE_ROLE_EXPERIMENT
    private static bool _killAttempted;
#endif
    private static bool _guardErrorLogged;
    private static string _status = "Join an online round as a guest, then bind this round.";
    private static string _roleStatus = "Role request: not attempted this round.";
    private static string _killStatus = "Kill test: not attempted this round.";

    public static string StatusText { get { Refresh(); return _status; } }
    public static string RoleRequestStatus => _roleStatus;
    public static string KillRequestStatus => _killStatus;
    public static bool IsBound { get { Refresh(); return _bound; } }

    public static bool CanBind
    {
        get
        {
            try
            {
                Refresh();
                return !_bound && TryGetReadyRound(out _, out _, out _);
            }
            catch (Exception error)
            {
                ReportGuardError(error);
                return false;
            }
        }
    }

    public static string RoleRequestBlockReason
    {
        get
        {
            try
            {
                return CanRequestRole(out _, out var reason) ? "" : reason;
            }
            catch (Exception error)
            {
                ReportGuardError(error);
                return "Cannot verify role-request conditions; see Console.";
            }
        }
    }

    public static string KillRequestBlockReason
    {
        get
        {
            try
            {
                return CanTestKill(out _, out _, out var reason) ? "" : reason;
            }
            catch (Exception error)
            {
                ReportGuardError(error);
                return "Cannot verify kill-test conditions; see Console.";
            }
        }
    }

    // Called only by the root's join/new-round/end lifecycle integrations.
    // Cancel/rebind and GUI toggles never reset per-round attempt flags.
    public static void Reset()
    {
        _bound = false;
        _gameId = 0;
        _hostId = 0;
        _wasPublic = false;
        _playerPointer = IntPtr.Zero;
        _shipPointer = IntPtr.Zero;
        _roleAttempted = false;
#if !JUDGE_ROLE_EXPERIMENT
        _killAttempted = false;
#endif
        _guardErrorLogged = false;
        _status = "Join an online round as a guest, then bind this round.";
        _roleStatus = "Role request: not attempted this round.";
        _killStatus = "Kill test: not attempted this round.";
    }

    // Observation only: no RPCs, role writes or automatic test actions.
    public static void Refresh()
    {
        try
        {
            if (!TryGetGuestRound(out var local, out var ship, out var reason))
            {
                if (_bound) CancelBinding(reason);
                else _status = reason;
                return;
            }
            var client = AmongUsClient.Instance;
            if (_bound && (_gameId != client.GameId || _hostId != client.HostId ||
                _wasPublic != client.IsGamePublic ||
                _playerPointer != local.Pointer || _shipPointer != ship.Pointer))
            {
                CancelBinding("Round, host, lobby visibility or player changed; binding cancelled.");
                return;
            }
            if (!_bound)
            {
                _status = "Guest round ready; bind before a manual test.";
                return;
            }
            if (!TryGetReadyRound(out _, out _, out reason))
            {
                _status = reason;
                return;
            }
            _status = "Bound to this guest round. Remote acceptance remains unverified.";
        }
        catch (Exception error)
        {
            if (_bound) CancelBinding("Cannot verify this round; binding cancelled.");
            else _status = "Cannot verify this round; see Console.";
            ReportGuardError(error);
        }
    }

    public static void BindCurrentRound()
    {
        try
        {
            Refresh();
            if (_bound) return;
            if (!TryGetReadyRound(out var local, out var ship, out var reason))
            {
                _status = reason;
                return;
            }
            var client = AmongUsClient.Instance;
            _gameId = client.GameId;
            _hostId = client.HostId;
            _wasPublic = client.IsGamePublic;
            _playerPointer = local.Pointer;
            _shipPointer = ship.Pointer;
            _bound = true;
            _status = "Bound to this guest round. Remote acceptance remains unverified.";
            MalumMenu.Log.LogInfo("Guest round test: manually bound game " + _gameId +
                ", host " + _hostId + ", public=" + _wasPublic + ", local player " + local.PlayerId +
                "; local data OwnerId=" + local.Data.OwnerId +
                ", ServerOwned=" + InnerNet.InnerNetClient.ServerOwned +
                ", HostInherit=" + InnerNet.InnerNetClient.HostInherit +
                ". Ownership values are diagnostics, not acceptance evidence.");
        }
        catch (Exception error)
        {
            CancelBinding("Binding failed; see Console.");
            ReportGuardError(error);
        }
    }

    public static void CancelBinding()
    {
        CancelBinding("Binding cancelled. Attempts remain used until the next round.");
    }

    public static void SendOwnRoleRequestOnce()
    {
        try
        {
            if (!CanRequestRole(out var local, out var reason))
            {
                _status = reason;
                return;
            }
            _roleAttempted = true; // A thrown call also consumes this round's attempt.
            _roleStatus = "Role request: ATTEMPTED; remote acceptance unverified.";
            local.RpcSetRole(RequestedRole, true);
            _roleStatus = "Role request: SENT; remote acceptance unverified.";
            MalumMenu.Log.LogInfo("Guest round test: sent one own-player " + RequestedRole + " role request. " +
                "A local role change does not prove the host or server accepted it.");
        }
        catch (Exception error)
        {
            _roleStatus = _roleAttempted
                ? "Role request: call failed; attempt used this round."
                : "Role request: conditions could not be verified.";
            MalumMenu.Log.LogError("Guest round test: role request failed: " + error);
        }
    }

    public static void SendOwnKillTestOnce()
    {
#if JUDGE_ROLE_EXPERIMENT
        _killStatus = "Kill test disabled in the Judge assignment experiment.";
#else
        try
        {
            if (!CanTestKill(out var local, out var target, out var reason))
            {
                _status = reason;
                return;
            }
            if (LocalImpostorHandler.BlockOwnKill(local))
            {
                _killStatus = "Kill test: BLOCKED; local-only role, no call sent.";
                return;
            }
            _killAttempted = true; // No retries, even when the typed call throws.
            _killStatus = "Kill test: ATTEMPTED; remote acceptance unverified.";
            local.RpcMurderPlayer(target, true);
            _killStatus = "Kill test: SENT; remote acceptance unverified.";
            MalumMenu.Log.LogInfo("Guest round test: sent one own-player kill decision for target " +
                target.PlayerId + ". A local body or animation does not prove remote acceptance.");
        }
        catch (Exception error)
        {
            _killStatus = _killAttempted
                ? "Kill test: call failed; attempt used this round."
                : "Kill test: conditions could not be verified.";
            MalumMenu.Log.LogError("Guest round test: kill call failed: " + error);
        }
#endif
    }

    private static void CancelBinding(string reason)
    {
        _bound = false;
        _status = reason;
        // Keep _roleAttempted and _killAttempted until a genuine lifecycle Reset.
    }

    private static bool TryGetGuestRound(out PlayerControl local, out ShipStatus ship,
        out string reason)
    {
        local = PlayerControl.LocalPlayer;
        ship = ShipStatus.Instance;
        var client = AmongUsClient.Instance;
        if (!client || !Utils.isOnlineGame || !Utils.isInGame || Utils.isHost)
        {
            reason = "Available during an online round hosted by someone else.";
            return false;
        }
        if (GameOptionsManager.Instance == null ||
            GameOptionsManager.Instance.CurrentGameOptions == null || !Utils.isNormalGame)
        {
            reason = "The guest test requires Normal mode.";
            return false;
        }
        if (!local || !ship || !local.AmOwner || local.Data == null ||
            !local.Data.Role || local.Data.IsDead || local.Data.Disconnected)
        {
            reason = "Waiting for your living, connected player and loaded ship.";
            return false;
        }
        reason = "";
        return true;
    }

    private static bool TryGetReadyRound(out PlayerControl local, out ShipStatus ship,
        out string reason)
    {
        if (!TryGetGuestRound(out local, out ship, out reason)) return false;
        var hud = HudManager.Instance;
        if (!hud || hud.IsIntroDisplayed || Utils.isMeeting || Utils.isExiling)
        {
            reason = "Test actions pause during intro, meetings or ejection.";
            return false;
        }
        if (!PolicyAllows(local, ship, false))
        {
            reason = "Guest round conditions or movement are not ready.";
            return false;
        }
        reason = "";
        return true;
    }

    private static bool TryGetBoundRound(out PlayerControl local, out string reason)
    {
        Refresh();
        local = null;
        if (!_bound)
        {
            reason = "Bind the current guest round first.";
            return false;
        }
        if (!TryGetReadyRound(out local, out var ship, out reason)) return false;
        var client = AmongUsClient.Instance;
        if (_wasPublic != client.IsGamePublic || _gameId != client.GameId || _hostId != client.HostId ||
            _playerPointer != local.Pointer || _shipPointer != ship.Pointer ||
            !PolicyAllows(local, ship, true))
        {
            CancelBinding("Round conditions changed; binding cancelled.");
            reason = _status;
            return false;
        }
        reason = "";
        return true;
    }

    private static bool CanRequestRole(out PlayerControl local, out string reason)
    {
        if (!TryGetBoundRound(out local, out reason)) return false;
        if (_roleAttempted)
        {
            reason = "Role request already used this round.";
            return false;
        }
        if (local.Data.Role.TeamType != RoleTeamTypes.Crewmate)
        {
            reason = "Role request requires a current local Crewmate role.";
            return false;
        }
#if JUDGE_ROLE_EXPERIMENT
        if (local.Data.Role.Role == RoleTypes.Judge)
        {
            reason = "You are already locally Judge; this would not test a new assignment.";
            return false;
        }
        if (CheatToggles.alwaysImpostor || CheatToggles.setFakeRole)
        {
            reason = "Disable Always Impostor and Set Fake Role before this request.";
            return false;
        }
#else
        var data = GameData.Instance;
        if (!data || data.AllPlayers == null)
        {
            reason = "Cannot verify living role counts.";
            return false;
        }
        int crewmates = 0, impostors = 0;
        var ids = new HashSet<byte>();
        foreach (var player in data.AllPlayers)
        {
            if (player == null)
            {
                reason = "Cannot verify every player in the round.";
                return false;
            }
            if (player.IsDead || player.Disconnected) continue;
            if (!player.Role || !ids.Add(player.PlayerId))
            {
                reason = "Cannot verify every living player's role.";
                return false;
            }
            if (player.Role.TeamType == RoleTeamTypes.Crewmate) crewmates++;
            else if (player.Role.TeamType == RoleTeamTypes.Impostor) impostors++;
            else
            {
                reason = "Unknown team in the living role counts; request blocked.";
                return false;
            }
        }
        if (!ids.Contains(local.PlayerId) || ids.Count < 5 ||
            crewmates - 1 <= impostors + 1)
        {
            reason = "Need more living crewmates to request a role safely.";
            return false;
        }
#endif
        reason = "";
        return true;
    }

    private static bool CanTestKill(out PlayerControl local, out PlayerControl target,
        out string reason)
    {
        target = null;
#if JUDGE_ROLE_EXPERIMENT
        local = null;
        reason = "Kill testing is disabled in this Judge assignment build.";
        return false;
#else
        if (!TryGetBoundRound(out local, out reason)) return false;
        if (_killAttempted)
        {
            reason = "Kill test already used this round.";
            return false;
        }
        if (local.Data.Role.TeamType != RoleTeamTypes.Impostor)
        {
            reason = "Enable Always Impostor to obtain local kill controls first.";
            return false;
        }
        if (CheatToggles.killReach || CheatToggles.killAnyone || CheatToggles.killVanished ||
            CheatToggles.noKillCd)
        {
            reason = "Disable Kill Reach, Kill Anyone, Kill While Vanished and No Kill Cooldown for this test.";
            return false;
        }
        var button = HudManager.Instance.KillButton;
        if (!button || !button.isActiveAndEnabled || !button.gameObject.activeInHierarchy ||
            button.IsOnCooldown || !local.CanMove)
        {
            reason = "Wait for an active kill button, normal cooldown and movement.";
            return false;
        }
        target = button.Target;
        if (!target || target.Pointer == local.Pointer || target.Data == null ||
            target.Data.IsDead || target.Data.Disconnected || !target.Data.Role ||
            target.Data.Role.TeamType != RoleTeamTypes.Crewmate ||
            !local.Data.Role.IsValidTarget(target.Data))
        {
            target = null;
            reason = "Approach a living crewmate until the normal kill button selects them.";
            return false;
        }
        reason = "";
        return true;
#endif
    }

    private static bool PolicyAllows(PlayerControl local, ShipStatus ship, bool requireBinding)
    {
        var client = AmongUsClient.Instance;
        var hud = HudManager.Instance;
        bool identityMatches = !requireBinding || (_bound && client && local && ship &&
            _gameId == client.GameId && _hostId == client.HostId &&
            _wasPublic == client.IsGamePublic &&
            _playerPointer == local.Pointer && _shipPointer == ship.Pointer);
        return GuestRoundTestPolicy.CanAct(new GuestRoundTestPolicy.Context(
            Online: client && Utils.isOnlineGame,
            Started: client && Utils.isInGame,
            NonHost: client && !Utils.isHost,
            Normal: GameOptionsManager.Instance != null &&
                GameOptionsManager.Instance.CurrentGameOptions != null && Utils.isNormalGame,
            OwnPlayer: local && local.AmOwner,
            Alive: local && local.Data != null && !local.Data.IsDead,
            Connected: local && local.Data != null && !local.Data.Disconnected,
            CanMove: local && local.CanMove,
            IntroClosed: hud && !hud.IsIntroDisplayed,
            NoMeeting: !Utils.isMeeting,
            NoExile: !Utils.isExiling,
            BoundRoundMatches: identityMatches));
    }

    private static void ReportGuardError(Exception error)
    {
        if (_guardErrorLogged) return;
        _guardErrorLogged = true;
        MalumMenu.Log.LogError("Guest round test: condition inspection failed: " + error);
    }
}
#endif
