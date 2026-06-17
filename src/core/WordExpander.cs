using System.Text;
using System.Text.RegularExpressions;

namespace AurShell.Core;

public static class WordExpander
{
    public static List<string> ExpandWord(string input, ShellEnvironment env, bool performGlobbing = true)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(input))
            return result;

        var sb = new StringBuilder();
        bool inSingleQuote = false;
        bool inDoubleQuote = false;
        
        List<string> words = new List<string>();
        
        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];

            if (c == '\'' && !inDoubleQuote)
            {
                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (c == '"' && !inSingleQuote)
            {
                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            if (c == '\\' && !inSingleQuote)
            {
                if (i + 1 < input.Length)
                {
                    char next = input[i + 1];
                    if (inDoubleQuote)
                    {
                        if (next == '$' || next == '`' || next == '"' || next == '\\' || next == '\n')
                        {
                            i++;
                            sb.Append(next);
                        }
                        else
                        {
                            sb.Append('\\');
                        }
                    }
                    else
                    {
                        i++;
                        sb.Append(next);
                    }
                }
                else
                {
                    if (inDoubleQuote) sb.Append('\\');
                }
                continue;
            }

            if (c == '$' && !inSingleQuote)
            {
                // Expand variable or subshell
                int endIdx = ParseExpansion(input, i, out List<string> expandedParts, env, inDoubleQuote);
                if (endIdx > i)
                {
                    if (expandedParts.Count > 0)
                    {
                        if (inDoubleQuote)
                        {
                            sb.Append(expandedParts[0]);
                            for (int p = 1; p < expandedParts.Count; p++)
                            {
                                words.Add(sb.ToString());
                                sb.Clear();
                                sb.Append(expandedParts[p]);
                            }
                        }
                        else
                        {
                            // Word splitting for unquoted expansion
                            string joined = string.Join(" ", expandedParts);
                            var parts = SplitWords(joined, env.Get("IFS") ?? " \t\n");
                            if (parts.Count > 0)
                            {
                                sb.Append(parts[0]);
                                for (int p = 1; p < parts.Count; p++)
                                {
                                    words.Add(sb.ToString());
                                    sb.Clear();
                                    sb.Append(parts[p]);
                                }
                            }
                        }
                    }
                    i = endIdx - 1; // -1 because loop will increment i
                    continue;
                }
            }

            if ((c == '<' || c == '>') && !inSingleQuote && !inDoubleQuote && i + 1 < input.Length && input[i + 1] == '(')
            {
                int startCmd = i + 2;
                int depth = 1;
                int j = startCmd;
                while (j < input.Length && depth > 0)
                {
                    if (input[j] == '(') depth++;
                    else if (input[j] == ')') depth--;
                    j++;
                }
                if (j > startCmd)
                {
                    string cmd = input.Substring(startCmd, j - startCmd - 1);
                    string tempFile = env.ProcessSubstitutionEvaluator?.Invoke(cmd, c == '<', env) ?? "";
                    sb.Append(tempFile);
                    i = j - 1;
                    continue;
                }
            }

            if (c == '`' && !inSingleQuote)
            {
                int endIdx = ParseBacktick(input, i, out string expanded, env);
                if (endIdx > i)
                {
                    if (inDoubleQuote)
                    {
                        sb.Append(expanded);
                    }
                    else
                    {
                        var parts = SplitWords(expanded, env.Get("IFS") ?? " \t\n");
                        if (parts.Count > 0)
                        {
                            sb.Append(parts[0]);
                            for (int p = 1; p < parts.Count; p++)
                            {
                                words.Add(sb.ToString());
                                sb.Clear();
                                sb.Append(parts[p]);
                            }
                        }
                    }
                    i = endIdx - 1;
                    continue;
                }
            }

            sb.Append(c);
        }

        words.Add(sb.ToString());

        // Now perform globbing on unquoted words
        var globbedWords = new List<string>();
        foreach (var w in words)
        {
            if (performGlobbing && HasGlobChars(w))
            {
                var matches = PerformGlobbing(w, env);
                if (matches.Count > 0)
                    globbedWords.AddRange(matches);
                else
                    globbedWords.Add(w); // Keep original if no match
            }
            else
            {
                globbedWords.Add(w);
            }
        }

        return globbedWords;
    }

    private static int ParseExpansion(string input, int startIdx, out List<string> expanded, ShellEnvironment env, bool inDoubleQuote)
    {
        expanded = new List<string>();
        int i = startIdx + 1;
        if (i >= input.Length) return startIdx;

        char next = input[i];

        if (next == '(')
        {
            i++;
            if (i < input.Length && input[i] == '(')
            {
                i++;
                int depth = 2;
                int startExpr = i;
                while (i < input.Length && depth > 0)
                {
                    if (input[i] == '(') depth++;
                    else if (input[i] == ')') depth--;
                    i++;
                }
                string expr = input.Substring(startExpr, i - startExpr - 2);
                try {
                    expanded.Add(MathEvaluator.Evaluate(expr, env).ToString(System.Globalization.CultureInfo.InvariantCulture));
                } catch {
                    expanded.Add("");
                }
                return i;
            }
            else
            {
                int depth = 1;
                int startCmd = i;
                while (i < input.Length && depth > 0)
                {
                    if (input[i] == '(') depth++;
                    else if (input[i] == ')') depth--;
                    i++;
                }
                string cmd = input.Substring(startCmd, i - startCmd - 1);
                expanded.Add(env.SubshellEvaluator?.Invoke(cmd, env) ?? "");
                return i;
            }
        }
        else if (next == '<' || next == '>')
        {
            i++;
            if (i < input.Length && input[i] == '(')
            {
                i++;
                int depth = 1;
                int startCmd = i;
                while (i < input.Length && depth > 0)
                {
                    if (input[i] == '(') depth++;
                    else if (input[i] == ')') depth--;
                    i++;
                }
                string cmd = input.Substring(startCmd, i - startCmd - 1);
                expanded.Add(env.ProcessSubstitutionEvaluator?.Invoke(cmd, next == '<', env) ?? "");
                return i;
            }
        }

        if (next == '{')
        {
            i++;
            int depth = 1;
            int startBrace = i;
            while (i < input.Length && depth > 0)
            {
                if (input[i] == '{') depth++;
                else if (input[i] == '}') depth--;
                i++;
            }
            string varName = input.Substring(startBrace, i - startBrace - 1);
            expanded = ExpandVariable(varName, env, inDoubleQuote);
            return i;
        }

        if (next == '?' || next == '$' || next == '!' || next == '-' || next == '#' || next == '@' || next == '*')
        {
            expanded = ExpandVariable(next.ToString(), env, inDoubleQuote);
            return i + 1;
        }

        var nameBuf = new StringBuilder();
        while (i < input.Length && (char.IsLetterOrDigit(input[i]) || input[i] == '_'))
        {
            nameBuf.Append(input[i]);
            i++;
        }
        
        if (nameBuf.Length == 0) return startIdx;

        if (i < input.Length && input[i] == '.')
        {
            int savedI = i;
            i++;
            var fieldBuf = new StringBuilder();
            while (i < input.Length && (char.IsLetterOrDigit(input[i]) || input[i] == '_'))
            {
                fieldBuf.Append(input[i]);
                i++;
            }
            if (fieldBuf.Length > 0)
            {
                expanded.Add(env.GetObjectField(nameBuf.ToString(), fieldBuf.ToString()) ?? "");
                return i;
            }
            else
            {
                i = savedI;
            }
        }

        expanded = ExpandVariable(nameBuf.ToString(), env, inDoubleQuote);
        return i;
    }

    private static int ParseBacktick(string input, int startIdx, out string expanded, ShellEnvironment env)
    {
        expanded = "";
        int i = startIdx + 1;
        int startCmd = i;
        while (i < input.Length && input[i] != '`')
        {
            if (input[i] == '\\') i++; // Skip escaped backticks
            i++;
        }
        if (i < input.Length)
        {
            string cmd = input.Substring(startCmd, i - startCmd).Replace("\\`", "`");
            expanded = env.SubshellEvaluator?.Invoke(cmd, env) ?? "";
            return i + 1;
        }
        return startIdx;
    }

    private static List<string> ExpandVariable(string name, ShellEnvironment env, bool inDoubleQuote)
    {
        // Handle advanced parameter expansions like ${var:-default}
        string defaultVal = "";
        bool hasDefault = false;
        bool isAssignDefault = false;
        
        if (name.Contains(":-"))
        {
            int idx = name.IndexOf(":-");
            defaultVal = name.Substring(idx + 2);
            name = name.Substring(0, idx);
            hasDefault = true;
        }
        else if (name.Contains(":="))
        {
            int idx = name.IndexOf(":=");
            defaultVal = name.Substring(idx + 2);
            name = name.Substring(0, idx);
            hasDefault = true;
            isAssignDefault = true;
        }
        
        string? substringOffset = null;
        string? substringLength = null;
        if (name.Contains(':') && !hasDefault)
        {
            var parts = name.Split(':');
            name = parts[0];
            substringOffset = parts[1];
            if (parts.Length > 2) substringLength = parts[2];
        }

        string? prefixShort = null;
        string? prefixLong = null;
        string? suffixShort = null;
        string? suffixLong = null;
        string? replacePattern = null;
        string? replaceStr = null;
        bool replaceAll = false;

        if (name.Contains("##"))
        {
            int idx = name.IndexOf("##");
            prefixLong = name.Substring(idx + 2);
            name = name.Substring(0, idx);
        }
        else if (name.Contains("#"))
        {
            int idx = name.IndexOf("#");
            if (idx > 0) // not ${#var} length operator
            {
                prefixShort = name.Substring(idx + 1);
                name = name.Substring(0, idx);
            }
        }
        else if (name.Contains("%%"))
        {
            int idx = name.IndexOf("%%");
            suffixLong = name.Substring(idx + 2);
            name = name.Substring(0, idx);
        }
        else if (name.Contains("%"))
        {
            int idx = name.IndexOf("%");
            suffixShort = name.Substring(idx + 1);
            name = name.Substring(0, idx);
        }
        else if (name.Contains("/"))
        {
            int idx = name.IndexOf("/");
            if (idx + 1 < name.Length && name[idx + 1] == '/')
            {
                replaceAll = true;
                int nextSlash = name.IndexOf("/", idx + 2);
                if (nextSlash > 0)
                {
                    replacePattern = name.Substring(idx + 2, nextSlash - idx - 2);
                    replaceStr = name.Substring(nextSlash + 1);
                }
                else
                {
                    replacePattern = name.Substring(idx + 2);
                    replaceStr = "";
                }
            }
            else
            {
                int nextSlash = name.IndexOf("/", idx + 1);
                if (nextSlash > 0)
                {
                    replacePattern = name.Substring(idx + 1, nextSlash - idx - 1);
                    replaceStr = name.Substring(nextSlash + 1);
                }
                else
                {
                    replacePattern = name.Substring(idx + 1);
                    replaceStr = "";
                }
            }
            name = name.Substring(0, idx);
        }

        List<string> result = new List<string>();

        if (name == "?") result.Add(env.LastExitCode.ToString());
        else if (name == "$") result.Add(env.ShellPid.ToString());
        else if (name == "!") result.Add(env.BackgroundPid.ToString());
        else if (name == "#") result.Add(env.PositionalArguments.Count.ToString());
        else if (name == "-") result.Add(""); // Flags not fully implemented yet
        else if (name == "@")
        {
            if (inDoubleQuote)
                result.AddRange(env.PositionalArguments);
            else
                result.Add(string.Join(" ", env.PositionalArguments));
        }
        else if (name == "*")
        {
            string ifs = env.Get("IFS") ?? " ";
            char sep = ifs.Length > 0 ? ifs[0] : ' ';
            result.Add(string.Join(sep.ToString(), env.PositionalArguments));
        }
        else if (int.TryParse(name, out int index))
        {
            if (index > 0 && index <= env.PositionalArguments.Count)
                result.Add(env.PositionalArguments[index - 1]);
            else if (index == 0)
                result.Add(System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "aursh");
            else
                result.Add("");
        }
        else
        {
            result.Add(env.Get(name) ?? "");
        }

        // Apply defaults
        if (hasDefault && string.IsNullOrEmpty(result[0]) && result.Count <= 1)
        {
            result.Clear();
            result.Add(defaultVal);
            if (isAssignDefault)
            {
                env.Set(name, defaultVal);
            }
        }

        // Process substring, prefix, suffix, and pattern replacement
        for (int i = 0; i < result.Count; i++)
        {
            string val = result[i];
            
            if (substringOffset != null)
            {
                int offset = 0;
                int length = val.Length;
                if (int.TryParse(substringOffset, out int o))
                {
                    if (o < 0) o = val.Length + o;
                    offset = System.Math.Max(0, System.Math.Min(val.Length, o));
                }
                if (substringLength != null && int.TryParse(substringLength, out int l))
                {
                    length = System.Math.Max(0, l);
                }
                length = System.Math.Min(length, val.Length - offset);
                val = val.Substring(offset, length);
            }

            if (prefixLong != null || prefixShort != null || suffixLong != null || suffixShort != null)
            {
                // Basic glob-like implementation: match pattern with *
                string pattern = prefixLong ?? prefixShort ?? suffixLong ?? suffixShort ?? "";
                string regexPattern = GlobSegmentToRegex(pattern).TrimStart('^').TrimEnd('$');
                
                if (prefixLong != null || prefixShort != null)
                {
                    regexPattern = "^" + regexPattern;
                    if (prefixShort != null)
                    {
                        regexPattern = regexPattern.Replace(".*", ".*?");
                    }
                    var match = System.Text.RegularExpressions.Regex.Match(val, regexPattern);
                    if (match.Success)
                    {
                        val = val.Substring(match.Length);
                    }
                }
                else if (suffixLong != null || suffixShort != null)
                {
                    regexPattern = regexPattern + "$";
                    if (suffixShort != null)
                    {
                        regexPattern = regexPattern.Replace(".*", ".*?");
                    }
                    var match = System.Text.RegularExpressions.Regex.Match(val, regexPattern);
                    if (match.Success)
                    {
                        val = val.Substring(0, val.Length - match.Length);
                    }
                }
            }
            
            if (replacePattern != null && replaceStr != null)
            {
                string regexPattern = GlobSegmentToRegex(replacePattern).TrimStart('^').TrimEnd('$');
                var regex = new System.Text.RegularExpressions.Regex(regexPattern);
                if (replaceAll)
                {
                    val = regex.Replace(val, replaceStr);
                }
                else
                {
                    val = regex.Replace(val, replaceStr, 1);
                }
            }

            result[i] = val;
        }

        return result;
    }

    private static List<string> SplitWords(string expanded, string ifs)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        foreach (char c in expanded)
        {
            if (ifs.Contains(c))
            {
                if (sb.Length > 0)
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                }
            }
            else
            {
                sb.Append(c);
            }
        }
        if (sb.Length > 0) result.Add(sb.ToString());
        return result;
    }

    private static bool HasGlobChars(string word)
    {
        return word.Contains('*') || word.Contains('?') || (word.Contains('[') && word.Contains(']'));
    }

    private static List<string> PerformGlobbing(string pattern, ShellEnvironment env)
    {
        var results = new List<string>();
        string currentDir = env.Get("PWD") ?? Directory.GetCurrentDirectory();

        string normalizedPattern = pattern.Replace('\\', '/');
        string[] segments = normalizedPattern.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0)
        {
            results.Add(pattern);
            return results;
        }

        bool isAbsolute = Path.IsPathRooted(pattern);
        string startDir = currentDir;
        int startIndex = 0;
        string initialAccumulated = "";

        if (isAbsolute)
        {
            startDir = Path.GetPathRoot(pattern) ?? "/";
            startDir = startDir.Replace('\\', '/');
            if (!startDir.EndsWith("/")) startDir += "/";
            initialAccumulated = startDir;
            
            if (Utils.Platform.CurrentOS == Utils.OperatingSystemType.Windows)
            {
                if (segments.Length > 0 && segments[0].EndsWith(":"))
                    startIndex = 1;
            }
        }

        MatchSegments(startDir, segments, startIndex, initialAccumulated, results);

        bool endsWithSlash = pattern.EndsWith("/") || pattern.EndsWith("\\");
        if (endsWithSlash)
        {
            var dirOnlyResults = new List<string>();
            foreach (var r in results)
            {
                string fullPath = isAbsolute ? r : Path.Combine(startDir, r);
                if (Directory.Exists(fullPath))
                    dirOnlyResults.Add(r + "/");
            }
            results = dirOnlyResults;
        }

        return results.Count > 0 ? results : new List<string> { pattern };
    }

    private static void MatchSegments(string currentPath, string[] segments, int segmentIndex, string accumulatedPath, List<string> results)
    {
        if (segmentIndex >= segments.Length)
        {
            results.Add(accumulatedPath == "" ? currentPath : accumulatedPath);
            return;
        }

        string segment = segments[segmentIndex];

        if (!Directory.Exists(currentPath)) return;

        if (segment == "**")
        {
            var allDirs = new List<string>();
            try
            {
                allDirs.Add(currentPath);
                allDirs.AddRange(Directory.GetDirectories(currentPath, "*", SearchOption.AllDirectories));
            }
            catch { }

            foreach (var d in allDirs)
            {
                string relPath;
                if (d == currentPath) relPath = accumulatedPath;
                else
                {
                    string sub = d.Substring(currentPath.Length).Replace('\\', '/').TrimStart('/');
                    relPath = accumulatedPath == "" ? sub : (accumulatedPath.EndsWith("/") ? accumulatedPath + sub : accumulatedPath + "/" + sub);
                }
                
                MatchSegments(d, segments, segmentIndex + 1, relPath, results);
            }
            return;
        }

        string regexPattern = GlobSegmentToRegex(segment);
        Regex regex = new Regex(regexPattern, RegexOptions.IgnoreCase);

        try
        {
            var entries = Directory.GetFileSystemEntries(currentPath);
            foreach (var entry in entries)
            {
                string name = Path.GetFileName(entry);

                if (name.StartsWith(".") && !segment.StartsWith("."))
                    continue;

                if (regex.IsMatch(name))
                {
                    string newAccumulated;
                    if (accumulatedPath == "") newAccumulated = name;
                    else if (accumulatedPath.EndsWith("/")) newAccumulated = accumulatedPath + name;
                    else newAccumulated = accumulatedPath + "/" + name;
                    
                    if (segmentIndex == segments.Length - 1)
                    {
                        results.Add(newAccumulated);
                    }
                    else
                    {
                        if (Directory.Exists(entry))
                        {
                            MatchSegments(entry, segments, segmentIndex + 1, newAccumulated, results);
                        }
                    }
                }
            }
        }
        catch { }
    }

    public static string GlobSegmentToRegex(string pattern)
    {
        var sb = new StringBuilder("^");
        bool inCharClass = false;
        Stack<char> extglobStack = new Stack<char>();

        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];
            
            if (i + 1 < pattern.Length && pattern[i + 1] == '(' && "?*+@!".Contains(c))
            {
                extglobStack.Push(c);
                if (c == '!') sb.Append("(?!.*(?:");
                else sb.Append("(?:");
                i++; // skip the '('
                continue;
            }
            
            if (c == '|' && extglobStack.Count > 0)
            {
                sb.Append('|');
                continue;
            }
            
            if (c == ')' && extglobStack.Count > 0)
            {
                char op = extglobStack.Pop();
                if (op == '?') sb.Append(")?");
                else if (op == '*') sb.Append(")*");
                else if (op == '+') sb.Append(")+");
                else if (op == '@') sb.Append(")");
                else if (op == '!') sb.Append(")$).*");
                continue;
            }

            if (c == '*') sb.Append(".*");
            else if (c == '?') sb.Append(".");
            else if (c == '[')
            {
                inCharClass = true;
                sb.Append('[');
                if (i + 1 < pattern.Length && (pattern[i + 1] == '!' || pattern[i + 1] == '^'))
                {
                    sb.Append('^');
                    i++;
                }
            }
            else if (c == ']' && inCharClass)
            {
                inCharClass = false;
                sb.Append(']');
            }
            else if (".+()|{}^$".Contains(c) && !inCharClass) sb.Append("\\" + c);
            else sb.Append(c);
        }
        sb.Append("$");
        return sb.ToString();
    }
}
