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
            LunaIntentKind.AskIdentity => new($"{LunaIdentity.Describe()} Esta instalação é a minha base local. Meu motor de linguagem agora também pode raciocinar e conversar localmente, sem Ollama e sem API de nuvem, quando o modelo estiver instalado."),
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
            _ => await AnswerWithLocalModelAsync(intent.RawText)
        };
    }

    private async Task<LunaResult> AnswerWithLocalModelAsync(string userText)
    {
        try
        {
            var knowledge = LunaKnowledge.Search(userText);
            var knowledgeText = knowledge.Count == 0
                ? "Nenhum item específico da base local foi encontrado."
                : string.Join("\n", knowledge.Take(4).Select(k => "- " + k.Content));

            var systemPrompt = """
Você é LUNA IA, uma assistente local para Windows. Você é a mesma identidade que está sendo construída neste computador: inteligente, direta, útil, curiosa, crítica e honesta.

Princípios:
- Responda em português do Brasil, salvo pedido contrário.
- Não invente fatos, ações ou acesso a serviços.
- Você é local: não diga que consultou a internet ou uma API se isso não aconteceu.
- Quando não souber, diga claramente o que falta.
- Pense passo a passo internamente, mas mostre ao usuário apenas a resposta útil e o raciocínio resumido quando ele for relevante.
- Você pode conversar naturalmente, explicar assuntos, comparar alternativas, planejar tarefas e ajudar a decidir.
- Quando uma tarefa exigir controle do Windows, a camada de ferramentas da LUNA fará a execução; não finja ter clicado, aberto ou verificado algo só porque foi solicitado.
- Preserve a identidade LUNA IA e o contexto de que este projeto está sendo construído para ganhar autonomia progressivamente.

Conhecimento local relevante:
""" + knowledgeText;

            // Local GGUF inference is CPU-heavy. Run the entire model call on a worker
            // thread so WinForms never blocks its UI message loop while the model loads
            // or generates tokens. The assistant window must remain responsive.
            var answer = await Task.Run(() => _language.ChatAsync(userText, systemPrompt));
            return new(answer, false);
        }
        catch (Exception ex)
        {
            return new($"Meu motor de linguagem local ainda não está disponível nesta instalação. Detalhe técnico: {ex.Message}");
        }
    }

    internal static LunaResult NavigateOrOpenBrowser(string url, string success)
    {
        try
        {
            var activeTitle = WindowsControl.ActiveWindowTitle();
            var activeIsBrowser = WindowsControl.IsBrowserWindowTitle(activeTitle);

            if (activeIsBrowser)
            {
                if (!WindowsControl.IsBlankBrowserWindowTitle(activeTitle))
                {
                    var newTab = WindowsControl.PressKey("ctrl+t");
                    if (!newTab.Executed) return newTab;
                    Thread.Sleep(500);
                }
            }
            else
            {
                if (WindowsControl.ActivateExistingBrowserWindow())
                {
                    Thread.Sleep(250);
                    var browserTitle = WindowsControl.ActiveWindowTitle();
                    if (!WindowsControl.IsBlankBrowserWindowTitle(browserTitle))
                    {
                        var newTab = WindowsControl.PressKey("ctrl+t");
                        if (!newTab.Executed) return newTab;
                        Thread.Sleep(500);
                    }
                }
                else
                {
                    var opened = WindowsControl.OpenNewBrowserWindow();
                    if (!opened.Executed) return opened;
                    Thread.Sleep(900);
                    if (!WindowsControl.ActivateExistingBrowserWindow())
                        return new($"{opened.Text} Abri o navegador, mas não consegui assumir a janela dele.", true);
                    var browserTitle = WindowsControl.ActiveWindowTitle();
                    if (!WindowsControl.IsBlankBrowserWindowTitle(browserTitle))
                    {
                        var newTab = WindowsControl.PressKey("ctrl+t");
                        if (!newTab.Executed) return newTab;
                        Thread.Sleep(500);
                    }
                }
            }

            var addressClick = LunaSemanticVision.ClickByNames(
                "Address and search bar", "Address bar", "Search or enter address",
                "Barra de endereços", "Barra de endereço", "Pesquisar ou inserir endereço",
                "Pesquisar ou digitar endereço");
            if (!addressClick.Executed)
            {
                var address = WindowsControl.PressKey("ctrl+l");
                if (!address.Executed) return address;
            }

            Thread.Sleep(120);
            var typed = WindowsControl.TypeText(url);
            if (!typed.Executed) return typed;
            var enter = WindowsControl.PressKey("enter");
            return enter.Executed
                ? new($"{success} Mantive a janela do navegador que você já estava usando e abri o destino em uma nova aba.", true)
                : enter;
        }
        catch (Exception ex) { return new($"Não consegui navegar no navegador: {ex.Message}"); }
    }

    private static bool IsRetryableLaunch(string command)
    {
        var n = Normalize(command);
        return Has(n, "calculadora", "calculator", "calc", "bloco de notas", "notepad", "chrome", "google chrome", "edge", "microsoft edge", "navegador", "browser", "github", "supabase", "vercel", "youtube", "google", "meu projeto", "meu repositorio");
    }

    private static LunaResult Open(string fileOrFolder, string? arguments, string success)
    {
        try { var process = Process.Start(new ProcessStartInfo { FileName = fileOrFolder, Arguments = arguments ?? string.Empty, UseShellExecute = true }); return process is null ? new($"Não consegui abrir {fileOrFolder} neste computador.") : new(success, true); }
        catch { return new($"Não consegui abrir {fileOrFolder} neste computador."); }
    }

    private static string Normalize(string value)
    {
        var form = value.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var chars = form.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark);
        return new string(chars.ToArray()).Normalize(System.Text.NormalizationForm.FormC);
    }
    private static bool Has(string text, params string[] terms) => terms.Any(text.Contains);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _language.Dispose();
        _memory.Dispose();
    }
}

internal sealed class LunaMemory : IDisposable
{
    private readonly string _file;
    private readonly List<string> _messages = [];
    private bool _disposed;
    public int Count => _messages.Count;
    public LunaMemory()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LunaPC", "memory");
        Directory.CreateDirectory(dir); _file = Path.Combine(dir, "conversation.json");
        try { if (File.Exists(_file)) _messages.AddRange(JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_file))?.TakeLast(500) ?? []); } catch { }
    }
    public void Remember(string message)
    {
        if (_disposed || string.IsNullOrWhiteSpace(message)) return; _messages.Add(message); if (_messages.Count > 500) _messages.RemoveRange(0, _messages.Count - 500);
        try { File.WriteAllText(_file, JsonSerializer.Serialize(_messages, new JsonSerializerOptions { WriteIndented = true })); } catch { }
    }
    public void Dispose() => _disposed = true;
}
