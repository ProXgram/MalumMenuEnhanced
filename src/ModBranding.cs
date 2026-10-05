namespace MalumMenu;

public static class ModBranding
{
    public const string Name = "MalumMenu Enhanced";
    public const string Creator = "Rifegul";
    public const string OriginalAuthors = "scp222thj & astra1dev (Astral)";
    public const string Attribution = "Based on MalumMenu; GPL-3.0";

#if JUDGE_ROLE_EXPERIMENT
    public const string Version = "1.1.0-judge-test";
#elif GUEST_KILL_EXPERIMENT
    public const string Version = "1.1.0-guest-test";
#else
    public const string Version = "1.1.0";
#endif
}
