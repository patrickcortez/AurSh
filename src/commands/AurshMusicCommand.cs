using System;
using AurShell.Core;
using AurShell.Parser;

namespace AurShell.Commands;

public static class AurshMusicCommand
{
    public static int Execute(SimpleCommandNode cmd)
    {
        if (cmd.Args.Count == 0 || cmd.Args[0] != "start")
        {
            Console.Error.WriteLine("aursh-music: usage: aursh-music start");
            return 1;
        }

        try
        {
            AurShell.Music.MusicServer.Start(new string[0]);
            return 0;
        }
        catch (System.Exception ex)
        {
            Console.Error.WriteLine($"aursh-music: error: {ex.Message}");
            return 1;
        }
    }
}
