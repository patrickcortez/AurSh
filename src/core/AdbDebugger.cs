using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AurShell.Core;

public enum StepMode
{
    None,
    StepInto,
    StepOver,
    StepOut,
    Until
}

public class Breakpoint
{
    public int Id { get; set; }
    public string File { get; set; } = "";
    public int Line { get; set; }
    public string? Condition { get; set; }
    public bool Enabled { get; set; } = true;
    public int IgnoreCount { get; set; } = 0;
    public bool IsTemporary { get; set; } = false;
}

public class AdbDebugger
{
    private ShellEnvironment _env;
    private StepMode _stepMode = StepMode.StepInto;
    private int _targetStackDepth = 0;
    private int _targetLine = 0;
    
    private List<Breakpoint> _breakpoints = new();
    private int _nextBreakpointId = 1;

    private int _currentFrameOffset = 0;
    private string _lastCommand = "step"; // default for pressing enter

    public AdbDebugger(ShellEnvironment env)
    {
        _env = env;
    }

    private string NormalizePath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        return Path.GetFullPath(path).Replace('\\', '/').ToLowerInvariant();
    }

    public bool ShouldPause(string file, int line, int stackDepth)
    {
        if (string.IsNullOrEmpty(file) || line <= 0) return false;

        string normFile = NormalizePath(file);
        
        // Breakpoint hit?
        var bps = _breakpoints.Where(b => b.Enabled && b.Line == line && NormalizePath(b.File) == normFile).ToList();
        foreach (var bp in bps)
        {
            if (bp.IgnoreCount > 0)
            {
                bp.IgnoreCount--;
                continue;
            }

            if (!string.IsNullOrEmpty(bp.Condition))
            {
                try
                {
                    string expanded = string.Join(" ", WordExpander.ExpandWord(bp.Condition, _env));
                    if (MathEvaluator.Evaluate(expanded, _env) == 0) continue; 
                }
                catch
                {
                    // If condition fails to evaluate, break anyway or skip? We'll break.
                }
            }

            if (bp.IsTemporary)
            {
                _breakpoints.Remove(bp);
            }

            Console.WriteLine($"\n[ADB] Breakpoint {bp.Id} hit at {file}:{line}");
            _stepMode = StepMode.StepInto;
            return true;
        }

        if (_stepMode == StepMode.StepInto) return true;
        if (_stepMode == StepMode.StepOver && stackDepth <= _targetStackDepth) return true;
        if (_stepMode == StepMode.StepOut && stackDepth < _targetStackDepth) return true;
        if (_stepMode == StepMode.Until && line >= _targetLine && stackDepth <= _targetStackDepth) return true;

        return false;
    }

    public void PauseAndBlock(string file, int line, ShellEnvironment env)
    {
        _currentFrameOffset = 0; 
        
        string codeLine = GetSourceLine(file, line);
        Console.WriteLine($"\n[ADB] Paused at {file}:{line}");
        Console.WriteLine($"-> {line}: {codeLine.Trim()}");

        while (true)
        {
            Console.Write("(adb) ");
            string? input = Console.ReadLine();
            
            if (input == null)
            {
                Console.WriteLine("EOF. Exiting debugger.");
                Environment.Exit(0);
            }

            input = input.Trim();
            
            if (string.IsNullOrEmpty(input))
            {
                input = _lastCommand;
            }
            else
            {
                _lastCommand = input;
            }

            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;

            string cmd = parts[0].ToLowerInvariant();
            try
            {
                if (ProcessCommand(cmd, parts.Skip(1).ToArray(), file, line, env))
                {
                    break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error executing '{cmd}': {ex.Message}");
            }
        }
    }

    private bool ProcessCommand(string cmd, string[] args, string currentFile, int currentLine, ShellEnvironment env)
    {
        switch (cmd)
        {
            // Execution control
            case "s": case "step":
                _stepMode = StepMode.StepInto;
                return true;

            case "n": case "next":
                _stepMode = StepMode.StepOver;
                _targetStackDepth = env.CallStack.Count;
                return true;

            case "c": case "cont": case "continue":
                _stepMode = StepMode.None;
                return true;

            case "fin": case "finish": case "return":
                _stepMode = StepMode.StepOut;
                _targetStackDepth = env.CallStack.Count;
                return true;

            case "until":
                if (args.Length > 0 && int.TryParse(args[0], out int uLine))
                {
                    _stepMode = StepMode.Until;
                    _targetLine = uLine;
                    _targetStackDepth = env.CallStack.Count;
                    return true;
                }
                Console.WriteLine("Usage: until <line>");
                return false;

            // Breakpoints
            case "b": case "break":
                if (args.Length == 0)
                {
                    Console.WriteLine("Usage: break <line>");
                    return false;
                }
                if (int.TryParse(args[0], out int bLine))
                {
                    int id = _nextBreakpointId++;
                    _breakpoints.Add(new Breakpoint { Id = id, File = currentFile, Line = bLine });
                    Console.WriteLine($"Breakpoint {id} at {currentFile}:{bLine}");
                }
                return false;
                
            case "tbreak":
                if (args.Length == 0)
                {
                    Console.WriteLine("Usage: tbreak <line>");
                    return false;
                }
                if (int.TryParse(args[0], out int tbLine))
                {
                    int id = _nextBreakpointId++;
                    _breakpoints.Add(new Breakpoint { Id = id, File = currentFile, Line = tbLine, IsTemporary = true });
                    Console.WriteLine($"Temporary breakpoint {id} at {currentFile}:{tbLine}");
                }
                return false;

            case "condition":
                if (args.Length >= 2 && int.TryParse(args[0], out int condId))
                {
                    var bp = _breakpoints.FirstOrDefault(b => b.Id == condId);
                    if (bp != null)
                    {
                        bp.Condition = string.Join(" ", args.Skip(1));
                        Console.WriteLine($"Condition set for breakpoint {condId}");
                    }
                    else Console.WriteLine($"No breakpoint with id {condId}");
                }
                else Console.WriteLine("Usage: condition <id> <expr>");
                return false;

            case "d": case "delete":
                if (args.Length > 0 && int.TryParse(args[0], out int delId))
                {
                    int removed = _breakpoints.RemoveAll(b => b.Id == delId);
                    if (removed > 0) Console.WriteLine($"Deleted breakpoint {delId}");
                    else Console.WriteLine($"No breakpoint with id {delId}");
                }
                else Console.WriteLine("Usage: delete <id>");
                return false;

            case "clear":
                _breakpoints.Clear();
                Console.WriteLine("Cleared all breakpoints.");
                return false;

            case "disable":
                if (args.Length > 0 && int.TryParse(args[0], out int disId))
                {
                    var bp = _breakpoints.FirstOrDefault(b => b.Id == disId);
                    if (bp != null) 
                    {
                        bp.Enabled = false;
                        Console.WriteLine($"Disabled breakpoint {disId}");
                    }
                }
                return false;

            case "enable":
                if (args.Length > 0 && int.TryParse(args[0], out int enId))
                {
                    var bp = _breakpoints.FirstOrDefault(b => b.Id == enId);
                    if (bp != null)
                    {
                        bp.Enabled = true;
                        Console.WriteLine($"Enabled breakpoint {enId}");
                    }
                }
                return false;

            case "ignore":
                if (args.Length == 2 && int.TryParse(args[0], out int igId) && int.TryParse(args[1], out int count))
                {
                    var bp = _breakpoints.FirstOrDefault(b => b.Id == igId);
                    if (bp != null)
                    {
                        bp.IgnoreCount = count;
                        Console.WriteLine($"Breakpoint {igId} will ignore the next {count} hits");
                    }
                }
                return false;

            case "info":
                if (args.Length > 0 && args[0] == "break")
                {
                    Console.WriteLine("Num\tType\t\tEnb\tWhere");
                    foreach (var bp in _breakpoints)
                    {
                        string type = bp.IsTemporary ? "tbreakpoint" : "breakpoint";
                        string enb = bp.Enabled ? "y" : "n";
                        Console.WriteLine($"{bp.Id}\t{type}\t{enb}\t{bp.File}:{bp.Line}");
                        if (bp.IgnoreCount > 0) Console.WriteLine($"\tignore next {bp.IgnoreCount} hits");
                        if (!string.IsNullOrEmpty(bp.Condition)) Console.WriteLine($"\tstop only if {bp.Condition}");
                    }
                }
                return false;

            // Source navigation
            case "l": case "list":
                PrintSourceContext(currentFile, currentLine, 5);
                return false;

            case "ll":
                PrintSourceContext(currentFile, currentLine, 1000);
                return false;

            case "where":
                Console.WriteLine($"File: {currentFile}");
                Console.WriteLine($"Line: {currentLine}");
                return false;

            // Stack navigation
            case "bt": case "backtrace":
                Console.WriteLine($"-> 0: {currentFile}:{currentLine} (Current)");
                int frameId = 1;
                foreach (var frame in env.CallStack)
                {
                    Console.WriteLine($"   {frameId++}: {frame.File}:{frame.Line} in {frame.Name}");
                }
                return false;

            case "up":
                if (_currentFrameOffset < env.CallStack.Count)
                {
                    _currentFrameOffset++;
                    Console.WriteLine($"Moved up to frame {_currentFrameOffset}");
                }
                else Console.WriteLine("Oldest frame");
                return false;

            case "down":
                if (_currentFrameOffset > 0)
                {
                    _currentFrameOffset--;
                    Console.WriteLine($"Moved down to frame {_currentFrameOffset}");
                }
                else Console.WriteLine("Newest frame");
                return false;

            case "frame":
                if (args.Length > 0 && int.TryParse(args[0], out int fId))
                {
                    if (fId >= 0 && fId <= env.CallStack.Count)
                    {
                        _currentFrameOffset = fId;
                        Console.WriteLine($"Selected frame {_currentFrameOffset}");
                    }
                    else Console.WriteLine("Invalid frame");
                }
                return false;

            // Variable inspection
            case "p": case "print":
                if (args.Length > 0)
                {
                    string expr = string.Join(" ", args);
                    if (!expr.StartsWith("$")) expr = "$" + expr; // implicit variable eval
                    string expanded = string.Join(" ", WordExpander.ExpandWord(expr, env));
                    Console.WriteLine(expanded);
                }
                else
                {
                    Console.WriteLine("Usage: print <expr>");
                }
                return false;

            case "locals":
                Console.WriteLine("Variables in local scope:");
                if (env.LocalScopes.Count() > _currentFrameOffset)
                {
                    foreach (var kv in env.LocalScopes.ElementAt(_currentFrameOffset))
                        Console.WriteLine($"{kv.Key} = {kv.Value}");
                }
                else
                {
                    Console.WriteLine("No local scope found.");
                }
                return false;

            case "globals":
                Console.WriteLine("Variables in global scope:");
                foreach (var kv in env.Variables)
                    Console.WriteLine($"{kv.Key} = {kv.Value}");
                return false;

            // Shell specific
            case "env":
                foreach (var kv in env.Variables)
                    Console.WriteLine($"{kv.Key} = {kv.Value}");
                return false;

            case "alias":
                foreach (var kv in env.Aliases)
                    Console.WriteLine($"alias {kv.Key}='{kv.Value}'");
                return false;

            case "functions":
                // Functions are stored as AurValues in this implementation, 
                // so we skip dedicated listing for now.
                Console.WriteLine("Functions are stored as variables. Use 'env' to list them.");
                return false;

            case "ast":
                Console.WriteLine("AST dumping not fully implemented from debugger context yet.");
                return false;

            // General
            case "q": case "quit":
                Console.WriteLine("Debugger quitting.");
                Environment.Exit(0);
                return true;

            case "h": case "help":
                Console.WriteLine("ADB (AurSh Debugging Bridge) Commands:");
                Console.WriteLine("  s, step             Step into");
                Console.WriteLine("  n, next             Step over");
                Console.WriteLine("  c, continue         Continue execution");
                Console.WriteLine("  fin, finish         Run until current function returns");
                Console.WriteLine("  until <line>        Continue until a specific line");
                Console.WriteLine("  b, break <line>     Set breakpoint");
                Console.WriteLine("  tbreak <line>       Set temporary breakpoint");
                Console.WriteLine("  condition <id> <c>  Break only if condition true");
                Console.WriteLine("  d, delete <id>      Delete a breakpoint");
                Console.WriteLine("  disable <id>        Disable a breakpoint");
                Console.WriteLine("  enable <id>         Enable a breakpoint");
                Console.WriteLine("  clear               Remove all breakpoints");
                Console.WriteLine("  ignore <id> <N>     Ignore next N hits");
                Console.WriteLine("  info break          List breakpoints");
                Console.WriteLine("  p, print <expr>     Print variable or expression");
                Console.WriteLine("  locals              Show local variables");
                Console.WriteLine("  globals             Show global variables");
                Console.WriteLine("  bt, backtrace       Show call stack");
                Console.WriteLine("  up, down            Move in call stack");
                Console.WriteLine("  frame <n>           Select a stack frame");
                Console.WriteLine("  l, list             Show surrounding source code");
                Console.WriteLine("  ll                  Show entire source code");
                Console.WriteLine("  where               Show current location");
                Console.WriteLine("  env, alias          Show environment / aliases");
                Console.WriteLine("  functions           Show functions");
                Console.WriteLine("  q, quit             Exit debugger");
                return false;

            default:
                Console.WriteLine($"*** Unknown command: {cmd}");
                return false;
        }
    }

    private string GetSourceLine(string file, int line)
    {
        try
        {
            if (File.Exists(file))
            {
                var lines = File.ReadAllLines(file);
                if (line > 0 && line <= lines.Length)
                {
                    return lines[line - 1];
                }
            }
        }
        catch { }
        return "";
    }

    private void PrintSourceContext(string file, int line, int contextLines)
    {
        try
        {
            if (File.Exists(file))
            {
                var lines = File.ReadAllLines(file);
                int start = Math.Max(0, line - contextLines - 1);
                int end = Math.Min(lines.Length, line + contextLines);

                for (int i = start; i < end; i++)
                {
                    string prefix = (i + 1) == line ? "->" : "  ";
                    Console.WriteLine($"{prefix} {i + 1}\t{lines[i]}");
                }
            }
            else
            {
                Console.WriteLine("Source file not found.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading source: {ex.Message}");
        }
    }
}
