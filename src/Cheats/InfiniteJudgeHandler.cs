using System;
using AmongUs.GameOptions;

namespace MalumMenu;

// Replenishes the owning practice Judge's local charge only in Freeplay.
// Online extra Overrule requests were rejected by the production server.
public static class InfiniteJudgeHandler
{
    private static IntPtr _playerPointer;
    private static IntPtr _rolePointer;
    private static bool _roundCaptured;
    private static bool _invalidated;
    private static bool _practiceBinding;
    private static bool _grantedUse;

    public static string StatusText => !Utils.isFreePlay
        ? "Freeplay only; online extra uses are blocked."
        : !_roundCaptured
        ? "Choose Judge in Freeplay."
        : _invalidated
            ? "Inactive after death or a role change."
            : "Refills next meeting; normal tasks required.";

    public static void Reset()
    {
        _playerPointer = IntPtr.Zero;
        _rolePointer = IntPtr.Zero;
        _roundCaptured = false;
        _invalidated = false;
        _practiceBinding = false;
        _grantedUse = false;
    }

    // A network round must not inherit any practice binding or granted use.
    public static void OnRoundIntro()
    {
        RemoveGrantedUse();
        Reset();
    }

    private static void CaptureCurrentJudge()
    {
        var local = PlayerControl.LocalPlayer;
        if (!local || !local.AmOwner || local.Data == null ||
            local.Data.IsDead || local.Data.Disconnected || !local.Data.Role ||
            local.Data.Role.Role != RoleTypes.Judge)
            return;

        var judge = local.Data.Role.TryCast<JudgeRole>();
        if (!judge || !judge.Player || judge.Player.Pointer != local.Pointer)
            return;

        _playerPointer = local.Pointer;
        _rolePointer = judge.Pointer;
        _roundCaptured = true;
    }

    private static bool IsCurrentAssignedJudge(JudgeRole judge)
    {
        var local = PlayerControl.LocalPlayer;
        return _roundCaptured && !_invalidated && local && local.AmOwner &&
            local.Pointer == _playerPointer && local.Data != null &&
            !local.Data.Disconnected && !local.Data.IsDead && local.Data.Role &&
            local.Data.Role.Role == RoleTypes.Judge &&
            local.Data.Role.Pointer == _rolePointer && judge &&
            judge.Pointer == _rolePointer && judge.Player &&
            judge.Player.Pointer == local.Pointer && judge.Player.AmOwner;
    }

    public static void Tick()
    {
        var local = PlayerControl.LocalPlayer;
        if (_practiceBinding && !Utils.isFreePlay)
        {
            RemoveGrantedUse();
            Reset();
            return;
        }

        // Practice has no round intro and allows arbitrary practice roles.
        if (Utils.isClient && Utils.isFreePlay && local && local.Data != null &&
            local.Data.Role && (!_practiceBinding ||
                local.Pointer != _playerPointer || local.Data.Role.Pointer != _rolePointer))
        {
            Reset();
            _practiceBinding = true;
            CaptureCurrentJudge();
        }
        if (!_roundCaptured || _invalidated) return;
        var judge = local && local.Data != null && local.Data.Role
            ? local.Data.Role.TryCast<JudgeRole>() : null;
        if (!IsCurrentAssignedJudge(judge))
        {
            // Do not accept a replacement fake Judge or a later fake-alive flag.
            _invalidated = true;
            _grantedUse = false;
            return;
        }

        if (!CheatToggles.infiniteJudge && _grantedUse)
        {
            // This use was granted from a spent (false) state, never from the
            // original unspent charge. Native consumption clears our marker.
            RemoveGrantedUse();
        }
    }

    private static void RemoveGrantedUse()
    {
        var local = PlayerControl.LocalPlayer;
        var judge = local && local.AmOwner && local.Pointer == _playerPointer &&
            local.Data != null && local.Data.Role &&
            local.Data.Role.Pointer == _rolePointer
                ? local.Data.Role.TryCast<JudgeRole>() : null;
        if (_grantedUse && judge && judge.Player &&
            judge.Player.Pointer == local.Pointer && judge.Player.AmOwner)
            judge.HasAnOverruleUse = false;
        _grantedUse = false;
    }

    public static void RefillForMeeting(JudgeRole judge)
    {
        Tick();
        if (!CheatToggles.infiniteJudge || !Utils.isClient ||
            !Utils.isFreePlay || !Utils.isNormalGame ||
            !IsCurrentAssignedJudge(judge) || judge.HasAnOverruleUse)
            return;

        judge.HasAnOverruleUse = true;
        _grantedUse = true;
        MalumMenu.Log.LogInfo("Infinite Judge: restored own Freeplay Overrule use for this meeting.");
    }

    public static void OnUseConsumed(JudgeRole judge)
    {
        if (judge && judge.Pointer == _rolePointer)
            _grantedUse = false;
    }
}
