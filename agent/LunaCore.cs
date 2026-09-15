using System.Diagnostics;
using System.Text.Json;

namespace LunaPC;

internal sealed record LunaResult(string Text, bool Executed = false);

internal sealed class LunaCore : IDisposable
{
    private readonly LunaMemory _memory = new();
    private readonly LunaPlanner _planner = new();
    private readonly LunaToolRegistry _tools = new();
    private readonly LunaDecisionEngine _decision;
    private readonly LunaAutonomyEngine _autonomy = new();
    private readonly LunaLocalBrain _brain;
    private readonly ILunaReasoningModel _reasoner = new LunaLocalReasoningModel();
    private readonly LunaLocalLanguageEngine _language = new();
    private bool _disposed;

    public LunaCore()
    {
        _decision = new LunaDecisionEngine(_tools);
        _brain = new LunaLocalBrain(_tools);
    }

    public async Task<LunaResult> ProcessAsync(string input)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LunaCore));
        var text = input.Trim();
        if (string.IsNullOrWhiteSpace(text)) return new("Estou ouvindo. Diga o que você quer que eu faça.");
        _memory.Remember(text);

        // Conversational intents are deterministic. They must never enter the
        // reasoning engine: this keeps greetings, identity and capability questions
        // instant and prevents a normal conversation from being mistaken for an action.
        var directIntent = LunaIntentParser.Parse(text);
        if (IsDirectConversation(directIntent))
            return await ProcessNonToolIntentAsync(directIntent);

        var thought = _autonomy.Think(text);
        var localThought = _brain.Think(text);
        var model = await _reasoner.ReasonAsync(new LunaModelRequest(text, BuildContext(localThought), thought.Intents));

        if (thought.Intents.Count == 0 || (thought.Intents.Count == 1 && thought.Intents[0].Kind == LunaIntentKind.Unknown && model.NeedsClarification))
            return await AnswerWithLocalModelAsync(text);

        if (thought.Intents.Count > 1)
        {
            var steps = thought.Intents.Select((intent, index) =>
                new LunaStep(
                    $"step-{index + 1}",
                    $"Etapa {index + 1}: {DescribeIntent(intent)}",
                    () => ExecuteAndVerifyAsync(intent.RawText, intent)))
                .ToList();
            return await _planner.ExecuteAsync(_planner.CreatePlan(text, steps));
        }

        var single = thought.Intents.FirstOrDefault();
        return single is null
            ? await AnswerWithLocalModelAsync(text)
            : await ExecuteAndVerifyAsync(single.RawText, single);
    }

    private static bool IsDirectConversation(LunaIntent intent)
        => intent.Kind is LunaIntentKind.Greeting
            or LunaIntentKind.AskIdentity
            or LunaIntentKind.AskCapabilities
            or LunaIntentKind.AskTime
            or LunaIntentKind.AskDate
            or LunaIntentKind.AskMemory
            or LunaIntentKind.AskActiveWindow;

    private static LunaCognitiveContext BuildContext(LunaThought thought)
        => new(thought.Goal, thought.Observation, [], [], thought.Knowledge, thought.Confidence);

    private static string DescribeIntent(LunaIntent intent)
        => intent.Kind == LunaIntentKind.ClickElement
            ? $"clicar em {intent.Value}"
            : intent.Kind == LunaIntentKind.OpenConfiguredProject
                ? "localizar e entrar no projeto pela visão"
                : intent.Kind.ToString();

    private async Task<LunaResult> ExecuteAndVerifyAsync(string text, LunaIntent intent)
    {
        var observation = LunaObserver.Observe();
        var thought = _brain.Think(text);
        var brainDecision = _brain.Decide(intent, thought);
        if (brainDecision.NeedsConfirmation)
            return new($"Preciso da sua confirmação antes de executar esta ação: {brainDecision.Rationale}");
        if (brainDecision.Confidence < 0.30 && intent.Kind != LunaIntentKind.ObserveScreen)
            return new($"Minha confiança para essa ação está baixa ({brainDecision.Confidence:P0}). Vou observar mais antes de agir.");

        var decision = _decision.Decide(intent);
        LunaResult result;
        if (decision.Tool is not null)
        {
            if (decision.RequiresConfirmation) return new($"Preciso da sua confirmação antes de executar: {decision.Tool.Description}.");
            result = await ExecuteToolAsync(intent, decision);
        }
        else result = await ProcessNonToolIntentAsync(intent);

        if (!result.Executed) return result;
        var after = LunaObserver.Observe();
        var verified = await LunaVerifier.VerifyAsync(text, result);
        if (verified.Executed) return verified;
        if (!IsRetryableLaunch(text)) return verified;

        var stateChanged = !string.Equals(observation.ActiveWindow, after.ActiveWindow, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(observation.ScreenFingerprint, after.ScreenFingerprint, StringComparison.OrdinalIgnoreCase);
        if (stateChanged) return verified;

        await Task.Delay(700);
        var retry = await ExecuteToolWithoutRetryAsync(intent, decision);
        if (!retry.Executed) return retry;
        var retryVerified = await LunaVerifier.VerifyAsync(text, retry);
        return retryVerified.Executed
            ? new($"{retryVerified.Text} A primeira tentativa não foi confirmada; fiz uma segunda tentativa controlada.", true)
            : new($"{retryVerified.Text} A segunda tentativa também não foi confirmada.");
    }

    private async Task<LunaResult> ExecuteToolAsync(LunaIntent intent, LunaDecision decision)
    {
        if (intent.Kind == LunaIntentKind.OpenWebsite)
        {
            var url = intent.Target switch
            {
                "github" => "https://github.com/",
                "supabase" => "https://supabase.com/dashboard",
                "vercel" => "https://vercel.com/dashboard",
                "youtube" => "https://www.youtube.com/",
                "google" => "https://www.google.com/",
                "default-browser" => "https://www.google.com/",
                _ => null
            };
            if (url is not null) return NavigateOrOpenBrowser(url, $"Abrindo {intent.Target}.");
        }

        if (intent.Kind == LunaIntentKind.OpenConfiguredProject)
            return await OpenConfiguredProjectByVisionAsync();

        return await decision.Tool!.Execute(intent);
    }

    private async Task<LunaResult> OpenConfiguredProjectByVisionAsync()
    {
        var terms = new[] { "lunaIA", "rasterbrasil/lunaIA", "luna IA" };
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            await Task.Delay(attempt == 1 ? 1400 : 900);
            var observation = LunaObserver.Observe();
            var click = LunaSemanticVision.ClickByNameContainsInBrowser(terms);
            if (click.Executed)
            {
                await Task.Delay(1200);
                var after = LunaObserver.Observe();
                return new($"Encontrei o projeto pela visão semântica local na tentativa {attempt}, movi o cursor até o projeto e cliquei. Janela observada após o clique: {after.ActiveWindow}.", true);
            }

            if (!WindowsControl.IsBrowserWindowTitle(observation.ActiveWindow))
            {
                WindowsControl.ActivateExistingBrowserWindow();
                await Task.Delay(250);
            }
        }

        return new("Não consegui encontrar visualmente o projeto na página do GitHub após quatro tentativas. Não vou digitar o nome na barra de navegação nem abrir o endereço diretamente.");
    }

    private async Task<LunaResult> ExecuteToolWithoutRetryAsync(LunaIntent intent, LunaDecision decision)
    {
        if (decision.Tool is not null)
        {
            if (decision.RequiresConfirmation) return new("Ação aguardando confirmação.");
            return await ExecuteToolAsync(intent, decision);
        }
        return await ProcessNonToolIntentAsync(intent);
    }

    private async Task<LunaResult> ProcessNonToolIntentAsync(LunaIntent intent)
    {
        await Task.Yield();
        return intent.Kind switch
        {
            LunaIntentKind.AskIdentity => new($"{LunaIdentity.Describe()} Esta instalação é a minha base local. Meu motor de linguagem também pode raciocinar e conversar localmente, sem Ollama e sem API de nuvem, quando o modelo estiver instalado."),
            LunaIntentKind.AskCapabilities => new("Eu consigo conversar com você, lembrar o contexto local, observar a tela, abrir aplicativos e pastas, controlar teclado e mouse, navegar na web, entrar no seu projeto do GitHub pela visão, verificar resultados e repetir ações seguras quando necessário. Quando uma tarefa exigir raciocínio, uso meu motor local para montar um plano e escolher as ferramentas."),
            LunaIntentKind.Greeting => new("Olá. Estou aqui. Meu núcleo local, minha memória, minha camada de raciocínio e meu motor de linguagem estão ativos."),
            LunaIntentKind.AskTime => new($"Agora são {DateTime.Now:HH:mm}."),
            LunaIntentKind.AskDate => new($"Hoje é {DateTime.Now:dd/MM/yyyy}."),
            LunaIntentKind.AskMemory => new($"Minha memória local contém {_memory.Count} mensagens nesta instalação."),
            LunaIntentKind.AskActiveWindow => new($"A janela ativa agora é '{WindowsControl.ActiveWindowTitle()}'."),
            LunaIntentKind.ObserveScreen => LunaSemanticVision.Describe(),
            LunaIntentKind.CaptureScreen => ScreenVision.Capture(),
            LunaIntentKind.SearchWeb => NavigateOrOpenBrowser("https://www.google.com/search?q=" + Uri.EscapeDataString(intent.Value ?? string.Empty), $"Pesquisando por: {intent.Value}."),
            LunaIntentKind.TypeText => WindowsControl.TypeText(intent.Value ?? string.Empty),
            LunaIntentKind.PressKey => WindowsControl.PressKey(intent.Value ?? string.Empty),
            LunaIntentKind.OpenFolder when intent.Target == "Downloads" => Open(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"), null, "Abrindo a pasta Downloads."),
            LunaIntentKind.OpenFolder when intent.Target == "Documents" => Open(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), null, "Abrindo Documentos."),
            _ => new("Ainda não tenho uma ferramenta local adequada para essa solicitação.")
        };
    }

    private static bool IsRetryableLaunch(string text)
    {
        var n = text.ToLowerInvariant();
        return n.Contains("calculadora") || n.Contains("calculator") || n.Contains("notepad") || n.Contains("bloco de notas") || n.Contains("chrome") || n.Contains("edge") || n.Contains("github") || n.Contains("supabase") || n.Contains("vercel") || n.Contains("youtube") || n.Contains("google") || n.Contains("navegador");
    }

    private static LunaResult Open(string path, string? fileName, string message)
    {
        try
        {
            var target = fileName is null ? path : Path.Combine(path, fileName);
            Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
            return new(message, true);
        }
        catch (Exception ex) { return new($"Não consegui abrir o caminho solicitado: {ex.Message}"); }
    }

    private static LunaResult NavigateOrOpenBrowser(string url, string message)
    {
        try
        {
            if (WindowsControl.IsBrowserWindowTitle(WindowsControl.ActiveWindowTitle()))
            {
                WindowsControl.PressKey("ctrl+t");
                Thread.Sleep(220);
                WindowsControl.PressKey("ctrl+l");
                WindowsControl.TypeText(url);
                WindowsControl.PressKey("enter");
                return new(message + " Abri em uma nova aba do navegador ativo.", true);
            }

            if (WindowsControl.ActivateExistingBrowserWindow())
            {
                Thread.Sleep(220);
                WindowsControl.PressKey("ctrl+t");
                Thread.Sleep(220);
                WindowsControl.PressKey("ctrl+l");
                WindowsControl.TypeText(url);
                WindowsControl.PressKey("enter");
                return new(message + " Reutilizei o navegador existente e abri uma nova aba.", true);
            }

            WindowsControl.OpenNewBrowserWindow();
            Thread.Sleep(400);
            WindowsControl.PressKey("ctrl+l");
            WindowsControl.TypeText(url);
            WindowsControl.PressKey("enter");
            return new(message + " Abri uma nova janela do navegador porque não havia uma disponível.", true);
        }
        catch (Exception ex) { return new($"Não consegui abrir o navegador: {ex.Message}"); }
    }

    private async Task<LunaResult> AnswerWithLocalModelAsync(string text)
    {
        try
        {
            var prompt = $"Você é a LUNA IA, uma assistente local para Windows. Responda em português do Brasil de forma curta, natural e direta. Não invente ações executadas. Pergunta do usuário: {text}";
            var answer = await _language.ChatAsync(text, prompt, maxTokens: 256, contextSize: 4096, disableThinking: true);
            return new(answer);
        }
        catch (Exception ex) { return new($"Meu motor local não conseguiu responder agora: {ex.Message}"); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _language.Dispose();
    }
}
