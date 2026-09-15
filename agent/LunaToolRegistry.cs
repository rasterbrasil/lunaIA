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
            new("windows.type", "Digitar texto na janela ativa", intent => Task.FromResult(WindowsControl.TypeText(intent.Value ?? string.Empty))),
            new("windows.key", "Pressionar uma tecla ou combinação na janela ativa", intent => Task.FromResult(WindowsControl.PressKey(intent.Value ?? string.Empty))),
            new("web.chrome", "Abrir ou navegar no Chrome", _ => Task.FromResult(OpenBrowser("https://www.google.com", "Abrindo o Chrome.", true))),
            new("web.edge", "Abrir ou navegar no Edge", _ => Task.FromResult(OpenBrowser("https://www.google.com", "Abrindo o Edge.", false, true))),
            new("web.browser", "Abrir o navegador padrão", _ => Task.FromResult(OpenBrowser("https://www.google.com", "Abrindo o navegador."))),
            new("web.github", "Abrir o GitHub", _ => Task.FromResult(OpenBrowser("https://github.com/", "Abrindo o GitHub."))),
            new("web.github-project", "Encontrar e abrir o projeto configurado no GitHub pela visão", _ => OpenConfiguredProjectAsync()),
            new("web.supabase", "Abrir o Supabase", _ => Task.FromResult(OpenBrowser("https://supabase.com/dashboard", "Abrindo o Supabase."))),
            new("web.vercel", "Abrir a Vercel", _ => Task.FromResult(OpenBrowser("https://vercel.com/dashboard", "Abrindo a Vercel."))),
            new("web.youtube", "Abrir o YouTube", _ => Task.FromResult(OpenBrowser("https://www.youtube.com/", "Abrindo o YouTube."))),
            new("web.google", "Abrir o Google", _ => Task.FromResult(OpenBrowser("https://www.google.com/", "Abrindo o Google."))),
            new("screen.observe", "Observar a tela usando visão semântica local", _ => Task.FromResult(LunaSemanticVision.Describe())),
            new("screen.capture", "Capturar a tela", _ => Task.FromResult(ScreenVision.Capture())),
            new("screen.click", "Encontrar e clicar em um elemento identificado na tela", intent => Task.FromResult(LunaSemanticVision.ClickByName(intent.Value ?? string.Empty))),
            new("windows.downloads", "Abrir a pasta Downloads", _ => Task.FromResult(Open(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"), "Abrindo a pasta Downloads."))),
            new("windows.documents", "Abrir a pasta Documentos", _ => Task.FromResult(Open(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Abrindo Documentos.")))
        };
    }

    public IReadOnlyList<LunaTool> Tools => _tools;

    public LunaTool? FindById(string id) => _tools.FirstOrDefault(t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public LunaTool? Resolve(LunaIntent intent) => intent.Kind switch
    {
        LunaIntentKind.OpenApplication when intent.Target == "calculator" => FindById("windows.calculator"),
        LunaIntentKind.OpenApplication when intent.Target == "notepad" => FindById("windows.notepad"),
        LunaIntentKind.OpenWebsite when intent.Target == "chrome" => FindById("web.chrome"),
        LunaIntentKind.OpenWebsite when intent.Target == "edge" => FindById("web.edge"),
        LunaIntentKind.OpenWebsite when intent.Target == "github" => FindById("web.github"),
        LunaIntentKind.OpenConfiguredProject => FindById("web.github-project"),
        LunaIntentKind.OpenWebsite when intent.Target == "supabase" => FindById("web.supabase"),
        LunaIntentKind.OpenWebsite when intent.Target == "vercel" => FindById("web.vercel"),
        LunaIntentKind.OpenWebsite when intent.Target == "youtube" => FindById("web.youtube"),
        LunaIntentKind.OpenWebsite when intent.Target == "google" => FindById("web.google"),
        LunaIntentKind.OpenWebsite when intent.Target == "default-browser" => FindById("web.browser"),
        LunaIntentKind.ClickElement => FindById("screen.click"),
        LunaIntentKind.TypeText => FindById("windows.type"),
        LunaIntentKind.PressKey => FindById("windows.key"),
        LunaIntentKind.ObserveScreen => FindById("screen.observe"),
        LunaIntentKind.CaptureScreen => FindById("screen.capture"),
        LunaIntentKind.OpenFolder when intent.Target == "Downloads" => FindById("windows.downloads"),
        LunaIntentKind.OpenFolder when intent.Target == "Documents" => FindById("windows.documents"),
        _ => null
    };

    private static async Task<LunaResult> OpenConfiguredProjectAsync()
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await Task.Delay(attempt == 1 ? 1600 : 900);
            var semanticClick = LunaSemanticVision.ClickByNameContains("lunaIA", "rasterbrasil/lunaIA");
            if (semanticClick.Executed)
                return new($"Encontrei o projeto pela visão semântica local na tentativa {attempt}, movi o cursor até o projeto e cliquei.", true);
        }
        return new("Não consegui encontrar visualmente o projeto na página atual do GitHub. Não vou digitar o nome na barra de navegação nem abrir um endereço direto.");
    }

    private static LunaResult Open(string fileOrFolder, string success)
    {
        try
        {
            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = fileOrFolder, UseShellExecute = true });
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
        var candidates = new[] { "chrome.exe", System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"), System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"), System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe") };
        return TryStartFirstExisting(candidates, $"--new-tab \"{url}\"");
    }

    private static bool TryStartEdge(string url)
    {
        var candidates = new[] { "msedge.exe", System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"), System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe") };
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
            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = file, Arguments = arguments, UseShellExecute = true });
            return process is not null;
        }
        catch { return false; }
    }
}
