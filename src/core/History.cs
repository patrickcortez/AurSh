namespace AurShell.Core;

public class History
{
    private readonly List<string> _entries = new();
    private readonly string _filePath;
    private readonly int _maxEntries;
    private int _navigationIndex;
    private string _savedCurrent = "";

    public int Count => _entries.Count;
    public IReadOnlyList<string> Entries => _entries;

    public History(string filePath, int maxEntries = 10000)
    {
        _filePath = filePath;
        _maxEntries = maxEntries;
        _navigationIndex = -1;
        Load();
    }

    public void Add(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        if (_entries.Count > 0 && _entries[_entries.Count - 1] == line)
            return;

        _entries.Add(line);

        if (_entries.Count > _maxEntries)
            _entries.RemoveAt(0);

        Utils.FileSystem.AppendLineSafe(_filePath, line);
        ResetNavigation();
    }

    public void Clear()
    {
        _entries.Clear();
        Utils.FileSystem.WriteAllTextSafe(_filePath, "");
        ResetNavigation();
    }

    public void ResetNavigation()
    {
        _navigationIndex = -1;
        _savedCurrent = "";
    }

    public string? NavigateUp(string currentInput)
    {
        if (_entries.Count == 0)
            return null;

        if (_navigationIndex == -1)
        {
            _savedCurrent = currentInput;
            _navigationIndex = _entries.Count - 1;
        }
        else if (_navigationIndex > 0)
        {
            _navigationIndex--;
        }
        else
        {
            return _entries[0];
        }

        return _entries[_navigationIndex];
    }

    public string? NavigateDown(string currentInput)
    {
        if (_navigationIndex == -1)
            return null;

        if (_navigationIndex < _entries.Count - 1)
        {
            _navigationIndex++;
            return _entries[_navigationIndex];
        }

        _navigationIndex = -1;
        return _savedCurrent;
    }

    public List<string> Search(string query, int maxResults = 50)
    {
        var results = new List<string>();
        if (string.IsNullOrEmpty(query))
            return results;

        for (int i = _entries.Count - 1; i >= 0 && results.Count < maxResults; i--)
        {
            if (_entries[i].Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                if (!results.Contains(_entries[i]))
                    results.Add(_entries[i]);
            }
        }

        return results;
    }

    public string? GetSuggestion(string prefix)
    {
        if (string.IsNullOrEmpty(prefix))
            return null;

        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i].StartsWith(prefix, StringComparison.Ordinal) && _entries[i].Length > prefix.Length)
                return _entries[i];
        }

        return null;
    }

    public string? ReverseSearch(string query, int startIndex = -1)
    {
        if (string.IsNullOrEmpty(query))
            return null;

        int start = startIndex >= 0 ? startIndex : _entries.Count - 1;

        for (int i = start; i >= 0; i--)
        {
            if (_entries[i].Contains(query, StringComparison.OrdinalIgnoreCase))
                return _entries[i];
        }

        return null;
    }

    public int ReverseSearchIndex(string query, int startIndex = -1)
    {
        if (string.IsNullOrEmpty(query))
            return -1;

        int start = startIndex >= 0 ? startIndex : _entries.Count - 1;

        for (int i = start; i >= 0; i--)
        {
            if (_entries[i].Contains(query, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private void Load()
    {
        string[] lines = Utils.FileSystem.ReadAllLinesSafe(_filePath);
        int start = Math.Max(0, lines.Length - _maxEntries);

        for (int i = start; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (!string.IsNullOrEmpty(line))
                _entries.Add(line);
        }

        if (lines.Length > _maxEntries)
            Compact();
    }

    private void Compact()
    {
        Utils.FileSystem.WriteAllLinesSafe(_filePath, _entries);
    }
}
