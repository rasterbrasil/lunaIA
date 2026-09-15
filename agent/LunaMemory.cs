using System.Text.Json;
using System.Text.Json.Serialization;

namespace LunaPC;

internal enum LunaMemoryKind
{
    Conversation,
    UserProfile,
    Preference,
    Project,
    Computer,
    Application,
    Decision,
    Skill,
    Experience
}

internal sealed record LunaMemoryEntry(
    Guid Id,
    LunaMemoryKind Kind,
    string Key,
    string Content,
    DateTime UpdatedAt,
    int Confidence = 100);

internal sealed class LunaMemory : IDisposable
{
    private readonly object _sync = new();
    private readonly string _directory;
    private readonly string _path;
    private readonly List<LunaMemoryEntry> _entries = new();
    private readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private bool _disposed;

    public LunaMemory()
    {
        _directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LunaIA",
            "memory");
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "long-term-memory.json");
        Load();
    }

    public int Count
    {
        get { lock (_sync) return _entries.Count; }
    }

    public void Remember(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        ThrowIfDisposed();

        var value = text.Trim();
        AddOrUpdate(LunaMemoryKind.Conversation, "conversation", value, 100);
        LearnExplicitFacts(value);
        TrimConversations(500);
        Save();
    }

    public void Learn(LunaMemoryKind kind, string key, string content, int confidence = 100)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(content)) return;
        ThrowIfDisposed();
        AddOrUpdate(kind, NormalizeKey(key), content.Trim(), Math.Clamp(confidence, 0, 100));
        Save();
    }

    public void RecordExperience(string task, bool success, string result)
    {
        if (string.IsNullOrWhiteSpace(task)) return;
        var status = success ? "sucesso" : "falha";
        Learn(
            LunaMemoryKind.Experience,
            $"task:{NormalizeKey(task)}",
            $"Tarefa: {task.Trim()} | Resultado: {status} | Detalhes: {result.Trim()}",
            success ? 100 : 80);
    }

    public string BuildContext(string query, int maxItems = 12)
    {
        ThrowIfDisposed();
        var normalized = NormalizeKey(query);
        lock (_sync)
        {
            var candidates = _entries
                .Where(e => e.Kind != LunaMemoryKind.Conversation)
                .Select(e => new
                {
                    Entry = e,
                    Score = Score(e, normalized)
                })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Entry.UpdatedAt)
                .Take(maxItems)
                .Select(x => Format(x.Entry))
                .ToList();

            if (candidates.Count == 0)
            {
                candidates = _entries
                    .Where(e => e.Kind != LunaMemoryKind.Conversation)
                    .OrderByDescending(e => e.UpdatedAt)
                    .Take(maxItems)
                    .Select(Format)
                    .ToList();
            }

            return candidates.Count == 0
                ? "Nenhuma memória de longo prazo relevante foi registrada ainda."
                : string.Join("\n", candidates);
        }
    }

    public IReadOnlyList<LunaMemoryEntry> GetByKind(LunaMemoryKind kind, int maxItems = 50)
    {
        lock (_sync)
        {
            return _entries
                .Where(e => e.Kind == kind)
                .OrderByDescending(e => e.UpdatedAt)
                .Take(maxItems)
                .ToArray();
        }
    }

    private void LearnExplicitFacts(string text)
    {
        var normalized = NormalizeKey(text);

        if (TryExtract(text, normalized, ["meu nome e ", "me chamo "], out var name))
            Learn(LunaMemoryKind.UserProfile, "name", name, 100);

        if (TryExtract(text, normalized, ["eu prefiro que voce ", "prefiro que voce ", "gosto que voce "], out var responsePreference))
            Learn(LunaMemoryKind.Preference, "response-style", responsePreference, 95);

        if (TryExtract(text, normalized, ["meu projeto e ", "meu projeto principal e ", "o projeto e "], out var project))
            Learn(LunaMemoryKind.Project, "primary-project", project, 95);

        if (TryExtract(text, normalized, ["meu computador e ", "este computador e ", "meu pc e "], out var computer))
            Learn(LunaMemoryKind.Computer, "primary-computer", computer, 95);

        if (TryExtract(text, normalized, ["meu navegador e ", "uso o navegador ", "uso o "], out var application)
            && LooksLikeApplicationStatement(normalized))
            Learn(LunaMemoryKind.Application, "preferred-application", application, 80);

        if (normalized.Contains("nao quero que voce") || normalized.Contains("nao faca isso sem") || normalized.Contains("sempre me pergunte antes"))
            Learn(LunaMemoryKind.Decision, $"rule:{DateTime.UtcNow.Ticks}", text.Trim(), 100);
    }

    private static bool TryExtract(string original, string normalized, IReadOnlyList<string> prefixes, out string value)
    {
        foreach (var prefix in prefixes)
        {
            var normalizedPrefix = NormalizeKey(prefix);
            if (!normalized.StartsWith(normalizedPrefix, StringComparison.Ordinal)) continue;
            var start = original.Length - normalized.Length + normalizedPrefix.Length;
            if (start >= 0 && start < original.Length)
            {
                value = original[start..].Trim(' ', ':', '.', '!', '?');
                if (!string.IsNullOrWhiteSpace(value)) return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static bool LooksLikeApplicationStatement(string normalized)
        => normalized.Contains("chrome")
            || normalized.Contains("edge")
            || normalized.Contains("whatsapp")
            || normalized.Contains("github")
            || normalized.Contains("supabase")
            || normalized.Contains("vercel")
            || normalized.Contains("youtube")
            || normalized.Contains("notepad")
            || normalized.Contains("calculadora");

    private void AddOrUpdate(LunaMemoryKind kind, string key, string content, int confidence)
    {
        lock (_sync)
        {
            var existing = _entries.FirstOrDefault(e =>
                e.Kind == kind && string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                _entries.Add(new(Guid.NewGuid(), kind, key, content, DateTime.UtcNow, confidence));
                return;
            }

            var index = _entries.IndexOf(existing);
            _entries[index] = existing with
            {
                Content = content,
                UpdatedAt = DateTime.UtcNow,
                Confidence = Math.Max(existing.Confidence, confidence)
            };
        }
    }

    private static int Score(LunaMemoryEntry entry, string query)
    {
        var score = 1;
        var key = NormalizeKey(entry.Key);
        var content = NormalizeKey(entry.Content);
        if (!string.IsNullOrWhiteSpace(query) && key.Contains(query)) score += 5;

        foreach (var token in query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token.Length >= 3 && content.Contains(token)) score += 2;
            if (token.Length >= 3 && key.Contains(token)) score += 3;
        }

        return score;
    }

    private static string Format(LunaMemoryEntry entry)
        => $"[{entry.Kind}] {entry.Key}: {entry.Content}";

    private void TrimConversations(int max)
    {
        lock (_sync)
        {
            var conversations = _entries
                .Where(e => e.Kind == LunaMemoryKind.Conversation)
                .OrderByDescending(e => e.UpdatedAt)
                .Skip(max)
                .Select(e => e.Id)
                .ToHashSet();
            if (conversations.Count == 0) return;
            _entries.RemoveAll(e => conversations.Contains(e.Id));
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var json = File.ReadAllText(_path);
            var loaded = JsonSerializer.Deserialize<List<LunaMemoryEntry>>(json, _json);
            if (loaded is null) return;
            lock (_sync)
            {
                _entries.Clear();
                _entries.AddRange(loaded);
            }
        }
        catch
        {
            // A damaged memory file must never prevent LUNA from starting.
            lock (_sync) _entries.Clear();
        }
    }

    private void Save()
    {
        try
        {
            List<LunaMemoryEntry> snapshot;
            lock (_sync) snapshot = _entries.ToList();
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(snapshot, _json));
            File.Move(temp, _path, overwrite: true);
        }
        catch
        {
            // Memory persistence is best-effort; conversation execution must survive I/O failures.
        }
    }

    private static string NormalizeKey(string value)
    {
        var form = value.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var chars = form.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark);
        return new string(chars.ToArray()).Normalize(System.Text.NormalizationForm.FormC).Trim();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LunaMemory));
    }

    public void Dispose() => _disposed = true;
}
