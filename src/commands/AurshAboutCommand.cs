using System;
using AurShell.Core;
using AurShell.Utils;
using System.Runtime.InteropServices;
using AurShell.Core;

namespace AurShell.Commands;

public static class AurshAboutCommand
{
    private static string GetPlatform()
    {
        if (OperatingSystem.IsWindows()) return "Windows";
        if (OperatingSystem.IsLinux()) return "Linux";
        if (OperatingSystem.IsMacOS()) return "macOS";
        if (OperatingSystem.IsFreeBSD()) return "FreeBSD";
        return RuntimeInformation.OSDescription;
    }

    private static Architecture GetArch()
    {
        return RuntimeInformation.OSArchitecture;
    }

    public static int Execute(SimpleCommandNode cmd)
    {

        string about = $@"
        {Ansi.FgBrightCyan}-------------------------------------------------------------------------------------------------------
        
                        {Ansi.FgBrightBlue}                 About:
                           {Ansi.FgBrightBlue} - This frontend shell is developed in C# by {Ansi.FgBrightCyan}Tezzz{Ansi.FgBrightBlue}.
                           {Ansi.FgBrightBlue} As a cross platform shell with a purpose to make the command-line
                          {Ansi.FgBrightBlue}  look aesthetically pleasing while working. This Shell is under the license of
                            {Ansi.FgBrightCyan}GNU General Public License.

                            {Ansi.FgBrightBlue}Current Platform: {Ansi.FgBrightMagenta}{GetPlatform()}
                            {Ansi.FgBrightBlue}Current Architecture: {Ansi.FgBrightMagenta}{GetArch().ToString()}
                            {Ansi.FgBrightBlue}Current Version: {Ansi.FgBrightMagenta}{BuiltinCommands.Version}

                            {Ansi.FgBrightBlue}AurSh has some native commands that you can invoke/use:
                                - {Ansi.FgBrightCyan}aursh-ls : {Ansi.FgBrightBlue}TUI file-system explorer.
                                - {Ansi.FgBrightCyan}aursh-about : {Ansi.FgBrightBlue}Shows this message.
                                - {Ansi.FgBrightCyan}aursh-assoc : {Ansi.FgBrightBlue}Associate file extensions with its compiler/interpreter.
                                - {Ansi.FgBrightCyan}aursh-plugin : {Ansi.FgBrightBlue}Plugin management of AurSh.
                                - {Ansi.FgBrightCyan}aursh-history : {Ansi.FgBrightBlue}TUI command history.
                                - {Ansi.FgBrightCyan}aursh-reload : {Ansi.FgBrightBlue}Reloads the Shell to apply newly added plugins.
                                - {Ansi.FgBrightCyan}aursh-cat <options: -e> <file> : {Ansi.FgBrightBlue}Pipable file reader and vim-like TUI text editor.
                                - {Ansi.FgBrightCyan}aursh-update : {Ansi.FgBrightBlue}Updates the shell from the remote repository.
                                - {Ansi.FgBrightCyan}aursh-context : {Ansi.FgBrightBlue}Create, Modify or Delete Contexts.
                                - {Ansi.FgBrightCyan}aursh-net : {Ansi.FgBrightBlue}A network tool for connecting,disconnecting and recieving/sending data through the command-line.
                                - {Ansi.FgBrightCyan}aursh-ssh : {Ansi.FgBrightBlue}TUI interface for managing SSH keys and remote hosts.
                                - {Ansi.FgBrightCyan}grm : {Ansi.FgBrightBlue}Git Repo Manager for installing repositories from GitHub.
                                - {Ansi.FgBrightCyan}aursh-music : {Ansi.FgBrightBlue}A websever music player, available at http://127.0.0.1:7007.

       {Ansi.FgBrightCyan} -------------------------------------------------------------------------------------------------------
        ";

        Console.WriteLine(about);

        return 0;
    }

}


