using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LunaPC;

internal sealed record LunaResult(string Text, bool Executed = false);

internal sealed class LunaCore : IDisposable
{
    private readonly LunaMemory _memory = new();
    private readonly LunaPlanner _planner = new();
    private readonly LunaToolRegistry _tools = new();
    private readonly LunaDecisionEngine _decision;
    private readonly LunaAutonomyEngine _autonomy = new();
    private bool _disposed;

    public LunaCore() => _decision = new LunaDecisionEngine(_tools);

    public async Task<LunaResult> ProcessAsync(string input)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LunaCore));
        var text = input.Trim();
        if (string.IsNullOrWhiteSpace(text)) return new("Estou ouvindo. Diga o que você quer que eu faça.");
        _memory.Remember(text);

        var thought = _autonomy.Think(text);
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
            ? new("Entendi o objetivo, mas ainda não consegui transformá-lo em uma ação local confiável.")
            : await ExecuteAndVerifyAsync(single.RawText, single);
    }

    private static string DescribeIntent(LunaIntent intent)
        => intent.Kind == LunaIntentKind.ClickElement
            ? $"clicar em {intent.Value}"
            : intent.Kind == LunaIntentKind.OpenConfiguredProject
                ? "localizar e entrar no projeto pela visão"
                : intent.Kind.ToString();

    private async Task<LunaResult> ExecuteAndVerifyAsync(string text, LunaIntent intent)
    {
        var observation = LunaObserver.Observe();
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
            LunaIntentKind.AskIdentity => new($"{LunaIdentity.Describe()} Esta instalação é a minha base local. Ainda estou em construção, mas meu objetivo é agir com contexto, autonomia e honestidade sobre o que consigo confirmar."),
            LunaIntentKind.Greeting => new("Olá. Estou aqui. Meu núcleo local, minha memória e minha camada de raciocínio estão ativos."),
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
            _ => BuildUnknownResponse(intent)
        };
    }

    private static LunaResult BuildUnknownResponse(LunaIntent intent)
    {
        var knowledge = LunaKnowledge.Search(intent.RawText);
        if (knowledge.Count > 0)
        {
            var answer = string.Join(" ", knowledge.Take(2).Select(k => k.Content));
            return new($"Ainda não encontrei uma ferramenta local para executar esse objetivo. Posso, porém, usar meu conhecimento local relevante: {answer}");
        }
        return new("Entendi sua mensagem e a guardei na memória. A intenção foi reconhecida, mas ainda não existe uma ferramenta local confiável para essa tarefa.");
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
    public void Dispose() { if (_disposed) return; _disposed = true; _memory.Dispose(); }
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
