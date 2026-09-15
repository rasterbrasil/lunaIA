using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace LunaPC;

internal sealed class AiBrain : IDisposable
{
    private const string DefaultEndpoint = "http://127.0.0.1:11434/api/chat";
    private const string DefaultModel = "qwen3:4b-instruct";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(90);
    private const int MaxHistoryMessages = 20;
    private readonly HttpClient _http;
    private readonly string _endpoint;
    private readonly string _model;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly object _historyLock = new();
    private readonly List<ChatMessage> _history = new();
    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };
    private Process? _ollamaProcess;
    private bool _disposed;
    private BrainDecision? _lastDecision;
    private sealed record ChatMessage(string role, string content);

    private const string SystemPrompt = """
Você é LUNA, uma IA pessoal privada no Windows. Você entende linguagem natural e agora também possui ferramentas de AÇÃO.
Sempre: entenda o pedido, use contexto, determine objetivo, planeje e, quando o pedido exigir ação no computador, gere ações estruturadas.

Tipos de ação disponíveis:
open_app = abrir aplicativo/arquivo/pasta pelo Windows
open_url = abrir site/URL no navegador
click = clicar em coordenadas da tela; use X/Y somente quando forem conhecidos pela percepção ou pelo contexto
move_mouse = mover o mouse para X/Y sem clicar
type_text = digitar texto
key = pressionar tecla ou combinação (ex.: ENTER, CTRL+L, CTRL+C)
create_directory = criar pasta
copy_file = copiar arquivo
move_file = mover/renomear arquivo
delete_file = excluir arquivo (sempre risk=confirm)
run_process = executar um programa com argumentos
run_powershell = executar tarefa PowerShell; use risk=confirm, exceto consultas claramente somente-leitura

Retorne SOMENTE JSON válido:
{
  "intent":"categoria",
  "goal":"objetivo",
  "interpretation":"interpretação curta",
  "plan":["etapa"],
  "assumptions":["suposição"],
  "relevantContext":["contexto"],
  "needsClarification":false,
  "clarificationQuestion":"",
  "actions":[{"type":"open_app","target":"notepad.exe","value":"","path":"","destination":"","url":"","arguments":"","x":0,"y":0,"risk":"safe"}],
  "response":"resposta natural"
}

Regras de ação:
- Não invente que uma ação foi executada; o executor fará isso depois.
- Gere actions=[] quando o pedido for apenas conversa, explicação ou planejamento sem execução.
- Use o menor número de ações necessário e preserve a ordem do plano.
- Não use comandos fixos por frase. Escolha a ação pelo significado do pedido.
- Para abrir programas, prefira target com nome/executável conhecido.
- Para sites, use open_url e uma URL válida.
- Para digitação, type_text contém exatamente o texto a digitar.
- Para teclado, key contém a combinação.
- Operações destrutivas (excluir arquivos, apagar dados, comandos administrativos ou scripts potencialmente destrutivos) devem usar risk=confirm.
- Nunca tente burlar segurança, antivírus, permissões ou controles do Windows.
- Se faltar caminho, programa, destino ou outra informação essencial, needsClarification=true e actions=[] em vez de adivinhar.
- Não exponha cadeia de pensamento privada; plan e interpretation são apenas resumos úteis.
""";

    public AiBrain()
    {
        _http = new HttpClient { Timeout = RequestTimeout };
        _endpoint = Environment.GetEnvironmentVariable("LUNA_LOCAL_AI_URL") ?? DefaultEndpoint;
        _model = Environment.GetEnvironmentVariable("LUNA_LOCAL_AI_MODEL") ?? DefaultModel;
    }
    public string Model => _model;
    public BrainDecision? LastDecision => _lastDecision;
    public async Task<string?> AskAsync(string userText, CancellationToken cancellationToken = default) => (await ThinkAsync(userText, cancellationToken))?.Response;

    public async Task<BrainDecision?> ThinkAsync(string userText, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AiBrain));
        if (string.IsNullOrWhiteSpace(userText)) return null;
        await _requestGate.WaitAsync(cancellationToken);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);
            await EnsureLocalEngineAsync(timeout.Token);
            var messages = BuildMessages(userText);
            var payload = new { model = _model, stream = false, keep_alive = "10m", format = "json", options = new { temperature = 0.25, num_ctx = 8192 }, messages };
            var raw = await SendChatAsync(payload, timeout.Token);
            var decision = ParseDecision(raw, userText);
            if (decision is null) return FallbackDecision(userText, "Não consegui estruturar meu raciocínio local agora. Tente novamente.");
            lock (_historyLock)
            {
                _history.Add(new ChatMessage("user", userText.Trim()));
                _history.Add(new ChatMessage("assistant", decision.Response.Trim()));
                while (_history.Count > MaxHistoryMessages) _history.RemoveAt(0);
            }
            _lastDecision = decision;
            return decision;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return FallbackDecision(userText, "Meu cérebro local demorou mais de 90 segundos para responder. Tente novamente."); }
        catch (HttpRequestException) { return FallbackDecision(userText, "Meu cérebro local não está disponível. Execute uma vez o instalador de preparação da LUNA."); }
        catch (JsonException) { return FallbackDecision(userText, "Recebi uma resposta inválida do cérebro local. Tente novamente."); }
        catch { return FallbackDecision(userText, "Meu cérebro local encontrou um problema ao processar essa mensagem. Tente novamente."); }
        finally { _requestGate.Release(); }
    }

    private List<ChatMessage> BuildMessages(string userText)
    {
        lock (_historyLock)
        {
            var messages = new List<ChatMessage> { new("system", SystemPrompt) };
            messages.AddRange(_history);
            messages.Add(new ChatMessage("user", userText.Trim()));
            return messages;
        }
    }

    private BrainDecision? ParseDecision(string? raw, string userText)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try
        {
            var decision = JsonSerializer.Deserialize<BrainDecision>(raw, _json);
            if (decision is null) return null;
            decision.Intent = Clean(decision.Intent, "conversar");
            decision.Goal = Clean(decision.Goal, userText);
            decision.Interpretation = Clean(decision.Interpretation, "Pedido interpretado a partir da mensagem atual e do contexto.");
            decision.Response = Clean(decision.Response, "Entendi o que você pediu.");
            decision.Plan ??= new List<string>();
            if (decision.Plan.Count == 0) decision.Plan.Add("Entender o pedido e responder de forma adequada.");
            decision.Assumptions ??= new List<string>();
            decision.RelevantContext ??= new List<string>();
            decision.Actions ??= new List<BrainAction>();
            foreach (var action in decision.Actions)
            {
                action.Type = action.Type?.Trim().ToLowerInvariant() ?? "";
                action.Risk = string.IsNullOrWhiteSpace(action.Risk) ? "safe" : action.Risk.Trim().ToLowerInvariant();
            }
            if (decision.NeedsClarification && string.IsNullOrWhiteSpace(decision.ClarificationQuestion)) decision.ClarificationQuestion = "Pode me dar mais detalhes para eu executar isso corretamente?";
            return decision;
        }
        catch
        {
            return new BrainDecision { Intent = "conversar", Goal = userText.Trim(), Interpretation = "O modelo retornou uma resposta não estruturada.", Plan = new List<string> { "Interpretar a mensagem", "Responder ao usuário" }, Response = raw.Trim() };
        }
    }
    private static BrainDecision FallbackDecision(string userText, string response) => new() { Intent = "erro_temporario", Goal = userText.Trim(), Interpretation = "Não foi possível concluir o processamento local.", Plan = new List<string> { "Tentar novamente quando o motor local estiver disponível" }, Response = response };
    private static string Clean(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private async Task<string?> SendChatAsync(object payload, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint) { Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json") };
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode) return JsonSerializer.Serialize(new { response = (int)response.StatusCode == 404 ? $"Meu cérebro local está instalado, mas o modelo {_model} ainda não foi encontrado." : $"Meu cérebro local respondeu com o código {(int)response.StatusCode}." });
        return ExtractMessageText(body);
    }

    private async Task EnsureLocalEngineAsync(CancellationToken cancellationToken)
    {
        var healthUrl = new Uri(new Uri(_endpoint), "/");
        try { using var response = await _http.GetAsync(healthUrl, cancellationToken); if (response.IsSuccessStatusCode) return; } catch (HttpRequestException) { }
        var ollama = FindOllama();
        if (ollama is null) throw new HttpRequestException("Ollama não encontrado");
        if (_ollamaProcess is null || _ollamaProcess.HasExited)
        {
            try { _ollamaProcess = new Process { StartInfo = new ProcessStartInfo { FileName = ollama, Arguments = "serve", UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } }; _ollamaProcess.Start(); } catch (InvalidOperationException) { }
        }
        for (var i = 0; i < 40; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { using var response = await _http.GetAsync(healthUrl, cancellationToken); if (response.IsSuccessStatusCode) return; } catch (HttpRequestException) { }
            await Task.Delay(250, cancellationToken);
        }
        throw new HttpRequestException("Motor local não respondeu");
    }
    private static string? FindOllama()
    {
        var candidates = new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", "ollama.exe"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Ollama", "ollama.exe"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Ollama", "ollama.exe") };
        return candidates.FirstOrDefault(File.Exists);
    }
    private static string? ExtractMessageText(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String) return content.GetString();
        return null;
    }
    public void ClearHistory() { lock (_historyLock) { _history.Clear(); _lastDecision = null; } }
    public void Dispose() { if (_disposed) return; _disposed = true; try { _ollamaProcess?.Dispose(); } catch { } _http.Dispose(); _requestGate.Dispose(); }
}
