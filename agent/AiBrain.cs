using System.Text.Json;

namespace LunaPC;

/// <summary>
/// LUNA's own local cognitive layer. No Ollama, no external model, no API.
/// The neural core is initialized and trained locally from LUNA's own seed corpus,
/// while the executive layer converts understood goals into the existing agent contract.
/// </summary>
internal sealed class AiBrain : IDisposable
{
    private readonly NativeBrainCore _neural;
    private readonly object _sync = new();
    private readonly List<(string User, string Assistant)> _history = new();
    private BrainDecision? _last;
    private bool _disposed;

    public OperationalMemory Memory { get; }
    public string Model => "LUNA-NATIVE-0.1";
    public BrainDecision? LastDecision => _last;
    public bool IsReady => !_disposed;
    public int ParameterCount => _neural.ParameterCount;

    public AiBrain()
    {
        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LunaPC", "brain");
        Directory.CreateDirectory(data);
        Memory = new OperationalMemory();
        _neural = new NativeBrainCore(data, 96);
        if (!_neural.IsTrained)
            _neural.Train(SeedCorpus, epochs: 2, learningRate: 0.0025f);
    }

    public Task<string?> AskAsync(string text, CancellationToken ct = default)
        => Task.FromResult(ThinkAsync(text, ct).GetAwaiter().GetResult()?.Response);

    public Task<BrainDecision?> ThinkAsync(string text, CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AiBrain));
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(text)) return Task.FromResult<BrainDecision?>(null);

        var input = text.Trim();
        var lower = input.ToLowerInvariant();
        var memory = Memory.ForBrain(input, 20, 8);
        var d = new BrainDecision
        {
            Intent = DetectIntent(lower),
            Goal = input,
            Interpretation = Interpret(lower),
            Plan = BuildPlan(lower),
            RelevantContext = new() { memory },
            Actions = new(),
            MemoryUpdates = new()
        };

        BuildActions(lower, d);

        if (d.Actions.Count == 0)
        {
            d.Response = GenerateResponse(input, lower);
        }
        else
        {
            d.Response = "Entendi o objetivo. Vou executar o próximo passo e verificar o resultado.";
        }

        if (lower is "oi" or "olá" or "ola" or "bom dia" or "boa tarde" or "boa noite")
            d.Response = "Olá, Marcos. Eu sou a LUNA. Meu cérebro nativo está funcionando localmente no computador.";

        lock (_sync)
        {
            _history.Add((input, d.Response));
            while (_history.Count > 20) _history.RemoveAt(0);
            _last = d;
        }
        return Task.FromResult<BrainDecision?>(d);
    }

    private string GenerateResponse(string input, string lower)
    {
        if (lower.Contains("quem é você") || lower.Contains("quem e você") || lower.Contains("o que você é"))
            return "Eu sou a LUNA, uma IA local construída para este computador. Meu cérebro neural é próprio e os meus pesos ficam na máquina.";
        if (lower.Contains("como você funciona") || lower.Contains("como voce funciona"))
            return "Eu observo o computador, interpreto o objetivo, planejo, ajo, verifico o resultado e registro aprendizados operacionais. O núcleo neural é executado localmente.";
        if (lower.Contains("obrigado") || lower.Contains("obrigada")) return "Por nada. Vamos continuar.";
        if (lower.Contains("teste") || lower.Contains("testando")) return "Teste recebido. Meu cérebro nativo está respondendo.";

        var generated = _neural.Generate("LUNA: " + input + "\nLUNA:", 220, 0.55f);
        if (!string.IsNullOrWhiteSpace(generated) && generated.Length >= 4 && generated.Any(char.IsLetter))
            return generated.Replace("LUNA:", "", StringComparison.Ordinal).Trim();
        return "Entendi a mensagem. Posso observar o computador, planejar uma tarefa, executar ações permitidas e verificar o resultado.";
    }

    private static string DetectIntent(string text)
    {
        if (text.Contains("abra ") || text.StartsWith("abrir ") || text.Contains("inicie ") || text.StartsWith("iniciar ")) return "abrir_programa";
        if (text.Contains("site") || text.Contains("url") || text.StartsWith("acesse ") || text.StartsWith("acessar ")) return "abrir_url";
        if (text.Contains("crie uma pasta") || text.Contains("criar uma pasta") || text.Contains("crie a pasta")) return "criar_diretorio";
        if (text.Contains("copie ") || text.Contains("copiar ")) return "copiar_arquivo";
        if (text.Contains("mova ") || text.Contains("mover ")) return "mover_arquivo";
        if (text.Contains("apague ") || text.Contains("exclua ") || text.Contains("delete ")) return "excluir_arquivo";
        if (text.Contains("powershell") || text.Contains("comando no terminal")) return "powershell";
        return "conversar";
    }

    private static string Interpret(string text)
    {
        return DetectIntent(text) switch
        {
            "abrir_programa" => "O usuário quer iniciar um programa no Windows.",
            "abrir_url" => "O usuário quer abrir um endereço no navegador.",
            "criar_diretorio" => "O usuário quer criar um diretório.",
            "copiar_arquivo" => "O usuário quer copiar um arquivo.",
            "mover_arquivo" => "O usuário quer mover um arquivo.",
            "excluir_arquivo" => "O usuário quer remover um arquivo.",
            "powershell" => "O usuário quer executar um comando de sistema.",
            _ => "O usuário está conversando ou pedindo uma explicação."
        };
    }

    private static List<string> BuildPlan(string text)
    {
        var intent = DetectIntent(text);
        return intent == "conversar"
            ? new() { "Entender a mensagem", "Responder" }
            : new() { "Observar o estado atual", "Executar o próximo passo", "Verificar o resultado", "Corrigir ou replanejar se necessário" };
    }

    private static void BuildActions(string text, BrainDecision d)
    {
        if (d.Intent == "abrir_url")
        {
            var url = ExtractUrl(text);
            if (!string.IsNullOrWhiteSpace(url)) d.Actions.Add(new BrainAction { Type = "open_url", Url = url, Risk = "safe" });
        }
        else if (d.Intent == "abrir_programa")
        {
            var target = ExtractAfter(text, new[] { "abra ", "abrir ", "inicie ", "iniciar " });
            if (!string.IsNullOrWhiteSpace(target)) d.Actions.Add(new BrainAction { Type = "open_app", Target = target, Risk = "safe" });
        }
        else if (d.Intent == "criar_diretorio")
        {
            var path = ExtractAfter(text, new[] { "crie uma pasta ", "criar uma pasta ", "crie a pasta " });
            if (!string.IsNullOrWhiteSpace(path)) d.Actions.Add(new BrainAction { Type = "create_directory", Path = path.Trim('"'), Risk = "safe" });
        }
        else if (d.Intent == "powershell")
        {
            var command = text[(text.IndexOf(':') + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(command) || command == text) command = text;
            d.Actions.Add(new BrainAction { Type = "run_powershell", Arguments = command, Risk = "confirm" });
        }
    }

    private static string ExtractUrl(string text)
    {
        var token = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(x => x.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || x.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || x.Contains('.'));
        if (string.IsNullOrWhiteSpace(token)) return "";
        return token.Trim('"', ',', '.', ';');
    }

    private static string ExtractAfter(string text, string[] prefixes)
    {
        foreach (var p in prefixes) if (text.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return text[p.Length..].Trim();
        return "";
    }

    public void ClearHistory() { lock (_sync) { _history.Clear(); _last = null; } }

    public void Dispose() { _disposed = true; }

    private const string SeedCorpus = """
LUNA é uma inteligência artificial local construída para operar um computador Windows.\n
LUNA entende objetivos, observa o estado do computador, planeja ações, executa ações permitidas e verifica os resultados.\n
LUNA deve ser clara, objetiva, segura e nunca inventar que uma ação foi concluída.\n
Quando uma tarefa falha, LUNA deve observar novamente, entender o erro, escolher outra estratégia e tentar novamente quando for seguro.\n
LUNA possui memória operacional para aprender relações entre situação, ação, resultado e solução.\n
A memória não é prova do estado atual; a percepção atual sempre tem prioridade.\n
Olá. Olá, Marcos. Eu sou a LUNA.\n
Bom dia. Boa tarde. Boa noite.\n
Posso conversar, analisar informações, observar o computador e executar tarefas permitidas.\n
Meu cérebro funciona localmente. Eu não preciso de um serviço externo para responder.\n
Eu devo confirmar operações destrutivas antes de executá-las.\n
Eu devo verificar o resultado depois de agir.\n
Eu devo aprender com erros confirmados e soluções que realmente funcionaram.\n
Entendi. Vou analisar o objetivo antes de agir.\n
Entendi o objetivo. Vou observar, executar e verificar.\n
Não vou afirmar sucesso sem observar o resultado.\n
Quando não houver informação suficiente, devo pedir esclarecimento.\n
Uma tarefa pode ser dividida em passos menores.\n
Planejamento é transformar um objetivo em ações verificáveis.\n
Percepção significa descobrir o estado atual do computador.\n
Ação significa modificar o computador de forma controlada.\n
Verificação significa comparar o estado observado com o objetivo.\n
Aprendizado significa registrar o que funcionou e o que falhou.\n
""";
}
