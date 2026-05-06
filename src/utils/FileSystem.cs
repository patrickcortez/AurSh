namespace AurShell.Utils;

public static class FileSystem
{
    public static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        path = Platform.ExpandTilde(path);
        path = Path.GetFullPath(path);

        if (Platform.CurrentOS == OperatingSystemType.Windows)
            path = path.Replace('/', '\\');
        else
            path = path.Replace('\\', '/');

        if (path.Length > 1 && path.EndsWith(Path.DirectorySeparatorChar.ToString()))
            path = path.TrimEnd(Path.DirectorySeparatorChar);

        return path;
    }

    public static string CombinePaths(params string[] parts)
    {
        if (parts.Length == 0)
            return "";

        string combined = Path.Combine(parts);
        return NormalizePath(combined);
    }

    public static string GetRelativePath(string basePath, string targetPath)
    {
        basePath = NormalizePath(basePath);
        targetPath = NormalizePath(targetPath);
        return Path.GetRelativePath(basePath, targetPath);
    }

    public static bool IsAbsolutePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;
        return Path.IsPathRooted(path);
    }

    public static void EnsureDirectoryExists(string path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }

    public static void EnsureDirectory(string dirPath)
    {
        if (!string.IsNullOrEmpty(dirPath) && !Directory.Exists(dirPath))
            Directory.CreateDirectory(dirPath);
    }

    public static string[] ReadAllLinesSafe(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
                return File.ReadAllLines(filePath);
        }
        catch { }
        return Array.Empty<string>();
    }

    public static string ReadAllTextSafe(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
                return File.ReadAllText(filePath);
        }
        catch { }
        return "";
    }

    public static bool WriteAllLinesSafe(string filePath, IEnumerable<string> lines)
    {
        try
        {
            EnsureDirectoryExists(filePath);
            File.WriteAllLines(filePath, lines);
            return true;
        }
        catch { return false; }
    }

    public static bool WriteAllTextSafe(string filePath, string content)
    {
        try
        {
            EnsureDirectoryExists(filePath);
            File.WriteAllText(filePath, content);
            return true;
        }
        catch { return false; }
    }

    public static bool AppendLineSafe(string filePath, string line)
    {
        try
        {
            EnsureDirectoryExists(filePath);
            File.AppendAllText(filePath, line + System.Environment.NewLine);
            return true;
        }
        catch { return false; }
    }

    public static bool AppendLinesSafe(string filePath, IEnumerable<string> lines)
    {
        try
        {
            EnsureDirectoryExists(filePath);
            File.AppendAllLines(filePath, lines);
            return true;
        }
        catch { return false; }
    }

    public static long GetFileSizeSafe(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
                return new FileInfo(filePath).Length;
        }
        catch { }
        return -1;
    }

    public static string GetParentDirectory(string path)
    {
        string? parent = Path.GetDirectoryName(path);
        return parent ?? path;
    }

    public static bool FileExistsCaseInsensitive(string directory, string fileName)
    {
        try
        {
            if (!Directory.Exists(directory))
                return false;
            string[] files = Directory.GetFiles(directory);
            string target = fileName.ToLowerInvariant();
            foreach (string f in files)
            {
                if (Path.GetFileName(f).ToLowerInvariant() == target)
                    return true;
            }
        }
        catch { }
        return false;
    }

    public static string? FindFileCaseInsensitive(string directory, string fileName)
    {
        try
        {
            if (!Directory.Exists(directory))
                return null;
            string[] files = Directory.GetFiles(directory);
            string target = fileName.ToLowerInvariant();
            foreach (string f in files)
            {
                if (Path.GetFileName(f).ToLowerInvariant() == target)
                    return f;
            }
        }
        catch { }
        return null;
    }

    public static bool IsHiddenFile(string path)
    {
        string name = Path.GetFileName(path);
        if (name.StartsWith("."))
            return true;

        if (Platform.CurrentOS == OperatingSystemType.Windows)
        {
            try
            {
                FileAttributes attrs = File.GetAttributes(path);
                return (attrs & FileAttributes.Hidden) != 0;
            }
            catch { }
        }

        return false;
    }

    public static bool DeleteFileSafe(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                return true;
            }
        }
        catch { }
        return false;
    }

    public static bool CopyFileSafe(string source, string destination)
    {
        try
        {
            EnsureDirectoryExists(destination);
            File.Copy(source, destination, true);
            return true;
        }
        catch { return false; }
    }

    public static string ResolvePath(string path, string workingDirectory)
    {
        path = Platform.ExpandTilde(path);

        if (Path.IsPathRooted(path))
            return NormalizePath(path);

        return NormalizePath(Path.Combine(workingDirectory, path));
    }

    public static string GetExtension(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant();
    }

    public static string GetFileNameWithoutExtension(string path)
    {
        return Path.GetFileNameWithoutExtension(path);
    }
}
