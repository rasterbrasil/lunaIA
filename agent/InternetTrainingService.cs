using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace LunaPC;

internal sealed class InternetTrainingService : IDisposable
{
    private const string Endpoint = "https://pt.wikipedia.org/w/api.php";
    private const int Batches = 3;
    private const int PagesPerBatch = 20;
    private const int MaxDocumentCharacters = 6000;
    private const int TrainingChunks = 24;
    private const int ChunkCharacters = 900;

    private readonly string _brainDirectory;
    private readonly string _corpusPath;
    private readonly string _statusPath;
    private readonly HttpClient _http;
    private TrainingProgressForm? _progressForm;
    private Thread? _progressThread;
    private readonly object _progressSync = new();

    public InternetTrainingService(string brainDirectory)
    {
        _brainDirectory = brainDirectory;
        Directory.CreateDirectory(_brainDirectory);
        _corpusPath = Path.Combine(_brainDirectory, "internet-corpus-v1.jsonl");
        _statusPath = Path.Combine(_brainDirectory, "internet-training-status.json");
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LunaPC", "2.2"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public InternetTrainingStatus GetStatus()
    {
        var documents = CountCorpusDocuments();
        var trainedChunks = 0;
        var running = false;
        try
        {
            if (File.Exists(_statusPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(_statusPath));
                var root = doc.RootElement;
                if (root.TryGetProperty("trainedChunks", out var c)) trainedChunks = c.GetInt32();
                if (root.TryGetProperty("running", out var r)) running = r.GetBoolean();
            }
        }
        catch { }
        return new InternetTrainingStatus(running, documents, trainedChunks, File.Exists(_corpusPath));
    }

    private int CountCorpusDocuments()
    {
        try
        {
            if (!File.Exists(_corpusPath)) return 0;
            return File.ReadLines(_corpusPath).Count(line => !string.IsNullOrWhiteSpace(line));
        }
        catch { return 0; }
    }

    private void SaveStatus(bool running, int documents, int trainedChunks)
    {
        try
        {
            var json = JsonSerializer.Serialize(new { running, documents, trainedChunks, updatedAt = DateTimeOffset.Now });
            File.WriteAllText(_statusPath, json, new UTF8Encoding(false));
        }
        catch { }
    }

    public async Task<InternetTrainingResult> CollectAndTrainAsync(NativeTransformerBrain brain, CancellationToken ct = default)
    {
        StartProgressWindow();
        SaveStatus(true, CountCorpusDocuments(), 0);
        Report("Conectando à fonte pública...", 2, 0, 0, 0, Batches + TrainingChunks + 2, "Iniciando coleta de dados da internet.");
        try
        {
            var documents = await CollectAsync(ct);
            if (documents.Count == 0)
            {
                SaveStatus(false, CountCorpusDocuments(), 0);
                Report("Nenhum documento foi obtido", 100, 0, 0, Batches + 1, Batches + TrainingChunks + 2, "Verifique a conexão com a internet e tente novamente.");
                return new InternetTrainingResult(0, 0, "Nenhum documento novo foi obtido.");
            }
            Report("Salvando corpus local...", 55, documents.Count, 0, Batches + 1, Batches + TrainingChunks + 2, $"{documents.Count} documentos válidos coletados.");
            await SaveCorpusAsync(documents, ct);
            var trained = 0;
            var chunks = BuildTrainingChunks(documents).Take(TrainingChunks).ToList();
            foreach (var chunk in chunks)
            {
                ct.ThrowIfCancellationRequested();
                var percent = 55 + (int)(43.0 * (trained + 1) / Math.Max(1, chunks.Count));
                Report($"Treinando os pesos do Transformer... bloco {trained + 1}/{chunks.Count}", percent, documents.Count, trained + 1, Batches + 1 + trained + 1, Batches + chunks.Count + 2, $"Backpropagation + AdamW no bloco {trained + 1}.");
                brain.Train(chunk, epochs: 1, learningRate: 0.0003f, ct: ct);
                trained++;
                SaveStatus(true, CountCorpusDocuments(), trained);
                await Task.Yield();
            }
            SaveStatus(false, CountCorpusDocuments(), trained);
            Report("Treinamento concluído", 100, documents.Count, trained, Batches + chunks.Count + 2, Batches + chunks.Count + 2, $"Checkpoint salvo localmente. {documents.Count} documentos / {trained} blocos.");
            await Task.Delay(900);
            return new InternetTrainingResult(documents.Count, trained, _corpusPath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SaveStatus(false, CountCorpusDocuments(), 0);
            Report("Treinamento interrompido por erro", 100, 0, 0, 0, 1, ex.Message);
            throw;
        }
        catch (OperationCanceledException)
        {
            SaveStatus(false, CountCorpusDocuments(), 0);
            throw;
        }
    }

    private void StartProgressWindow()
    {
        lock (_progressSync)
        {
            if (_progressThread is { IsAlive: true }) return;
            _progressThread = new Thread(() =>
            {
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                using var form = new TrainingProgressForm();
                _progressForm = form;
                Application.Run(form);
                _progressForm = null;
            });
            _progressThread.IsBackground = true;
            _progressThread.SetApartmentState(ApartmentState.STA);
            _progressThread.Start();
        }
    }

    private void Report(string status, int percent, int documents, int chunks, int step, int totalSteps, string log)
    {
        var progress = new TrainingProgress(status, percent, documents, chunks, step, totalSteps, log);
        var form = _progressForm;
        if (form is not null && !form.IsDisposed)
        {
            try { form.Report(progress); } catch { }
        }
    }

    private async Task<List<InternetDocument>> CollectAsync(CancellationToken ct)
    {
        var result = new List<InternetDocument>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var batch = 0; batch < Batches; batch++)
        {
            ct.ThrowIfCancellationRequested();
            Report($"Coletando dados da internet... lote {batch + 1}/{Batches}", 5 + batch * 16, result.Count, 0, batch + 1, Batches + TrainingChunks + 2, $"Consultando Wikipédia em português (lote {batch + 1}).");
            var url = Endpoint + "?action=query&generator=random&grnnamespace=0&grnlimit=" + PagesPerBatch + "&prop=extracts&explaintext=1&exintro=0&format=json&formatversion=2";
            using var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) continue;
            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("query", out var query) || !query.TryGetProperty("pages", out var pages)) continue;
            foreach (var page in pages.EnumerateArray())
            {
                var title = page.TryGetProperty("title", out var t) ? t.GetString() : null;
                var extract = page.TryGetProperty("extract", out var e) ? e.GetString() : null;
                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(extract)) continue;
                var cleaned = Clean(extract);
                if (cleaned.Length < 120 || !seen.Add(title)) continue;
                if (cleaned.Length > MaxDocumentCharacters) cleaned = cleaned[..MaxDocumentCharacters];
                result.Add(new InternetDocument(title, cleaned));
            }
            Report($"Lote {batch + 1}/{Batches} recebido", 18 + (batch + 1) * 12, result.Count, 0, batch + 1, Batches + TrainingChunks + 2, $"Documentos válidos até agora: {result.Count}.");
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
            await writer.WriteLineAsync(JsonSerializer.Serialize(document));
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

    public void Dispose()
    {
        _http.Dispose();
        try { if (_progressForm is { IsDisposed: false }) _progressForm.BeginInvoke(new Action(_progressForm.Close)); } catch { }
    }

    private sealed record InternetDocument(string Title, string Text);
}

internal readonly record struct InternetTrainingResult(int Documents, int TrainingChunks, string CorpusPath);
internal readonly record struct InternetTrainingStatus(bool Running, int Documents, int TrainedChunks, bool CorpusExists);
