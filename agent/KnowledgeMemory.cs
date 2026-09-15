using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LunaPC;

/// <summary>
/// Phase 2.3: persistent local knowledge memory built from the internet corpus.
/// This is retrieval, not an external AI service. It searches the corpus stored on disk,
/// ranks passages lexically, and returns grounded excerpts for the native Transformer.
/// </summary>
internal sealed class KnowledgeMemory
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a","as","o","os","um","uma","uns","umas","de","da","do","das","dos","e","ou",
        "em","no","na","nos","nas","por","para","com","sem","que","se","é","e","eu","você",
        "voce","me","te","ele","ela","eles","elas","isso","isto","aquele","aquela","como","mais",
        "menos","sobre","entre","também","tambem","ser","são","sao","foi","era","sua","seu","suas","seus",
        "ao","aos","à","às","ate","até","já","ja","muito","muita","muitos","muitas","pode","podem"
    };

    private readonly string _corpusPath;
    private readonly object _sync = new();
    private List<KnowledgeDocument>? _cache;
    private DateTime _cacheWriteTimeUtc;

    public KnowledgeMemory(string brainDirectory)
    {
        _corpusPath = Path.Combine(brainDirectory, "internet-corpus-v1.jsonl");
    }

    public int DocumentCount => LoadDocuments().Count;

    public IReadOnlyList<KnowledgeMatch> Search(string query, int maxResults = 5)
    {
        var terms = Tokenize(query);
        if (terms.Count == 0) return Array.Empty<KnowledgeMatch>();

        var results = new List<KnowledgeMatch>();
        foreach (var document in LoadDocuments())
        {
            foreach (var passage in SplitPassages(document.Text))
            {
                var normalized = Normalize(passage);
                var score = Score(terms, normalized, document.Title);
                if (score <= 0) continue;
                results.Add(new KnowledgeMatch(document.Title, passage.Trim(), score));
            }
        }

        return results
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Text.Length)
            .Take(Math.Max(1, maxResults))
            .ToList();
    }

    public string BuildContext(string query, int maxCharacters = 2200)
    {
        var matches = Search(query, 6);
        if (matches.Count == 0) return "";
        var sb = new StringBuilder();
        foreach (var match in matches)
        {
            var block = $"[{match.Title}] {match.Text}";
            if (sb.Length + block.Length + 1 > maxCharacters) break;
            sb.AppendLine(block);
        }
        return sb.ToString().Trim();
    }

    private List<KnowledgeDocument> LoadDocuments()
    {
        lock (_sync)
        {
            if (!File.Exists(_corpusPath)) return _cache = new List<KnowledgeDocument>();
            var writeTime = File.GetLastWriteTimeUtc(_corpusPath);
            if (_cache is not null && writeTime == _cacheWriteTimeUtc) return _cache;

            var documents = new List<KnowledgeDocument>();
            try
            {
                foreach (var line in File.ReadLines(_corpusPath))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        var item = JsonSerializer.Deserialize<CorpusLine>(line);
                        if (item is null || string.IsNullOrWhiteSpace(item.Title) || string.IsNullOrWhiteSpace(item.Text)) continue;
                        documents.Add(new KnowledgeDocument(item.Title, item.Text));
                    }
                    catch { }
                }
            }
            catch { }

            _cache = documents;
            _cacheWriteTimeUtc = writeTime;
            return documents;
        }
    }

    private static IEnumerable<string> SplitPassages(string text)
    {
        var pieces = Regex.Split(text, @"(?<=[.!?])\s+|\n+");
        foreach (var piece in pieces)
        {
            var clean = piece.Trim();
            if (clean.Length >= 45) yield return clean.Length > 650 ? clean[..650] : clean;
        }
    }

    private static int Score(List<string> terms, string normalizedText, string title)
    {
        var titleText = Normalize(title);
        var score = 0;
        foreach (var term in terms)
        {
            var textHits = CountWhole(normalizedText, term);
            if (textHits > 0) score += Math.Min(6, textHits * 3);
            if (titleText.Contains(term, StringComparison.Ordinal)) score += 8;
        }

        var coverage = terms.Count(t => normalizedText.Contains(t, StringComparison.Ordinal));
        if (coverage >= 2) score += coverage * 4;
        return score;
    }

    private static int CountWhole(string text, string term)
    {
        var count = 0;
        var start = 0;
        while ((start = text.IndexOf(term, start, StringComparison.Ordinal)) >= 0)
        {
            count++;
            start += term.Length;
        }
        return count;
    }

    private static List<string> Tokenize(string text)
        => Regex.Matches(Normalize(text), "[a-z0-9áéíóúâêôãõç]+", RegexOptions.IgnoreCase)
            .Select(m => m.Value)
            .Where(x => x.Length >= 3 && !StopWords.Contains(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string Normalize(string value)
    {
        var form = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(form.Length);
        foreach (var c in form)
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private sealed record CorpusLine(string Title, string Text);
    private sealed record KnowledgeDocument(string Title, string Text);
}

internal readonly record struct KnowledgeMatch(string Title, string Text, int Score);
