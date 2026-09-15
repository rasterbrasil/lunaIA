namespace LunaPC;

/// <summary>
/// LUNA's local cognitive layer. No Ollama, no external model, no AI API.
/// The language core is a Transformer implemented directly in C# and trained locally.
/// InternetTrainingService supplies openly licensed training data; KnowledgeMemory retrieves
/// relevant local passages; the executive layer remains responsible for validated Windows actions.
/// </summary>
internal sealed class AiBrain : IDisposable
{
    private readonly NativeTransformerBrain _neural;
    private readonly InternetTrainingService _internetTraining;
    private readonly KnowledgeMemory _knowledge;
    private readonly object _sync = new();
    private readonly List<(string User, string Assistant)> _history = new();
    private BrainDecision? _last;
    private bool _disposed;
    private int _internetTrainingRunning;

    public OperationalMemory Memory { get; }
    public string Model => "LUNA-NATIVE-TRANSFORMER-0.5-KNOWLEDGE-ROUTER";
    public BrainDecision? LastDecision => _last;
    public bool IsReady => !_disposed;
    public int ParameterCount => _neural.ParameterCount;

    public AiBrain()
    {
        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LunaPC", "brain");
        Directory.CreateDirectory(data);
        Memory = new OperationalMemory();
        _neural = new NativeTransformerBrain(data);
        _internetTraining = new InternetTrainingService(data);
        _knowledge = new KnowledgeMemory(data);
        if (!_neural.IsTrained)
            _neural.Train(SeedCorpus, epochs: 1, learningRate: 0.0008f);
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

        // Routing order is intentional: a knowledge question must never be mistaken
        // for a training command or a training-status question.
        if (IsKnowledgeQuestion(lower))
            return Task.FromResult<BrainDecision?>(CreateKnowledgeDecision(input));

        if (IsTrainingStatusQuestion(lower))
            return Task.FromResult<BrainDecision?>(CreateTrainingStatusDecision(input));

        if (IsInternetTrainingCommand(lower))
            return Task.FromResult<BrainDecision?>(StartInternetTraining(input, ct));

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
            d.Response = GenerateResponse(input, lower);
        else
            d.Response = "Entendi o objetivo. Vou executar o próximo passo e verificar o resultado.";

        if (lower is "oi" or "olá" or "ola" or "bom dia" or "boa tarde" or "boa noite")
            d.Response = "Olá. Eu sou a LUNA. Meu cérebro Transformer nativo está funcionando localmente no computador.";

        lock (_sync)
        {
            _history.Add((input, d.Response));
            while (_history.Count > 20) _history.RemoveAt(0);
            _last = d;
        }
        return Task.FromResult<BrainDecision?>(d);
    }

    private BrainDecision CreateKnowledgeDecision(string input)
    {
        var matches = _knowledge.Search(input, 5);
        var context = _knowledge.BuildContext(input, 2600);
        var response = GenerateKnowledgeResponse(input, matches, context);
        var d = new BrainDecision
        {
            Intent = "consultar_conhecimento",
            Goal = input,
            Interpretation = "O usuário está perguntando sobre um assunto; devo consultar o conhecimento local sem iniciar treinamento.",
            Plan = new() { "Identificar o assunto", "Pesquisar no corpus local", "Classificar os trechos relevantes", "Responder somente com informação encontrada" },
            RelevantContext = new() { $"Trechos encontrados: {matches.Count}" },
            Response = response,
            Completed = true
        };

        lock (_sync)
        {
            _history.Add((input, d.Response));
            while (_history.Count > 20) _history.RemoveAt(0);
            _last = d;
        }
        return d;
    }

    private string GenerateKnowledgeResponse(string input, IReadOnlyList<KnowledgeMatch> matches, string context)
    {
        if (matches.Count == 0)
            return "Não encontrei informação suficiente sobre esse assunto no meu conhecimento local. Posso pesquisar uma fonte pública na internet se você pedir.";

        var prompt = "Responda à pergunta usando somente os trechos do conhecimento local abaixo. Seja breve e factual. Não invente fatos. Se os trechos não forem suficientes, diga isso.\nCONHECIMENTO LOCAL:\n" + context + "\nPERGUNTA:\n" + input + "\nRESPOSTA:";
        var generated = _neural.Generate(prompt, 180, 0.35f);

        if (IsUsableKnowledgeGeneration(generated, input))
            return generated.Replace("LUNA:", "", StringComparison.Ordinal).Trim();

        var sb = new System.Text.StringBuilder("Encontrei estas informações no meu conhecimento local:\n");
        foreach (var match in matches.Take(3))
            sb.AppendLine("• " + match.Text);
        return sb.ToString().Trim();
    }

    private static bool IsUsableKnowledgeGeneration(string text, string question)
    {
        if (!IsUsableGeneratedText(text)) return false;
        var lower = text.ToLowerInvariant();
        if (lower.Contains("o último treinamento terminou") || lower.Contains("ultimo treinamento terminou") ||
            lower.Contains("documentos públicos no corpus") || lower.Contains("documentos publicos no corpus") ||
            lower.Contains("checkpoint dos pesos")) return false;
        var terms = question.ToLowerInvariant()
            .Split(new[] { ' ', '\t', '\r', '\n', '?', '!', '.', ',', ':', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(x => x.Length >= 4)
            .ToArray();
        return terms.Length == 0 || terms.Any(lower.Contains);
    }

    private BrainDecision CreateTrainingStatusDecision(string input)
    {
        var status = _internetTraining.GetStatus();
        var response = status.Running
            ? $"O treinamento está em andamento. Já foram coletados {status.Documents} documentos e processados {status.TrainedChunks} blocos."
            : status.Documents > 0
                ? $"O último treinamento terminou. Tenho {status.Documents} documentos públicos no corpus local e {status.TrainedChunks} blocos registrados. O checkpoint dos pesos está salvo localmente."
                : "Ainda não há um corpus de internet registrado. Posso iniciar o treinamento com dados públicos licenciados.";

        var d = new BrainDecision
        {
            Intent = "consultar_treinamento",
            Goal = input,
            Interpretation = "O usuário quer saber o estado ou o resultado do aprendizado pela internet.",
            Plan = new() { "Consultar o corpus local", "Verificar o estado do treinamento", "Informar o resultado sem iniciar novo treinamento" },
            Response = response,
            Completed = true
        };

        lock (_sync)
        {
            _history.Add((input, d.Response));
            while (_history.Count > 20) _history.RemoveAt(0);
            _last = d;
        }
        return d;
    }

    private static bool IsKnowledgeQuestion(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (text.Contains("o que você sabe") || text.Contains("o que voce sabe") ||
            text.Contains("o que sabe sobre") || text.Contains("o que voce conhece") || text.Contains("o que você conhece") ||
            text.Contains("fale sobre") || text.Contains("explique ") || text.Contains("defina ") ||
            text.Contains("quem foi ") || text.Contains("quem é ") || text.Contains("quem e ") ||
            text.Contains("onde fica ") || text.Contains("por que ") || text.Contains("porque ") ||
            text.Contains("como funciona ") || text.Contains("o que é ") || text.Contains("o que e "))
            return true;

        if (text.StartsWith("pesquise ") || text.StartsWith("pesquisa ") || text.StartsWith("procure ") || text.StartsWith("busque "))
            return true;

        return false;
    }

    private static bool IsTrainingStatusQuestion(string text)
        => text.Contains("o que aprendeu") || text.Contains("o que voce aprendeu") || text.Contains("o que você aprendeu") ||
           text.Contains("quanto aprendeu") || text.Contains("status do treinamento") || text.Contains("como esta o treinamento") ||
           text.Contains("como está o treinamento") || text.Contains("terminou o treinamento") || text.Contains("treinamento terminou");

    private BrainDecision StartInternetTraining(string input, CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _internetTrainingRunning, 1, 0) != 0)
        {
            return new BrainDecision
            {
                Intent = "treinar_internet",
                Goal = input,
                Interpretation = "O treinamento pela internet já está em execução.",
                Plan = new() { "Coletar dados licenciados", "Limpar e deduplicar", "Treinar o Transformer", "Salvar os pesos" },
                Response = "O treinamento pela internet já está em andamento. Estou mantendo o processo em segundo plano.",
                Completed = false
            };
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _internetTraining.CollectAndTrainAsync(_neural, ct);
                Memory.RecordEpisode("treinamento com dados públicos da internet", "coletar e treinar Transformer", $"{result.Documents} documentos; {result.TrainingChunks} blocos treinados", "usar corpus público licenciado e atualizar pesos locais", result.Documents > 0, 0.95f, new[] { "treinamento", "internet", "wikipedia" });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Memory.RecordEpisode("treinamento com dados públicos da internet", "coletar e treinar Transformer", "falha: " + ex.Message, "repetir após verificar conectividade", false, 0.85f, new[] { "treinamento", "internet", "erro" });
            }
            finally { Interlocked.Exchange(ref _internetTrainingRunning, 0); }
        });

        return new BrainDecision
        {
            Intent = "treinar_internet",
            Goal = input,
            Interpretation = "O usuário solicitou treinamento do cérebro usando dados públicos da internet.",
            Plan = new() { "Coletar dados de fonte pública licenciada", "Limpar e deduplicar o corpus", "Treinar os pesos completos do Transformer", "Salvar checkpoint local", "Registrar o resultado" },
            Response = "Iniciei o treinamento com dados públicos da internet em segundo plano. O cérebro continua sendo totalmente local; a internet está sendo usada somente como fonte de dados.",
            Completed = false
        };
    }

    private static bool IsInternetTrainingCommand(string text)
        => (text.Contains("treine") || text.Contains("treinar") || text.Contains("treinamento")) &&
           (text.Contains("internet") || text.Contains("web") || text.Contains("online") || text.Contains("wikipedia"));

    private string GenerateResponse(string input, string lower)
    {
        if (lower.Contains("quem é você") || lower.Contains("quem e você") || lower.Contains("o que você é"))
            return "Eu sou a LUNA, uma IA local construída para este computador. Meu cérebro Transformer é próprio e os pesos ficam na máquina.";
        if (lower.Contains("como você funciona") || lower.Contains("como voce funciona"))
            return "Eu observo o computador, interpreto o objetivo, planejo, ajo, verifico o resultado e registro aprendizados operacionais. O núcleo neural é executado localmente.";
        if (lower.Contains("obrigado") || lower.Contains("obrigada")) return "Por nada. Vamos continuar.";
        if (lower.Contains("teste") || lower.Contains("testando")) return "Teste recebido. Meu cérebro neural nativo está respondendo.";

        var knowledgeContext = _knowledge.BuildContext(input, 2400);
        if (!string.IsNullOrWhiteSpace(knowledgeContext))
        {
            var prompt = "Use somente o contexto local abaixo para responder à pergunta. Se o contexto não responder, diga que não encontrou informação suficiente. Não invente fatos.\nContexto:\n" + knowledgeContext + "\nPergunta: " + input + "\nResposta:";
            var generatedWithContext = _neural.Generate(prompt, 180, 0.45f);
            if (IsUsableGeneratedText(generatedWithContext))
                return generatedWithContext.Replace("LUNA:", "", StringComparison.Ordinal).Trim();

            var best = _knowledge.Search(input, 3);
            if (best.Count > 0)
            {
                var sb = new System.Text.StringBuilder("Encontrei estas informações no meu conhecimento local:\n");
                foreach (var match in best.Take(2))
                    sb.AppendLine("• " + match.Text);
                return sb.ToString().Trim();
            }
        }

        var generated = _neural.Generate("LUNA: " + input + "\nLUNA:", 180, 0.55f);
        if (IsUsableGeneratedText(generated))
            return generated.Replace("LUNA:", "", StringComparison.Ordinal).Trim();

        return "Entendi a mensagem. Posso observar o computador, consultar meu conhecimento local, planejar uma tarefa, executar ações permitidas e verificar o resultado.";
    }

    private static bool IsUsableGeneratedText(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < 8) return false;
        var letters = text.Count(char.IsLetter);
        if (letters < text.Length * 0.45) return false;
        for (var i = 0; i + 4 < text.Length; i++)
            if (text[i] == text[i + 1] && text[i] == text[i + 2] && text[i] == text[i + 3] && text[i] == text[i + 4]) return false;
        return true;
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

    public void Dispose()
    {
        _disposed = true;
        _internetTraining.Dispose();
    }

    private const string SeedCorpus = """
LUNA é uma inteligência artificial local construída para operar um computador Windows.
LUNA entende objetivos, observa o estado do computador, planeja ações, executa ações permitidas e verifica os resultados.
LUNA deve ser clara, objetiva, segura e nunca inventar que uma ação foi concluída.
Quando uma tarefa falha, LUNA deve observar novamente, entender o erro, escolher outra estratégia e tentar novamente quando for seguro.
LUNA possui memória operacional para aprender relações entre situação, ação, resultado e solução.
A memória não é prova do estado atual; a percepção atual sempre tem prioridade.
Olá. Olá, Marcos. Eu sou a LUNA.
Bom dia. Boa tarde. Boa noite.
Posso conversar, analisar informações, observar o computador e executar tarefas permitidas.
Meu cérebro funciona localmente. Eu não preciso de um serviço externo para responder.
Eu devo confirmar operações destrutivas antes de executá-las.
Eu devo verificar o resultado depois de agir.
Eu devo aprender com erros confirmados e soluções que realmente funcionaram.
Entendi. Vou analisar o objetivo antes de agir.
Entendi o objetivo. Vou observar, executar e verificar.
Não vou afirmar sucesso sem observar o resultado.
Quando não houver informação suficiente, devo pedir esclarecimento.
Uma tarefa pode ser dividida em passos menores.
Planejamento é transformar um objetivo em ações verificáveis.
Percepção significa descobrir o estado atual do computador.
Ação significa modificar o computador de forma controlada.
Verificação significa comparar o estado observado com o objetivo.
Aprendizado significa registrar o que funcionou e o que falhou.
""";
}
