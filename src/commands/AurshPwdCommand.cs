namespace AurShell.Commands;

public static class AurshPwdCommand
{
    public static int Execute(string workingDirectory)
    {
        Core.BuiltinCommands.WriteOut(workingDirectory);
        return 0;
    }
}
