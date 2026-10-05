namespace MalumMenuEnhanced.Setup;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length != 0)
        {
            Environment.ExitCode = UpdateCommand.RunAsync(args).GetAwaiter().GetResult();
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new SetupForm());
    }
}
