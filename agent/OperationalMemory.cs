using System.Text.Json;

namespace LunaPC;

internal sealed class OperationalMemory
{
    private readonly string _filePath;
    private readonly object _sync = new();
    private readonly List<MemoryItem> _items = new();
    private readonly List<LearningEpisode> _episodes = new();
    private const int MaxItems = 500;
    private const int MaxEpisodes = 300;

    public OperationalMemory()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LunaPC");
        Directory.CreateDirectory(folder);
        _filePath = Path.Combine(folder, "operational-memory.json");
        Load();
    }

    public IReadOnlyList<MemoryItem> Items { get { lock (_sync) return _items.ToList(); } }
    public IReadOnlyList<LearningEpisode> Episodes { get { lock (_sync) return _episodes.ToList(); } }

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

    public void RecordEpisode(string situation, string action, string result, string solution, bool success, double confidence = 1.0, IEnumerable<string>? tags = null)
    {
        if (string.IsNullOrWhiteSpace(situation) || string.IsNullOrWhiteSpace(action)) return;
        var episode = new LearningEpisode
        {
            Situation = Trim(situation, 4000),
            Action = Trim(action, 4000),
            Result = Trim(result, 4000),
            Solution = Trim(solution, 4000),
            Success = success,
            Confidence = Math.Clamp(confidence, 0, 1),
            Tags = tags?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim().ToLowerInvariant()).Distinct().Take(20).ToList() ?? new(),
            CreatedAt = DateTimeOffset.Now,
            LastUsedAt = DateTimeOffset.Now,
            Uses = 1
        };
        lock (_sync)
        {
            var existing = _episodes.FirstOrDefault(x =>
                string.Equals(x.Situation, episode.Situation, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.Action, episode.Action, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                existing.Result = episode.Result;
                existing.Solution = episode.Solution;
                existing.Success = episode.Success;
                existing.Confidence = Math.Clamp((existing.Confidence + episode.Confidence) / 2.0 + (episode.Success ? 0.05 : -0.02), 0, 1);
                existing.LastUsedAt = DateTimeOffset.Now;
                existing.Uses++;
                foreach (var tag in episode.Tags) if (!existing.Tags.Contains(tag)) existing.Tags.Add(tag);
            }
            else
            {
                _episodes.Add(episode);
                while (_episodes.Count > MaxEpisodes)
                    _episodes.RemoveAt(_episodes.FindIndex(x => x.LastUsedAt == _episodes.Min(y => y.LastUsedAt)));
            }
            SaveLocked();
        }
    }

    public string ForBrain(string? query = null, int maxItems = 25, int maxEpisodes = 8)
    {
        lock (_sync)
        {
            var items = _items.OrderByDescending(x => x.UpdatedAt).Take(maxItems).ToList();
            var episodes = RankEpisodes(query).Take(maxEpisodes).ToList();
            return JsonSerializer.Serialize(new { facts = items, learnedEpisodes = episodes }, new JsonSerializerOptions { WriteIndented = true });
        }
    }

    private IEnumerable<LearningEpisode> RankEpisodes(string? query)
    {
        var episodes = _episodes.ToList();
        if (string.IsNullOrWhiteSpace(query)) return episodes.OrderByDescending(x => x.LastUsedAt);
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length >= 3).Take(30).ToArray();
        return episodes
            .Select(e => new { Episode = e, Score = terms.Sum(t =>
                (e.Situation.Contains(t, StringComparison.OrdinalIgnoreCase) ? 4 : 0) +
                (e.Action.Contains(t, StringComparison.OrdinalIgnoreCase) ? 3 : 0) +
                (e.Result.Contains(t, StringComparison.OrdinalIgnoreCase) ? 2 : 0) +
                (e.Solution.Contains(t, StringComparison.OrdinalIgnoreCase) ? 3 : 0) +
                (e.Tags.Any(tag => tag.Contains(t, StringComparison.OrdinalIgnoreCase)) ? 2 : 0)) +
                (e.Success ? 2 : 0) + Math.Min(e.Uses, 10) * 0.25 })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Episode.LastUsedAt)
            .Select(x => x.Episode);
    }

    public void Clear()
    {
        lock (_sync) { _items.Clear(); _episodes.Clear(); SaveLocked(); }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return;
            var json = File.ReadAllText(_filePath);
            var data = JsonSerializer.Deserialize<MemoryData>(json);
            if (data is not null)
            {
                lock (_sync)
                {
                    _items.AddRange(data.Facts?.Take(MaxItems) ?? Enumerable.Empty<MemoryItem>());
                    _episodes.AddRange(data.LearnedEpisodes?.Take(MaxEpisodes) ?? Enumerable.Empty<LearningEpisode>());
                }
                return;
            }
            var legacy = JsonSerializer.Deserialize<List<MemoryItem>>(json);
            if (legacy is not null) lock (_sync) _items.AddRange(legacy.Take(MaxItems));
        }
        catch { }
    }

    private void SaveLocked()
    {
        try
        {
            var temp = _filePath + ".tmp";
            var data = new MemoryData { Facts = _items, LearnedEpisodes = _episodes };
            File.WriteAllText(temp, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, _filePath, true);
        }
        catch { }
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

    private static string Trim(string value, int max) => value.Length <= max ? value : value[..max] + "…";
}

internal sealed class MemoryData
{
    public List<MemoryItem> Facts { get; set; } = new();
    public List<LearningEpisode> LearnedEpisodes { get; set; } = new();
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

internal sealed class LearningEpisode
{
    public string Situation { get; set; } = "";
    public string Action { get; set; } = "";
    public string Result { get; set; } = "";
    public string Solution { get; set; } = "";
    public bool Success { get; set; }
    public double Confidence { get; set; }
    public List<string> Tags { get; set; } = new();
    public int Uses { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastUsedAt { get; set; }
}
