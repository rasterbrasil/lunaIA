using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace LunaPC;

/// <summary>
/// Phase 2.2: obtains training material from a public, openly licensed source.
/// This is data collection only: no external AI model, inference engine or API is used.
/// The first source is Portuguese Wikipedia (CC BY-SA), accessed through its public API.
/// </summary>
internal sealed class InternetTrainingService
{
    private const string Endpoint = "https://pt.wikipedia.org/w/api.php";
    private const int Batches = 3;
    private const int PagesPerBatch = 20;
    private const int MaxDocumentCharacters = 6000;
    private const int TrainingChunks = 24;
    private const int ChunkCharacters = 900;

    private readonly string _brainDirectory;
    private readonly string _corpusPath;
    private readonly HttpClient _http;

    public InternetTrainingService(string brainDirectory)
    {
        _brainDirectory = brainDirectory;
        Directory.CreateDirectory(_brainDirectory);
        _corpusPath = Path.Combine(_brainDirectory, "internet-corpus-v1.jsonl");
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(25)
        };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LunaPC", "2.2"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<InternetTrainingResult> CollectAndTrainAsync(NativeTransformerBrain brain, CancellationToken ct = default)
    {
        var documents = await CollectAsync(ct);
        if (documents.Count == 0)
            return new InternetTrainingResult(0, 0, "Nenhum documento novo foi obtido.");

        await SaveCorpusAsync(documents, ct);

        var chunks = BuildTrainingChunks(documents);
        var trained = 0;
        foreach (var chunk in chunks.Take(TrainingChunks))
        {
            ct.ThrowIfCancellationRequested();
            brain.Train(chunk, epochs: 1, learningRate: 0.0003f, ct);
            trained++;
            await Task.Yield();
        }

        return new InternetTrainingResult(documents.Count, trained, _corpusPath);
    }

    private async Task<List<InternetDocument>> CollectAsync(CancellationToken ct)
    {
        var result = new List<InternetDocument>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var batch = 0; batch < Batches; batch++)
        {
            ct.ThrowIfCancellationRequested();
            var url = Endpoint +
                "?action=query&generator=random&grnnamespace=0&grnlimit=" + PagesPerBatch +
                "&prop=extracts&explaintext=1&exintro=0&format=json&formatversion=2";

            using var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) continue;
            var json = await response.Content.ReadAsStringAsync(ct);

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("query", out var query) ||
                !query.TryGetProperty("pages", out var pages)) continue;

            foreach (var page in pages.EnumerateArray())
            {
                var title = page.TryGetProperty("title", out var t) ? t.GetString() : null;
                var extract = page.TryGetProperty("extract", out var e) ? e.GetString() : null;
                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(extract)) continue;

                var cleaned = Clean(extract);
                if (cleaned.Length < 120) continue;
                if (!seen.Add(title)) continue;
                if (cleaned.Length > MaxDocumentCharacters) cleaned = cleaned[..MaxDocumentCharacters];

                result.Add(new InternetDocument(title, cleaned));
            }
        }

        return result;
    }

    private async Task SaveCorpusAsync(List<InternetDocument> documents, CancellationToken ct)
    {
        await using var stream = new FileStream(_corpusPath, FileMode.Append, FileAccess.Write, FileShare.Read);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        foreach (var document in documents)
        {
            ct.ThrowIfCancellationRequested();
            var line = JsonSerializer.Serialize(document);
            await writer.WriteLineAsync(line);
        }
    }

    private static IEnumerable<string> BuildTrainingChunks(IEnumerable<InternetDocument> documents)
    {
        var corpus = new StringBuilder();
        corpus.AppendLine("Conhecimento obtido de fonte pública licenciada para treinamento local.");
        foreach (var d in documents)
        {
            corpus.AppendLine("Título: " + d.Title);
            corpus.AppendLine(d.Text);
            corpus.AppendLine();
        }

        var text = corpus.ToString();
        for (var start = 0; start < text.Length; start += ChunkCharacters)
        {
            var length = Math.Min(ChunkCharacters, text.Length - start);
            if (length >= 120) yield return text.Substring(start, length);
        }
    }

    private static string Clean(string text)
    {
        var sb = new StringBuilder(text.Length);
        var previousWhitespace = false;
        foreach (var c in text)
        {
            if (char.IsControl(c) && c is not '\n' and not '\r' and not '\t') continue;
            if (char.IsWhiteSpace(c))
            {
                if (previousWhitespace) continue;
                sb.Append(c == '\n' || c == '\r' ? '\n' : ' ');
                previousWhitespace = true;
            }
            else
            {
                sb.Append(c);
                previousWhitespace = false;
            }
        }
        return sb.ToString().Trim();
    }

    private sealed record InternetDocument(string Title, string Text);
}

internal readonly record struct InternetTrainingResult(int Documents, int TrainingChunks, string CorpusPath);
