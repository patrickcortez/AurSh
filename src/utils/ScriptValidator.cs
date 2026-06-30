using System;

namespace AurShell.Utils
{
    public static class ScriptValidator
    {
        public static void Validate(string path, string content)
        {
            if (!path.EndsWith(".aur", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception($"Validation Error: AurSh scripts must have a .aur extension. Provided file: {System.IO.Path.GetFileName(path)}");
            }

            int firstNewline = content.IndexOf('\n');
            if (firstNewline == -1) firstNewline = content.Length;

            string firstLine = content.Substring(0, firstNewline).TrimEnd('\r');

            if (!firstLine.StartsWith("#!"))
            {
                throw new Exception($"Validation Error: AurSh scripts must start with a shebang line. Found: {firstLine}");
            }

            string expectedPath = Environment.ProcessPath ?? "";
            string shebangPath = firstLine.Substring(2).Trim(' ', '"', '\'');
            
            // Normalize paths for comparison (ignoring case and slash direction for cross-platform robustness)
            string normExpected = expectedPath.Replace('\\', '/').ToLowerInvariant();
            string normActual = shebangPath.Replace('\\', '/').ToLowerInvariant();

            if (normExpected != normActual)
            {
                throw new Exception($"Validation Error: AurSh scripts must start with a shebang line pointing to the exact AurSh executable path.\nExpected: #!{expectedPath}\nFound: {firstLine}");
            }
        }
    }
}
