using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LunaPC;

internal sealed record LunaResult(string Text, bool Executed = false);

internal sealed class LunaCore : IDisposable
{
    private readonly LunaMemory _memory = new();
    private readonly LunaPlanner _planner = new();
    private bool _disposed;

    public async Task<LunaResult> ProcessAsync(string input)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LunaCore));

        var text = input.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return new("Estou ouvindo. Diga o que você quer que eu faça.");

        _memory.Remember(text);

        // O cérebro agora passa pedidos compostos pelo planejador real:
        // objetivo -> etapas -> execução -> resultado de cada etapa.
        var parts = Regex.Split(text, @"\s+(?:e depois|depois|em seguida)\s+", RegexOptions.IgnoreCase)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToArray();

        if (parts.Length > 1)
        {
            var steps = parts.Select((part, index) => new LunaStep(
                $"step-{index + 1}",
                $"Etapa {index + 1}: {part}",
                () => ProcessSingleAsync(part))).ToList();

            var plan = _planner.CreatePlan(text, steps);
            return await _planner.ExecuteAsync(plan);
        }

        return await ProcessSingleAsync(text);
    }

    private async Task<LunaResult> ProcessSingleAsync(string text)
    {
        await Task.Yield();
        var n = Normalize(text);

        if (Has(n, "quem e voce", "o que voce e"))
            return new("Eu sou a LUNA. Meu núcleo roda neste computador e não depende de uma API de nuvem para executar estas ações. Estamos construindo minha inteligência por camadas.");
        if (Has(n, "ola", "oi", "bom dia", "boa tarde", "boa noite"))
            return new("Olá. Estou aqui. Meu núcleo local e minha memória estão funcionando.");
        if (Has(n, "como voce esta"))
            return new("Estou funcionando normalmente. Já consigo interpretar pedidos, criar planos simples e executar ações locais no Windows.");
        if (Has(n, "que horas", "hora agora"))
            return new($"Agora são {DateTime.Now:HH:mm}.");
        if (Has(n, "que dia", "data de hoje", "hoje e"))
            return new($"Hoje é {DateTime.Now:dd/MM/yyyy}.");
        if (Has(n, "qual janela", "janela ativa", "onde estou"))
            return new($"A janela ativa é: {WindowsControl.ActiveWindowTitle()}.");
        if (Has(n, "memoria"))
            return new($"Minha memória local contém {_memory.Count} mensagens nesta instalação.");

        if (Has(n, "tire uma foto da tela", "tire uma foto da minha tela", "captura de tela", "capturar tela", "print da tela", "screenshot", "veja minha tela"))
            return ScreenVision.Capture();

        if (Has(n, "bloco de notas", "notepad"))
            return Open("notepad.exe", null, "Abrindo o Bloco de Notas.");
        if (Has(n, "calculadora", "calculator", "calc"))
            return Open("calc.exe", null, "Abrindo a Calculadora.");

        if (Has(n, "chrome", "google chrome"))
            return OpenBrowser("https://www.google.com", "Abrindo o Chrome.", preferChrome: true);
        if (Has(n, "edge", "microsoft edge"))
            return OpenBrowser("https://www.google.com", "Abrindo o Edge.", preferEdge: true);
        if (Has(n, "navegador", "browser", "internet", "aba do navegador", "aba no navegador"))
            return OpenBrowser("https://www.google.com", "Abrindo o navegador.");
        if (Has(n, "github")) return OpenBrowser("https://github.com/", "Abrindo o GitHub.");
        if (Has(n, "supabase")) return OpenBrowser("https://supabase.com/dashboard", "Abrindo o Supabase.");
        if (Has(n, "vercel")) return OpenBrowser("https://vercel.com/dashboard", "Abrindo a Vercel.");
        if (Has(n, "youtube")) return OpenBrowser("https://www.youtube.com/", "Abrindo o YouTube.");
        if (Has(n, "google")) return OpenBrowser("https://www.google.com/", "Abrindo o Google.");

        var search = Regex.Match(text, @"^\s*(?:luna[, ]*)?(?:pesquise|pesquisar|procure|procurar|busque|buscar)\s+(.+)$", RegexOptions.IgnoreCase);
        if (search.Success)
        {
            var query = search.Groups[1].Value.Trim();
            return OpenBrowser("https://www.google.com/search?q=" + Uri.EscapeDataString(query), $"Pesquisando por: {query}.");
        }

        var type = Regex.Match(text, @"^\s*(?:luna[, ]*)?(?:digite|escreva|escrever)\s+(.+)$", RegexOptions.IgnoreCase);
        if (type.Success)
            return WindowsControl.TypeText(type.Groups[1].Value.Trim());

        var key = Regex.Match(text, @"^\s*(?:luna[, ]*)?(?:pressione|aperte|tecla)\s+(.+)$", RegexOptions.IgnoreCase);
        if (key.Success)
            return WindowsControl.PressKey(key.Groups[1].Value.Trim());

        if (Has(n, "nova aba", "nova guia")) return WindowsControl.PressKey("ctrl+t");
        if (Has(n, "selecionar tudo")) return WindowsControl.PressKey("ctrl+a");
        if (Has(n, "copiar")) return WindowsControl.PressKey("ctrl+c");
        if (Has(n, "colar")) return WindowsControl.PressKey("ctrl+v");
        if (Has(n, "atualizar pagina", "recarregar pagina")) return WindowsControl.PressKey("f5");

        if (Has(n, "downloads", "pasta downloads"))
        {
            var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            return Open(downloads, null, "Abrindo a pasta Downloads.");
        }
        if (Has(n, "meus documentos", "documentos", "pasta documentos"))
            return Open(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), null, "Abrindo Documentos.");

        return new("Entendi sua mensagem e a guardei na memória. Posso criar planos simples para pedidos em sequência e executar cada etapa localmente. O próximo nível é verificar automaticamente o resultado de cada ação e usar visão para decidir a próxima etapa.");
    }

    private static LunaResult Open(string fileOrFolder, string? arguments, string success)
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo { FileName = fileOrFolder, Arguments = arguments ?? string.Empty, UseShellExecute = true });
            return process is null ? new($"Não consegui abrir {fileOrFolder} neste computador.") : new(success, true);
        }
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
        foreach (var file in candidates)
        {
            if (file.Contains(Path.DirectorySeparatorChar) && !File.Exists(file)) continue;
            if (TryStart(file, arguments)) return true;
        }
        return false;
    }

    private static bool TryStart(string file, string arguments)
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo { FileName = file, Arguments = arguments, UseShellExecute = true });
            return process is not null;
        }
        catch { return false; }
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
        Directory.CreateDirectory(dir);
        _file = Path.Combine(dir, "conversation.json");
        try { if (File.Exists(_file)) _messages.AddRange(JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_file))?.TakeLast(500) ?? []); } catch { }
    }

    public void Remember(string message)
    {
        if (_disposed || string.IsNullOrWhiteSpace(message)) return;
        _messages.Add(message);
        if (_messages.Count > 500) _messages.RemoveRange(0, _messages.Count - 500);
        try { File.WriteAllText(_file, JsonSerializer.Serialize(_messages, new JsonSerializerOptions { WriteIndented = true })); } catch { }
    }

    public void Dispose() => _disposed = true;
}
