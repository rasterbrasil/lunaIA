using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LunaPC;

/// <summary>
/// Phase 2.3/2.4: persistent local knowledge memory with an inverted index.
/// The corpus is scanned only when it changes; normal questions query the index,
/// not the full JSONL corpus. No external AI service is used.
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
        "pesquise","pesquisa","procure","procurar","busque","buscar","quero","assunto"
    };

    private readonly string _corpusPath;
    private readonly string _indexPath;
    private readonly object _sync = new();
    private KnowledgeIndex? _index;

    public KnowledgeMemory(string brainDirectory)
    {
        Directory.CreateDirectory(brainDirectory);
        _corpusPath = Path.Combine(brainDirectory, "internet-corpus-v1.jsonl");
        _indexPath = Path.Combine(brainDirectory, "knowledge-index-v1.json");
    }

    public int DocumentCount => EnsureIndex().Documents.Count;

    /// <summary>
    /// Searches only indexed candidate passages. If the corpus changed, the index is
    /// rebuilt once and persisted; subsequent queries do not read/scan the corpus file.
    /// </summary>
    public IReadOnlyList<KnowledgeMatch> Search(string query, int maxResults = 5)
    {
        var index = EnsureIndex();
        var terms = Tokenize(query);
        if (terms.Count == 0) return Array.Empty<KnowledgeMatch>();

        var candidateIds = new HashSet<int>();
        foreach (var term in terms)
        {
            if (index.Postings.TryGetValue(term, out var ids))
                foreach (var id in ids) candidateIds.Add(id);

            // Small Portuguese morphology expansion. The index stores prefixes for
            // title matching, so "brasil" can also find "brasileiro/brasileira" titles.
            if (term.Length >= 5 && index.TitlePrefixPostings.TryGetValue(term, out var titleIds))
                foreach (var id in titleIds) candidateIds.Add(id);
        }

        if (candidateIds.Count == 0) return Array.Empty<KnowledgeMatch>();

        var results = new List<KnowledgeMatch>();
        foreach (var id in candidateIds)
        {
            if (id < 0 || id >= index.Passages.Count) continue;
            var passage = index.Passages[id];
            var normalizedTitle = Normalize(passage.Title);
            var normalizedText = passage.NormalizedText;
            var exactTitleHits = terms.Count(term => ExactTitleMatch(normalizedTitle, term));
            var fuzzyTitleHits = terms.Count(term => FuzzyTitleMatch(normalizedTitle, term));
            var coverage = terms.Count(t => ContainsTopic(normalizedText, t));

            if (terms.Count == 1 && exactTitleHits == 0 && fuzzyTitleHits == 0 && coverage == 0) continue;
            if (terms.Count >= 2 && exactTitleHits == 0 && fuzzyTitleHits == 0 && coverage < 2) continue;

            var score = Score(terms, normalizedText, normalizedTitle, exactTitleHits, fuzzyTitleHits);
            if (score > 0)
                results.Add(new KnowledgeMatch(passage.Title, passage.Text, score));
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

    private KnowledgeIndex EnsureIndex()
    {
        lock (_sync)
        {
            var corpusTicks = File.Exists(_corpusPath) ? File.GetLastWriteTimeUtc(_corpusPath).Ticks : 0;
            if (_index is not null && _index.CorpusWriteTicks == corpusTicks) return _index;

            if (corpusTicks == 0)
            {
                _index = EmptyIndex(0);
                return _index;
            }

            if (TryLoadIndex(corpusTicks, out var saved))
            {
                _index = saved;
                return _index;
            }

            _index = BuildIndex(corpusTicks);
            SaveIndex(_index);
            return _index;
        }
    }

    private bool TryLoadIndex(long corpusTicks, out KnowledgeIndex index)
    {
        index = EmptyIndex(corpusTicks);
        try
        {
            if (!File.Exists(_indexPath)) return false;
            var json = File.ReadAllText(_indexPath, Encoding.UTF8);
            var loaded = JsonSerializer.Deserialize<KnowledgeIndex>(json);
            if (loaded is null || loaded.CorpusWriteTicks != corpusTicks || loaded.Passages.Count == 0 && loaded.Documents.Count > 0) return false;
            index = loaded;
            return true;
        }
        catch { return false; }
    }

    private KnowledgeIndex BuildIndex(long corpusTicks)
    {
        var documents = new List<KnowledgeDocument>();
        var passages = new List<IndexedPassage>();
        var postings = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var titlePrefixPostings = new Dictionary<string, List<int>>(StringComparer.Ordinal);

        try
        {
            foreach (var line in File.ReadLines(_corpusPath, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var item = JsonSerializer.Deserialize<CorpusLine>(line);
                    if (item is null || string.IsNullOrWhiteSpace(item.Title) || string.IsNullOrWhiteSpace(item.Text)) continue;
                    documents.Add(new KnowledgeDocument(item.Title, item.Text));

                    foreach (var passageText in SplitPassages(item.Text))
                    {
                        var id = passages.Count;
                        var normalizedText = Normalize(passageText);
                        passages.Add(new IndexedPassage(item.Title, passageText, normalizedText));

                        foreach (var token in Tokenize(item.Title + " " + passageText))
                            AddPosting(postings, token, id);

                        foreach (var titleToken in Tokenize(item.Title))
                        {
                            if (titleToken.Length >= 5)
                                AddPosting(titlePrefixPostings, titleToken[..Math.Min(7, titleToken.Length)], id);
                        }
                    }
                }
                catch { }
            }
        }
        catch { }

        return new KnowledgeIndex(corpusTicks, documents, passages, postings, titlePrefixPostings);
    }

    private void SaveIndex(KnowledgeIndex index)
    {
        try
        {
            var temp = _indexPath + ".tmp";
            var options = new JsonSerializerOptions { WriteIndented = false };
            File.WriteAllText(temp, JsonSerializer.Serialize(index, options), new UTF8Encoding(false));
            File.Move(temp, _indexPath, true);
        }
        catch { }
    }

    private static void AddPosting(Dictionary<string, List<int>> map, string token, int id)
    {
        if (!map.TryGetValue(token, out var list))
        {
            list = new List<int>();
            map[token] = list;
        }
        if (list.Count == 0 || list[^1] != id) list.Add(id);
    }

    private static KnowledgeIndex EmptyIndex(long ticks)
        => new(ticks, new List<KnowledgeDocument>(), new List<IndexedPassage>(),
            new Dictionary<string, List<int>>(StringComparer.Ordinal),
            new Dictionary<string, List<int>>(StringComparer.Ordinal));

    private static IEnumerable<string> SplitPassages(string text)
    {
        var pieces = Regex.Split(text, @"(?<=[.!?])\s+|\n+");
        foreach (var piece in pieces)
        {
            var clean = piece.Trim();
            if (clean.Length >= 45) yield return clean.Length > 650 ? clean[..650] : clean;
        }
    }

    private static int Score(List<string> terms, string normalizedText, string normalizedTitle, int exactTitleHits, int fuzzyTitleHits)
    {
        var score = exactTitleHits * 40 + Math.Max(0, fuzzyTitleHits - exactTitleHits) * 10;
        foreach (var term in terms)
        {
            var textHits = CountWhole(normalizedText, term);
            if (textHits > 0) score += Math.Min(12, textHits * 4);
            if (ExactTitleMatch(normalizedTitle, term)) score += 25;
            else if (FuzzyTitleMatch(normalizedTitle, term)) score += 5;
        }

        var coverage = terms.Count(t => ContainsTopic(normalizedText, t));
        if (coverage >= 2) score += coverage * 10;
        return score;
    }

    private static bool ExactTitleMatch(string title, string term)
        => title.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(word => string.Equals(word, term, StringComparison.Ordinal));

    private static bool FuzzyTitleMatch(string title, string term)
    {
        if (ExactTitleMatch(title, term)) return true;
        return term.Length >= 5 && title.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(word => word.StartsWith(term, StringComparison.Ordinal) && word.Length <= term.Length + 5);
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
    private sealed record IndexedPassage(string Title, string Text, string NormalizedText);
    private sealed record KnowledgeIndex(
        long CorpusWriteTicks,
        List<KnowledgeDocument> Documents,
        List<IndexedPassage> Passages,
        Dictionary<string, List<int>> Postings,
        Dictionary<string, List<int>> TitlePrefixPostings);
}

internal readonly record struct KnowledgeMatch(string Title, string Text, int Score);
