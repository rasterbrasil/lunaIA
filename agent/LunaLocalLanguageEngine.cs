using LLama;
using LLama.Common;
using LLama.Sampling;
using LLama.Transformers;
using System.Net;

namespace LunaPC;

/// <summary>
/// Real on-device language engine. It runs a GGUF model through llama.cpp via
/// LLamaSharp. No Ollama process and no cloud API are required for inference.
/// The model is downloaded once to the user's local model directory and then
/// reused offline.
/// </summary>
internal sealed class LunaLocalLanguageEngine : IDisposable
{
    private const string ModelFileName = "Qwen3-4B-Q4_K_M.gguf";
    private const string ModelUrl = "https://huggingface.co/Qwen/Qwen3-4B-GGUF/resolve/main/Qwen3-4B-Q4_K_M.gguf?download=true";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _modelDirectory;
    private readonly string _modelPath;
    private LLamaWeights? _weights;
    private LLamaContext? _context;
    private InteractiveExecutor? _executor;
    private ChatSession? _session;
    private bool _disposed;

    public bool IsModelReady => File.Exists(_modelPath) && _session is not null;
    public string ModelPath => _modelPath;

    public LunaLocalLanguageEngine()
    {
        _modelDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LunaIA", "models");
        Directory.CreateDirectory(_modelDirectory);
        _modelPath = Path.Combine(_modelDirectory, ModelFileName);
    }

    public async Task<string> ChatAsync(string userText, string systemPrompt, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LunaLocalLanguageEngine));
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);

            var history = new ChatHistory();
            history.AddMessage(AuthorRole.System, systemPrompt);
            history.AddMessage(AuthorRole.User, userText);

            var inference = new InferenceParams
            {
                MaxTokens = 512,
                SamplingPipeline = new DefaultSamplingPipeline
                {
                    Temperature = 0.55f,
                    TopP = 0.90f,
                    TopK = 20
                },
                AntiPrompts = ["<|im_end|>", "<|endoftext|>"]
            };

            var pieces = new List<string>();
            await foreach (var piece in _session!.ChatAsync(history, inference, cancellationToken))
            {
                if (!string.IsNullOrEmpty(piece)) pieces.Add(piece);
            }

            var answer = string.Concat(pieces).Trim();
            return string.IsNullOrWhiteSpace(answer)
                ? "Meu motor local terminou o raciocínio sem produzir uma resposta textual."
                : answer;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_session is not null) return;
        if (!File.Exists(_modelPath))
            await DownloadModelAsync(cancellationToken);

        var parameters = new ModelParams(_modelPath)
        {
            ContextSize = 8192,
            GpuLayerCount = 0,
            Seed = 42
        };

        _weights = await Task.Run(() => LLamaWeights.LoadFromFile(parameters), cancellationToken);
        _context = await Task.Run(() => _weights.CreateContext(parameters), cancellationToken);
        _executor = new InteractiveExecutor(_context);
        _session = new ChatSession(_executor);
        _session.WithHistoryTransform(new PromptTemplateTransformer(_weights, withAssistant: true));
    }

    private async Task DownloadModelAsync(CancellationToken cancellationToken)
    {
        var temp = _modelPath + ".download";
        if (File.Exists(temp))
        {
            try { File.Delete(temp); } catch { }
        }

        using var client = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All
        })
        {
            Timeout = TimeSpan.FromMinutes(30)
        };
        using var response = await client.GetAsync(ModelUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true);
        await source.CopyToAsync(target, 1024 * 1024, cancellationToken);
        await target.FlushAsync(cancellationToken);
        target.Close();
        File.Move(temp, _modelPath, overwrite: true);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session = null;
        _executor = null;
        _context?.Dispose();
        _weights?.Dispose();
        _context = null;
        _weights = null;
        _gate.Dispose();
    }
}
