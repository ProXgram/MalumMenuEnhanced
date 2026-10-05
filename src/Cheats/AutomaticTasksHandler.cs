using System;
using System.Collections.Generic;
using UnityEngine;

namespace MalumMenu;

public static class AutomaticTasksHandler
{
    private static readonly AutomaticTaskScheduler Scheduler = new();
    private static IntPtr _playerPointer;
    private static IntPtr _shipPointer;
    private static bool _enabledLastTick;
    private static bool _failed;
    private static int _completed;
    private static string _status = "Waiting for your assigned tasks.";

    public static string StatusText => _failed
        ? "Task error; toggle off/on to retry."
        : _status;

    public static void Reset()
    {
        Scheduler.Reset();
        _playerPointer = IntPtr.Zero;
        _shipPointer = IntPtr.Zero;
        _enabledLastTick = CheatToggles.automaticTasks;
        _failed = false;
        _completed = 0;
        _status = "Waiting for your assigned tasks.";
    }

    public static void Tick()
    {
        if (MovementAutomation.Mode == MovementMode.AiTasks)
        {
            _status = "AI Tasks is walking to your tasks.";
            return;
        }
        if (_enabledLastTick != CheatToggles.automaticTasks) Reset();
        if (!CheatToggles.automaticTasks) return;

        try
        {
            var local = PlayerControl.LocalPlayer;
            var ship = ShipStatus.Instance;
            if (local && ship && (_playerPointer != local.Pointer || _shipPointer != ship.Pointer))
            {
                Reset();
                _playerPointer = local.Pointer;
                _shipPointer = ship.Pointer;
            }
            if (_failed) return;
            if (!Utils.isClient || !local || local.Data == null || !ship ||
                (!Utils.isInGame && !Utils.isFreePlay) || !local.AmOwner ||
                local.Data.Disconnected || GameOptionsManager.Instance == null ||
                GameOptionsManager.Instance.CurrentGameOptions == null || !Utils.isNormalGame)
            {
                _status = "Waiting for your assigned tasks.";
                return;
            }

            // Instance creates this gameplay singleton when absent. A saved
            // enabled toggle must not instantiate it on the main menu or lobby.
            var hud = HudManager.Instance;
            if (!hud)
            {
                _status = "Waiting for the gameplay HUD.";
                return;
            }

            if (hud.IsIntroDisplayed || Utils.isMeeting || Utils.isExiling)
            {
                _status = "Paused during intro or meeting.";
                return;
            }

            var role = local.Data.Role;
            if (!role || role.TeamType != RoleTeamTypes.Crewmate || !role.TasksCountTowardProgress)
            {
                _status = "Inactive for Impostor / fake tasks.";
                return;
            }

            if (local.myTasks == null || local.myTasks.Count == 0)
            {
                _status = "Waiting for your assigned tasks.";
                return;
            }

            var slots = new List<AutomaticTaskScheduler.TaskSlot>();
            var tasks = new Dictionary<long, NormalPlayerTask>();
            foreach (var task in local.myTasks)
            {
                if (!task) continue;
                var normal = task.TryCast<NormalPlayerTask>();
                if (!normal) continue; // Exclude role text and other special tasks.
                var assigned = local.Data.FindTaskById(task.Id);
                var owner = task.Owner;
                var pointer = task.Pointer.ToInt64();
                slots.Add(new AutomaticTaskScheduler.TaskSlot(task.Id, pointer,
                    owner && owner.AmOwner && owner.Pointer == local.Pointer,
                    assigned != null, true,
                    task.IsComplete || (assigned != null && assigned.Complete),
                    PlayerTask.TaskIsEmergency(task), normal.MaxStep - normal.taskStep));
                tasks[pointer] = normal;
            }

            if (!Scheduler.TryTake(Time.realtimeSinceStartupAsDouble, true, true, true,
                slots, out var next))
            {
                _status = _completed > 0
                    ? "Completed " + _completed + " tasks this round."
                    : "Waiting for unfinished normal tasks.";
                return;
            }

            var selected = tasks[next.InstanceId];
            // Native task stepping updates local task progress and emits the
            // normal completion RPC at the final step. Never send a second RPC.
            for (var step = 0; step < next.RemainingSteps && !selected.IsComplete; step++)
            {
                var previousStep = selected.taskStep;
                selected.NextStep();
                if (!selected.IsComplete && selected.taskStep <= previousStep)
                    throw new InvalidOperationException("The native task did not advance.");
            }
            if (!selected.IsComplete)
                throw new InvalidOperationException("The assigned task did not complete.");

            _completed++;
            _status = "Completed " + _completed + " tasks this round.";
            MalumMenu.Log.LogInfo("Automatic Tasks: completed own task " + next.Id +
                " (" + selected.TaskType + ").");
        }
        catch (Exception error)
        {
            if (!_failed)
                MalumMenu.Log.LogError("Automatic Tasks: paused after task completion failed: " + error);
            _failed = true; // One failure pauses completion instead of retrying every frame.
        }
    }
}
