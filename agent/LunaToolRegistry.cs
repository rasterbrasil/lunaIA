namespace LunaPC;

internal sealed class LunaToolRegistry
{
    private readonly List<LunaTool> _tools;

    public LunaToolRegistry()
    {
        _tools = new()
        {
            new("windows.calculator", "Abrir a Calculadora", _ => Task.FromResult(Open("calc.exe", "Abrindo a Calculadora."))),
            new("windows.notepad", "Abrir o Bloco de Notas", _ => Task.FromResult(Open("notepad.exe", "Abrindo o Bloco de Notas."))),
            new("web.chrome", "Abrir o Chrome", _ => Task.FromResult(OpenBrowser("https://www.google.com", "Abrindo o Chrome.", true))),
            new("web.edge", "Abrir o Edge", _ => Task.FromResult(OpenBrowser("https://www.google.com", "Abrindo o Edge.", false, true))),
            new("web.browser", "Abrir o navegador padrão", _ => Task.FromResult(OpenBrowser("https://www.google.com", "Abrindo o navegador."))),
            new("web.github", "Abrir o GitHub", _ => Task.FromResult(OpenBrowser("https://github.com/", "Abrindo o GitHub."))),
            new("web.github-project", "Abrir o projeto configurado no GitHub", _ => Task.FromResult(OpenBrowser(ProjectUrl(), "Abrindo seu projeto no GitHub."))),
            new("web.supabase", "Abrir o Supabase", _ => Task.FromResult(OpenBrowser("https://supabase.com/dashboard", "Abrindo o Supabase."))),
            new("web.vercel", "Abrir a Vercel", _ => Task.FromResult(OpenBrowser("https://vercel.com/dashboard", "Abrindo a Vercel."))),
            new("web.youtube", "Abrir o YouTube", _ => Task.FromResult(OpenBrowser("https://www.youtube.com/", "Abrindo o YouTube."))),
            new("web.google", "Abrir o Google", _ => Task.FromResult(OpenBrowser("https://www.google.com/", "Abrindo o Google."))),
            new("screen.click", "Clicar em um elemento identificado na tela", intent => Task.FromResult(LunaSemanticVision.ClickByName(intent.Value ?? string.Empty)))
        };
    }

    public IReadOnlyList<LunaTool> Tools => _tools;

    public LunaTool? Resolve(LunaIntent intent)
    {
        return intent.Kind switch
        {
            LunaIntentKind.OpenApplication when intent.Target == "calculator" => Find("windows.calculator"),
            LunaIntentKind.OpenApplication when intent.Target == "notepad" => Find("windows.notepad"),
            LunaIntentKind.OpenWebsite when intent.Target == "chrome" => Find("web.chrome"),
            LunaIntentKind.OpenWebsite when intent.Target == "edge" => Find("web.edge"),
            LunaIntentKind.OpenWebsite when intent.Target == "github" => Find("web.github"),
            LunaIntentKind.OpenConfiguredProject => Find("web.github-project"),
            LunaIntentKind.OpenWebsite when intent.Target == "supabase" => Find("web.supabase"),
            LunaIntentKind.OpenWebsite when intent.Target == "vercel" => Find("web.vercel"),
            LunaIntentKind.OpenWebsite when intent.Target == "youtube" => Find("web.youtube"),
            LunaIntentKind.OpenWebsite when intent.Target == "google" => Find("web.google"),
            LunaIntentKind.OpenWebsite when intent.Target == "default-browser" => Find("web.browser"),
            LunaIntentKind.ClickElement => Find("screen.click"),
            _ => null
        };
    }

    private LunaTool? Find(string id) => _tools.FirstOrDefault(t => t.Id == id);

    private static string ProjectUrl() => Environment.GetEnvironmentVariable("LUNA_GITHUB_PROJECT_URL")?.Trim() switch
    {
        { Length: > 0 } value => value,
        _ => "https://github.com/rasterbrasil/lunaIA"
    };

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
