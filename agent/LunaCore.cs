using System.Diagnostics;
using System.Text.Json;

namespace LunaPC;

internal sealed record LunaResult(string Text, bool Executed = false);

internal sealed class LunaCore : IDisposable
{
    private readonly LunaMemory _memory = new();
    private bool _disposed;

    public async Task<LunaResult> ProcessAsync(string input)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LunaCore));
        await Task.Yield();

        var text = input.Trim();
        var n = Normalize(text);
        _memory.Remember(text);

        if (Has(n, "quem é você", "quem e voce", "o que você é", "o que voce e"))
            return new("Eu sou a LUNA. Meu núcleo roda neste computador e não depende de uma API de nuvem para executar estas ações. Estamos construindo minha inteligência por camadas.");

        if (Has(n, "olá", "ola", "oi", "bom dia", "boa tarde", "boa noite"))
            return new("Olá. Estou aqui. Meu núcleo local e minha memória estão funcionando.");

        if (Has(n, "como você está", "como voce esta"))
            return new("Estou funcionando normalmente. Já consigo interpretar alguns pedidos e executar ações locais no Windows.");

        if (Has(n, "que horas", "hora agora"))
            return new($"Agora são {DateTime.Now:HH:mm}.");

        if (Has(n, "que dia", "data de hoje", "hoje é", "hoje e"))
            return new($"Hoje é {DateTime.Now:dd/MM/yyyy}.");

        if (Has(n, "memória", "memoria"))
            return new($"Minha memória local contém {_memory.Count} mensagens nesta instalação.");

        // Aplicativos básicos do Windows.
        if (Has(n, "bloco de notas", "notepad"))
            return Open("notepad.exe", null, "Abrindo o Bloco de Notas.");

        if (Has(n, "calculadora", "calculator", "calc"))
            return Open("calc.exe", null, "Abrindo a Calculadora.");

        // Navegador: não exigimos uma frase exata.
        if (Has(n, "chrome", "google chrome"))
            return OpenBrowser("https://www.google.com", "Abrindo o Chrome/navegador.", preferChrome: true);

        if (Has(n, "edge", "microsoft edge"))
            return OpenBrowser("https://www.google.com", "Abrindo o Edge/navegador.", preferEdge: true);

        if (Has(n, "navegador", "browser", "internet", "aba do navegador", "aba no navegador"))
            return OpenBrowser("https://www.google.com", "Abrindo uma aba do navegador.");

        // Sites que a LUNA já conhece.
        if (Has(n, "meu github", "abre meu github", "abrir meu github", "github"))
            return OpenBrowser("https://github.com/", "Abrindo o GitHub.");

        if (Has(n, "supabase"))
            return OpenBrowser("https://supabase.com/dashboard", "Abrindo o Supabase.");

        if (Has(n, "vercel"))
            return OpenBrowser("https://vercel.com/dashboard", "Abrindo a Vercel.");

        if (Has(n, "youtube"))
            return OpenBrowser("https://www.youtube.com/", "Abrindo o YouTube.");

        if (Has(n, "google"))
            return OpenBrowser("https://www.google.com/", "Abrindo o Google.");

        // Pastas comuns.
        if (Has(n, "downloads", "pasta downloads"))
            return Open(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Abrindo a pasta Downloads.");

        if (Has(n, "meus documentos", "documentos", "pasta documentos"))
            return Open(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), null, "Abrindo Documentos.");

        return new("Entendi sua mensagem e a guardei na memória. Ainda não reconheço esse pedido como uma ação. Meu próximo nível será ampliar meu interpretador, visão e controle do Windows.");
    }

    private static LunaResult Open(string fileOrFolder, string? arguments, string success)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = fileOrFolder,
                Arguments = arguments ?? string.Empty,
                UseShellExecute = true
            });
            return new(success, true);
        }
        catch
        {
            return new($"Não consegui abrir {fileOrFolder} neste computador.");
        }
    }

    private static LunaResult OpenBrowser(string url, string success, bool preferChrome = false, bool preferEdge = false)
    {
        try
        {
            if (preferChrome && TryStart("chrome.exe", $"--new-tab \"{url}\""))
                return new(success, true);
            if (preferEdge && TryStart("msedge.exe", $"--new-tab \"{url}\""))
                return new(success, true);

            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            return new(success, true);
        }
        catch
        {
            return new("Não consegui abrir o navegador neste computador.");
        }
    }

    private static bool TryStart(string file, string arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = file, Arguments = arguments, UseShellExecute = true });
            return true;
        }
        catch { return false; }
    }

    private static string Normalize(string value) => value.ToLowerInvariant();
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
        try
        {
            if (File.Exists(_file))
                _messages.AddRange(JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_file))?.TakeLast(500) ?? []);
        }
        catch { }
    }

    public void Remember(string message)
    {
        if (_disposed || string.IsNullOrWhiteSpace(message)) return;
        _messages.Add(message);
        if (_messages.Count > 500) _messages.RemoveRange(0, _messages.Count - 500);
        try
        {
            File.WriteAllText(_file, JsonSerializer.Serialize(_messages, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public void Dispose() => _disposed = true;
}
