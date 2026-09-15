using System.Text.Json;

namespace LunaPC;

internal sealed class OperationalMemory
{
    private readonly string _filePath;
    private readonly object _sync = new();
    private readonly List<MemoryItem> _items = new();
    private const int MaxItems = 500;

    public OperationalMemory()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LunaPC");
        Directory.CreateDirectory(folder);
        _filePath = Path.Combine(folder, "operational-memory.json");
        Load();
    }

    public IReadOnlyList<MemoryItem> Items
    {
        get { lock (_sync) return _items.ToList(); }
    }

    public void Add(string category, string content, double confidence = 1.0, string source = "agent")
    {
        if (string.IsNullOrWhiteSpace(content)) return;
        category = NormalizeCategory(category);
        confidence = Math.Clamp(confidence, 0, 1);
        if (confidence < 0.75) return;

        lock (_sync)
        {
            var existing = _items.FirstOrDefault(x => x.Category == category && string.Equals(x.Content, content, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                existing.UpdatedAt = DateTimeOffset.Now;
                existing.Confidence = Math.Max(existing.Confidence, confidence);
            }
            else
            {
                _items.Add(new MemoryItem { Category = category, Content = content.Trim(), Confidence = confidence, Source = source, CreatedAt = DateTimeOffset.Now, UpdatedAt = DateTimeOffset.Now });
                while (_items.Count > MaxItems) _items.RemoveAt(0);
            }
            SaveLocked();
        }
    }

    public string ForBrain(string? query = null, int maxItems = 25)
    {
        IEnumerable<MemoryItem> selected;
        lock (_sync) selected = _items.OrderByDescending(x => x.UpdatedAt).Take(maxItems).ToList();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var relevant = selected.Where(x => terms.Any(t => x.Content.Contains(t, StringComparison.OrdinalIgnoreCase) || x.Category.Contains(t, StringComparison.OrdinalIgnoreCase))).ToList();
            if (relevant.Count > 0) selected = relevant;
        }
        return JsonSerializer.Serialize(selected, new JsonSerializerOptions { WriteIndented = true });
    }

    public void Clear()
    {
        lock (_sync) { _items.Clear(); SaveLocked(); }
    }

    private static string NormalizeCategory(string category) => category?.Trim().ToLowerInvariant() switch
    {
        "preferencia" or "preference" => "preferencia",
        "programa" or "program" => "programa",
        "fluxo" or "workflow" => "fluxo",
        "decisao" or "decision" => "decisao",
        "erro" or "error" => "erro",
        "solucao" or "solution" => "solucao",
        _ => "outro"
    };

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return;
            var data = JsonSerializer.Deserialize<List<MemoryItem>>(File.ReadAllText(_filePath));
            if (data is null) return;
            lock (_sync) _items.AddRange(data.Take(MaxItems));
        }
        catch { }
    }

    private void SaveLocked()
    {
        try
        {
            var temp = _filePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_items, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, _filePath, true);
        }
        catch { }
    }
}

internal sealed class MemoryItem
{
    public string Category { get; set; } = "outro";
    public string Content { get; set; } = "";
    public double Confidence { get; set; }
    public string Source { get; set; } = "agent";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
