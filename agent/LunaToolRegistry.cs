namespace LunaPC;

internal enum LunaRisk
{
    Safe,
    Confirm
}

internal sealed record LunaTool(
    string Id,
    string Description,
    LunaRisk Risk,
    Func<Task<LunaResult>> Execute,
    Func<bool> Matches);

internal sealed class LunaToolRegistry
{
    private readonly List<LunaTool> _tools;

    public LunaToolRegistry()
    {
        _tools = new()
        {
            new("windows.calculator", "Abrir a Calculadora", LunaRisk.Safe,
                () => Task.FromResult(Open("calc.exe", "Abrindo a Calculadora.")),
                () => false),
            new("windows.notepad", "Abrir o Bloco de Notas", LunaRisk.Safe,
                () => Task.FromResult(Open("notepad.exe", "Abrindo o Bloco de Notas.")),
                () => false),
            new("web.chrome", "Abrir o Chrome", LunaRisk.Safe,
                () => Task.FromResult(OpenBrowser("https://www.google.com", "Abrindo o Chrome.", true, false)),
                () => false),
            new("web.edge", "Abrir o Edge", LunaRisk.Safe,
                () => Task.FromResult(OpenBrowser("https://www.google.com", "Abrindo o Edge.", false, true)),
                () => false),
            new("web.browser", "Abrir o navegador padrão", LunaRisk.Safe,
                () => Task.FromResult(OpenBrowser("https://www.google.com", "Abrindo o navegador.")),
                () => false),
            new("web.github", "Abrir o GitHub", LunaRisk.Safe,
                () => Task.FromResult(OpenBrowser("https://github.com/", "Abrindo o GitHub.")),
                () => false),
            new("web.supabase", "Abrir o Supabase", LunaRisk.Safe,
                () => Task.FromResult(OpenBrowser("https://supabase.com/dashboard", "Abrindo o Supabase.")),
                () => false),
            new("web.vercel", "Abrir a Vercel", LunaRisk.Safe,
                () => Task.FromResult(OpenBrowser("https://vercel.com/dashboard", "Abrindo a Vercel.")),
                () => false),
            new("web.youtube", "Abrir o YouTube", LunaRisk.Safe,
                () => Task.FromResult(OpenBrowser("https://www.youtube.com/", "Abrindo o YouTube.")),
                () => false),
            new("web.google", "Abrir o Google", LunaRisk.Safe,
                () => Task.FromResult(OpenBrowser("https://www.google.com/", "Abrindo o Google.")),
                () => false)
        };
    }

    public IReadOnlyList<LunaTool> Tools => _tools;

    public LunaTool? Resolve(string normalizedInput)
    {
        if (Has(normalizedInput, "calculadora", "calculator", "calc")) return _tools.First(t => t.Id == "windows.calculator");
        if (Has(normalizedInput, "bloco de notas", "notepad")) return _tools.First(t => t.Id == "windows.notepad");
        if (Has(normalizedInput, "chrome", "google chrome")) return _tools.First(t => t.Id == "web.chrome");
        if (Has(normalizedInput, "edge", "microsoft edge")) return _tools.First(t => t.Id == "web.edge");
        if (Has(normalizedInput, "github")) return _tools.First(t => t.Id == "web.github");
        if (Has(normalizedInput, "supabase")) return _tools.First(t => t.Id == "web.supabase");
        if (Has(normalizedInput, "vercel")) return _tools.First(t => t.Id == "web.vercel");
        if (Has(normalizedInput, "youtube")) return _tools.First(t => t.Id == "web.youtube");
        if (Has(normalizedInput, "google")) return _tools.First(t => t.Id == "web.google");
        if (Has(normalizedInput, "navegador", "browser", "internet", "aba do navegador", "aba no navegador")) return _tools.First(t => t.Id == "web.browser");
        return null;
    }

    private static bool Has(string text, params string[] terms) => terms.Any(text.Contains);

    private static LunaResult Open(string fileOrFolder, string success)
    {
        try
        {
            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = fileOrFolder,
                UseShellExecute = true
            });
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
        var candidates = new[]
        {
            "chrome.exe",
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe")
        };
        return TryStartFirstExisting(candidates, $"--new-tab \"{url}\"");
    }

    private static bool TryStartEdge(string url)
    {
        var candidates = new[]
        {
            "msedge.exe",
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe")
        };
        return TryStartFirstExisting(candidates, $"--new-tab \"{url}\"");
    }

    private static bool TryStartFirstExisting(IEnumerable<string> candidates, string arguments)
    {
        foreach (var file in candidates)
        {
            if (file.Contains(System.IO.Path.DirectorySeparatorChar) && !System.IO.File.Exists(file)) continue;
            if (TryStart(file, arguments)) return true;
        }
        return false;
    }

    private static bool TryStart(string file, string arguments)
    {
        try
        {
            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = file,
                Arguments = arguments,
                UseShellExecute = true
            });
            return process is not null;
        }
        catch { return false; }
    }
}
