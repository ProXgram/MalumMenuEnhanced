using UnityEngine;

namespace MalumMenu;

public class TasksTab : ITab
{
    public string name => "Tasks";

    public void Draw()
    {
        MovementAutomation.Pause();
        GUILayout.BeginHorizontal();
        GUILayout.BeginVertical(GUILayout.Width(250f));

        GUILayout.Label("My tasks", GUIStylePreset.TabSubtitle);
        GUILayout.Label("These controls use your own\nreal assigned tasks.");
        CheatToggles.automaticTasks = GUILayout.Toggle(CheatToggles.automaticTasks,
            " Automatic Tasks");
        GUILayout.Label("Turn it on yourself when you\nwant tasks completed.");
        if (CheatToggles.automaticTasks)
            GUILayout.Label(AutomaticTasksHandler.StatusText, GUIStylePreset.TabSubtitle);

        GUILayout.Space(15f);
        GUILayout.Label("Task display", GUIStylePreset.TabSubtitle);
        CheatToggles.taskArrows = GUILayout.Toggle(CheatToggles.taskArrows, " Task Arrows");
        CheatToggles.showTasksMenu = GUILayout.Toggle(CheatToggles.showTasksMenu,
            " Show Tasks Menu");

        GUILayout.EndVertical();
        GUILayout.Space(12f);
        GUILayout.BeginVertical(GUILayout.Width(240f));

        GUILayout.Label("AI Tasks", GUIStylePreset.TabSubtitle);
        GUILayout.Label("Walks to your own task consoles\nand completes each step there.");
        bool isRunning = MovementAutomation.Mode == MovementMode.AiTasks;
        if (GUILayout.Button(isRunning ? "Stop AI Tasks" : "Start AI Tasks"))
        {
            if (isRunning) MovementAutomation.Stop();
            else MovementAutomation.Start(MovementMode.AiTasks);
        }

        GUILayout.Label(AiTasksHandler.StatusText);
        GUILayout.Label(MovementAutomation.StatusText);
        if (!string.IsNullOrEmpty(MovementAutomation.LastNavigationIssue))
            GUILayout.Label("Last route issue: " + MovementAutomation.LastNavigationIssue);
        GUILayout.Label("Close the menu and chat to walk.\nF8 stops movement.");
        GUILayout.Label("Starting AI Tasks turns\nAutomatic Tasks off. One movement\nmode runs at a time.");

        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
    }
}
