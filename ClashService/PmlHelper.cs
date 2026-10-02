using Aveva.Core.Utilities.CommandLine;
namespace ClashForKPI;

public static class PmlHelper
{

    public static void WriteLine(string message)
    {
        Command.CreateCommand($"$p '{message}'").RunInPdms();
    }

}
