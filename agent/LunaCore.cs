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
        var n = text.ToLowerInvariant();
        _memory.Remember(text);

        if (Has(n, "quem é você", "quem e voce", "o que você é", "o que voce e"))
            return new("Eu sou a LUNA. Este é meu núcleo local inicial: independente, privado e sem API de nuvem. A inteligência profunda será construída nas próximas camadas.");
        if (Has(n, "olá", "ola", "oi", "bom dia", "boa tarde", "boa noite"))
            return new("Olá. Estou aqui. Meu núcleo local e minha memória local estão funcionando.");
        if (Has(n, "como você está", "como voce esta"))
            return new("Estou funcionando normalmente. Ainda estou na primeira etapa, então não vou fingir que tenho capacidades que ainda não construímos.");
        if (Has(n, "que horas", "hora agora"))
            return new($"Agora são {DateTime.Now:HH:mm}.");
        if (Has(n, "que dia", "data de hoje", "hoje é", "hoje e"))
            return new($"Hoje é {DateTime.Now:dd/MM/yyyy}.");
        if (Has(n, "abra o bloco de notas", "abrir bloco de notas", "abre o bloco de notas"))
            return Open("notepad.exe", "Abrindo o Bloco de Notas.");
        if (Has(n, "abra a calculadora", "abrir calculadora", "abre a calculadora"))
            return Open("calc.exe", "Abrindo a Calculadora.");
        if (Has(n, "abra o chrome", "abrir o chrome", "abre o chrome", "abra chrome"))
            return Open("chrome.exe", "Abrindo o Chrome.");
        if (Has(n, "memória", "memoria"))
            return new($"Minha memória local contém {_memory.Count} mensagens nesta instalação.");

        return new("Recebi sua mensagem e a guardei na memória local. O próximo passo será conectar nosso próprio motor de raciocínio, sem transformar a LUNA em uma simples interface de outro serviço.");
    }

    private static LunaResult Open(string file, string success)
    {
        try { Process.Start(new ProcessStartInfo { FileName = file, UseShellExecute = true }); return new(success, true); }
        catch { return new($"Não consegui abrir {file} neste computador."); }
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
