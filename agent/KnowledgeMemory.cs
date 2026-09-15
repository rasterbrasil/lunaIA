using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LunaPC;

/// <summary>
/// Phase 2.3: persistent local knowledge memory built from the internet corpus.
/// Retrieval is deterministic and local: no external AI service is used.
/// </summary>
internal sealed class KnowledgeMemory
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a","as","o","os","um","uma","uns","umas","de","da","do","das","dos","e","ou",
        "em","no","na","nos","nas","por","para","com","sem","que","se","é","e","eu","você",
        "voce","me","te","ele","ela","eles","elas","isso","isto","aquele","aquela","como","mais",
        "menos","sobre","entre","também","tambem","ser","são","sao","foi","era","sua","seu","suas","seus",
        "ao","aos","à","às","ate","até","já","ja","muito","muita","muitos","muitas","pode","podem",
        "sabe","sabem","conhece","conhecem","fale","falar","explique","explicar","defina","definir","diga","dizer",
        "pesquise","pesquisa","procure","procurar","busque","buscar","quero","quero","sobre","assunto"
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
            var normalizedTitle = Normalize(document.Title);
            var titleHits = terms.Count(term => TitleMatches(normalizedTitle, term));

            foreach (var passage in SplitPassages(document.Text))
            {
                var normalized = Normalize(passage);
                var score = Score(terms, normalized, normalizedTitle, titleHits);
                if (score <= 0) continue;

                // For a one-topic question such as "sobre o Brasil", require the
                // topic to be present in the title or repeatedly in the passage.
                // This prevents a random mention of "Brasil" from winning.
                var coverage = terms.Count(t => ContainsTopic(normalized, t));
                if (terms.Count == 1 && titleHits == 0 && coverage == 0) continue;
                if (terms.Count >= 2 && titleHits == 0 && coverage < 2) continue;

                results.Add(new KnowledgeMatch(document.Title, passage.Trim(), score));
            }
        }

        return results
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
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

    private static int Score(List<string> terms, string normalizedText, string normalizedTitle, int titleHits)
    {
        var score = titleHits * 30;
        foreach (var term in terms)
        {
            var textHits = CountWhole(normalizedText, term);
            if (textHits > 0) score += Math.Min(12, textHits * 4);
            if (TitleMatches(normalizedTitle, term)) score += 25;
        }

        var coverage = terms.Count(t => ContainsTopic(normalizedText, t));
        if (coverage >= 2) score += coverage * 10;
        return score;
    }

    private static bool TitleMatches(string title, string term)
    {
        if (title.Contains(term, StringComparison.Ordinal)) return true;
        // Small Portuguese morphological tolerance: brasil -> brasileira/brasileiro.
        if (term.Length >= 5 && title.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(word => word.StartsWith(term, StringComparison.Ordinal) && word.Length <= term.Length + 5)) return true;
        return false;
    }

    private static bool ContainsTopic(string text, string term)
        => text.Contains(term, StringComparison.Ordinal);

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
        => Regex.Matches(Normalize(text), "[a-z0-9]+")
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
