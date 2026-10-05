#if GUEST_KILL_EXPERIMENT
using UnityEngine;

namespace MalumMenu;

public class GuestRoundTestTab : ITab
{
    public string name =>
#if JUDGE_ROLE_EXPERIMENT
        "Judge Test";
#else
        "Guest Test";
#endif

    public void Draw()
    {
        GUILayout.Label("Guest round experiment", GUIStylePreset.TabSubtitle);
#if JUDGE_ROLE_EXPERIMENT
        GUILayout.Label("One manual request for your own Judge role per round. " +
            "The server may reject it or disconnect you.");
        GUILayout.Label("A local Judge label is not confirmation of a real assignment. " +
            "Infinite Judge remains restricted to the original assigned role.");
#else
        GUILayout.Label("Join an existing online round hosted by someone else. " +
            "Both actions are manual and limited to one attempt each per round.");
        GUILayout.Label("This test does not confirm a real Impostor role or accepted kill. " +
            "Your screen alone cannot confirm that the host or server accepted an action.");
#endif
        GUILayout.Space(8);
        GUILayout.Label(GuestRoundTest.StatusText);

        bool previouslyEnabled = GUI.enabled;
        try
        {
            GUI.enabled = previouslyEnabled && GuestRoundTest.CanBind;
            if (GUILayout.Button("Bind current guest round"))
                GuestRoundTest.BindCurrentRound();
            GUI.enabled = previouslyEnabled && GuestRoundTest.IsBound;
            if (GUILayout.Button("Cancel binding (keep used attempts)"))
                GuestRoundTest.CancelBinding();

            GUI.enabled = previouslyEnabled;
            GUILayout.Space(12);
            GUILayout.Label("Own role request", GUIStylePreset.TabSubtitle);
            GUILayout.Label("Optional: send your own " + GuestRoundTest.RequestedRole + " role request once. " +
                "A role changing on your screen is only a local observation.");
            var roleBlockReason = GuestRoundTest.RoleRequestBlockReason;
            GUI.enabled = previouslyEnabled && string.IsNullOrEmpty(roleBlockReason);
            if (GUILayout.Button("Send own " + GuestRoundTest.RequestedRole + " role request ONCE"))
                GuestRoundTest.SendOwnRoleRequestOnce();
            GUI.enabled = previouslyEnabled;
            if (!string.IsNullOrEmpty(roleBlockReason)) GUILayout.Label(roleBlockReason);
            GUILayout.Label(GuestRoundTest.RoleRequestStatus);

#if !JUDGE_ROLE_EXPERIMENT
            GUILayout.Space(12);
            GUILayout.Label("Own kill decision test", GUIStylePreset.TabSubtitle);
            GUILayout.Label("You can test this separately using Roles > Always Impostor. " +
                "Approach a crewmate with normal target selection and cooldown.");
            var killBlockReason = GuestRoundTest.KillRequestBlockReason;
            GUI.enabled = previouslyEnabled && string.IsNullOrEmpty(killBlockReason);
            if (GUILayout.Button("Send own kill test ONCE"))
                GuestRoundTest.SendOwnKillTestOnce();
            GUI.enabled = previouslyEnabled;
            if (!string.IsNullOrEmpty(killBlockReason)) GUILayout.Label(killBlockReason);
            GUILayout.Label(GuestRoundTest.KillRequestStatus);
#endif
        }
        finally
        {
            GUI.enabled = previouslyEnabled;
        }
    }
}
#endif
