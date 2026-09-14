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
    private bool _disposed;

    public LunaCore() => _decision = new LunaDecisionEngine(_tools);

    public async Task<LunaResult> ProcessAsync(string input)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LunaCore));
        var text = input.Trim();
        if (string.IsNullOrWhiteSpace(text)) return new("Estou ouvindo. Diga o que você quer que eu faça.");
        _memory.Remember(text);

        var parts = Regex.Split(text, @"\s+(?:e depois|depois|em seguida)\s+", RegexOptions.IgnoreCase)
            .Select(p => p.Trim()).Where(p => p.Length > 0).ToArray();
        if (parts.Length > 1)
        {
            var steps = parts.Select((part, index) => new LunaStep($"step-{index + 1}", $"Etapa {index + 1}: {part}", () => ExecuteAndVerifyAsync(part))).ToList();
            return await _planner.ExecuteAsync(_planner.CreatePlan(text, steps));
        }
        return await ExecuteAndVerifyAsync(text);
    }

    private async Task<LunaResult> ExecuteAndVerifyAsync(string text)
    {
        var observation = LunaObserver.Observe();
        var intent = LunaIntentParser.Parse(text);
        var decision = _decision.Decide(intent);

        LunaResult result;
        if (decision.Tool is not null)
        {
            if (decision.RequiresConfirmation)
                return new($"Preciso da sua confirmação antes de executar: {decision.Tool.Description}.");
            result = await decision.Tool.Execute(intent);
        }
        else
        {
            result = await ProcessNonToolIntentAsync(intent);
        }

        if (!result.Executed) return result;
        var verified = await LunaVerifier.VerifyAsync(text, result);
        if (verified.Executed) return verified;
        if (!IsRetryableLaunch(text)) return verified;

        var after = LunaObserver.Observe();
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

    private async Task<LunaResult> ExecuteToolWithoutRetryAsync(LunaIntent intent, LunaDecision decision)
    {
        if (decision.Tool is not null)
        {
            if (decision.RequiresConfirmation) return new("Ação aguardando confirmação.");
            return await decision.Tool.Execute(intent);
        }
        return await ProcessNonToolIntentAsync(intent);
    }

    private async Task<LunaResult> ProcessNonToolIntentAsync(LunaIntent intent)
    {
        await Task.Yield();
        return intent.Kind switch
        {
            LunaIntentKind.AskIdentity => new("Eu sou a LUNA. Meu núcleo roda neste computador e estamos construindo minha inteligência por camadas, sem depender de uma API de nuvem para executar estas ações."),
            LunaIntentKind.Greeting => new("Olá. Estou aqui. Meu núcleo local e minha memória estão funcionando."),
            LunaIntentKind.AskTime => new($"Agora são {DateTime.Now:HH:mm}."),
            LunaIntentKind.AskDate => new($"Hoje é {DateTime.Now:dd/MM/yyyy}."),
            LunaIntentKind.AskMemory => new($"Minha memória local contém {_memory.Count} mensagens nesta instalação."),
            LunaIntentKind.ObserveScreen => LunaObserver.Describe(),
            LunaIntentKind.CaptureScreen => ScreenVision.Capture(),
            LunaIntentKind.SearchWeb => OpenBrowser("https://www.google.com/search?q=" + Uri.EscapeDataString(intent.Value ?? string.Empty), $"Pesquisando por: {intent.Value}."),
            LunaIntentKind.TypeText => WindowsControl.TypeText(intent.Value ?? string.Empty),
            LunaIntentKind.PressKey => WindowsControl.PressKey(intent.Value ?? string.Empty),
            LunaIntentKind.OpenFolder when intent.Target == "Downloads" => Open(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"), null, "Abrindo a pasta Downloads."),
            LunaIntentKind.OpenFolder when intent.Target == "Documents" => Open(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), null, "Abrindo Documentos."),
            _ => new("Entendi sua mensagem e a guardei na memória. A intenção foi reconhecida, mas ainda não existe uma ferramenta local para essa tarefa.")
        };
    }

    private static bool IsRetryableLaunch(string command)
    {
        var n = Normalize(command);
        return Has(n, "calculadora", "calculator", "calc", "bloco de notas", "notepad", "chrome", "google chrome", "edge", "microsoft edge", "navegador", "browser", "github", "supabase", "vercel", "youtube", "google");
    }

    private static LunaResult Open(string fileOrFolder, string? arguments, string success)
    {
        try { var process = Process.Start(new ProcessStartInfo { FileName = fileOrFolder, Arguments = arguments ?? string.Empty, UseShellExecute = true }); return process is null ? new($"Não consegui abrir {fileOrFolder} neste computador.") : new(success, true); }
        catch { return new($"Não consegui abrir {fileOrFolder} neste computador."); }
    }

    private static LunaResult OpenBrowser(string url, string success, bool preferChrome = false, bool preferEdge = false)
    {
        try
        {
            if (preferChrome && TryStartChrome(url)) return new(success, true);
            if (preferEdge && TryStartEdge(url)) return new(success, true);
            if (TryStart("explorer.exe", url)) return new(success, true);
            if (TryStart("rundll32.exe", $"url.dll,FileProtocolHandler \"{url}\"")) return new(success, true);
            return new("Não consegui abrir o navegador padrão deste computador.");
        }
        catch (Exception ex) { return new($"Não consegui abrir o navegador: {ex.Message}"); }
    }

    private static bool TryStartChrome(string url)
    {
        var candidates = new[] { "chrome.exe", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe") };
        return TryStartFirstExisting(candidates, $"--new-tab \"{url}\"");
    }

    private static bool TryStartEdge(string url)
    {
        var candidates = new[] { "msedge.exe", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe") };
        return TryStartFirstExisting(candidates, $"--new-tab \"{url}\"");
    }

    private static bool TryStartFirstExisting(IEnumerable<string> candidates, string arguments)
    {
        foreach (var file in candidates) { if (file.Contains(Path.DirectorySeparatorChar) && !File.Exists(file)) continue; if (TryStart(file, arguments)) return true; }
        return false;
    }

    private static bool TryStart(string file, string arguments)
    {
        try { var process = Process.Start(new ProcessStartInfo { FileName = file, Arguments = arguments, UseShellExecute = true }); return process is not null; }
        catch { return false; }
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
