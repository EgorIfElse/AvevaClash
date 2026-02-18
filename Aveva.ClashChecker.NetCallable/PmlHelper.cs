using Aveva.Core.Utilities.CommandLine;
namespace Aveva.ClashChecker.NetCallable;

public static class PmlHelper
{

    public static void WriteLine(string message)
    {
        Command.CreateCommand($"$p '{message}'").RunInPdms();
    }

}
