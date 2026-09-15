using LLama;
using LLama.Common;
using LLama.Sampling;
using LLama.Transformers;
using System.Net;
using System.Net.Http;

namespace LunaPC;

internal sealed class LunaLocalLanguageEngine : IDisposable
{
    private const string ModelFileName = "Qwen3-4B-Q4_K_M.gguf";
    private const string ModelUrl = "https://huggingface.co/Qwen/Qwen3-4B-GGUF/resolve/main/Qwen3-4B-Q4_K_M.gguf?download=true";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _modelDirectory;
    private readonly string _modelPath;
    private LLamaWeights? _weights;
    private bool _modelLoaded;
    private bool _disposed;

    public bool IsModelReady => File.Exists(_modelPath) && _modelLoaded;
    public string ModelPath => _modelPath;

    public LunaLocalLanguageEngine()
    {
        _modelDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LunaIA", "models");
        Directory.CreateDirectory(_modelDirectory);
        _modelPath = Path.Combine(_modelDirectory, ModelFileName);
    }

    public Task<string> ChatAsync(string userText, string systemPrompt, CancellationToken cancellationToken = default)
        => ChatAsync(userText, systemPrompt, maxTokens: 384, contextSize: 4096, disableThinking: true, cancellationToken);

    public async Task<string> ChatAsync(
        string userText,
        string systemPrompt,
        int maxTokens,
        uint contextSize,
        bool disableThinking,
        CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LunaLocalLanguageEngine));
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);

            // Model weights stay loaded once. Each request gets a fresh context/KV
            // cache, which prevents state leaking between independent requests.
            var parameters = new ModelParams(_modelPath)
            {
                ContextSize = contextSize,
                GpuLayerCount = 0
            };

            using var context = await Task.Run(() => _weights!.CreateContext(parameters), cancellationToken);
            var executor = new InteractiveExecutor(context);

            var history = new ChatHistory();
            history.AddMessage(AuthorRole.System, systemPrompt);
            var session = new ChatSession(executor, history);
            session.WithHistoryTransform(new PromptTemplateTransformer(_weights!, withAssistant: true));

            // Qwen3 enables reasoning by default. For the fast local assistant and
            // especially for planning JSON, disable thinking so the first real user
            // request does not spend most of its budget in hidden <think> tokens.
            var effectiveUserText = disableThinking
                ? $"{userText.Trim()} /no_think"
                : userText;

            var inference = new InferenceParams
            {
                MaxTokens = maxTokens,
                SamplingPipeline = new DefaultSamplingPipeline
                {
                    Temperature = disableThinking ? 0.70f : 0.60f,
                    TopP = disableThinking ? 0.80f : 0.95f,
                    TopK = 20
                },
                AntiPrompts = ["<|im_end|>", "<|endoftext|>"]
            };

            var pieces = new List<string>();
            await foreach (var piece in session.ChatAsync(
                new ChatHistory.Message(AuthorRole.User, effectiveUserText),
                inference,
                cancellationToken))
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
        if (_modelLoaded) return;
        if (!File.Exists(_modelPath)) await DownloadModelAsync(cancellationToken);

        var parameters = new ModelParams(_modelPath)
        {
            ContextSize = 4096,
            GpuLayerCount = 0
        };

        _weights = await Task.Run(() => LLamaWeights.LoadFromFile(parameters), cancellationToken);
        _modelLoaded = true;
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
        _modelLoaded = false;
        _weights?.Dispose();
        _weights = null;
        _gate.Dispose();
    }
}
