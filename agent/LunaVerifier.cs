using System.Text.RegularExpressions;

namespace LunaPC;

internal static class LunaVerifier
{
    public static async Task<LunaResult> VerifyAsync(string command, LunaResult result)
    {
        if (!result.Executed)
            return result;

        var n = Normalize(command);

        // A captura é verificável de forma objetiva: o arquivo precisa existir e ter conteúdo.
        if (Has(n, "captura de tela", "capturar tela", "print da tela", "screenshot", "tire uma foto da tela", "veja minha tela"))
            return VerifyCapture(result);

        // Para aplicações gráficas, confirmamos a mudança da janela ativa após um pequeno intervalo.
        if (Has(n, "calculadora", "calculator", "calc", "bloco de notas", "notepad", "chrome", "google chrome", "edge", "microsoft edge", "navegador", "browser", "github", "supabase", "vercel", "youtube", "google"))
        {
            await Task.Delay(800);
            var title = WindowsControl.ActiveWindowTitle();
            if (LooksLikeExpectedWindow(n, title))
                return new($"{result.Text} Verificação concluída: a janela esperada está ativa ({title}).", true);

            return new($"{result.Text} A execução foi solicitada, mas a verificação não confirmou a janela esperada. Janela atual: {title}.");
        }

        // SendKeys não oferece confirmação do estado interno do aplicativo.
        // Aqui confirmamos apenas que o comando foi despachado pelo núcleo.
        return new($"{result.Text} Verificação de despacho concluída.", true);
    }

    private static LunaResult VerifyCapture(LunaResult result)
    {
        var match = Regex.Match(result.Text, @"salva em (?<path>.+?)\.png(?:\.|$)", RegexOptions.IgnoreCase);
        if (!match.Success)
            return new($"{result.Text} Não consegui localizar o caminho da captura para verificar o arquivo.");

        var path = match.Groups["path"].Value.Trim() + ".png";
        try
        {
            var info = new FileInfo(path);
            if (info.Exists && info.Length > 0)
                return new($"{result.Text} Verificação concluída: o arquivo existe e tem {info.Length:N0} bytes.", true);
        }
        catch { }

        return new($"{result.Text} A captura foi solicitada, mas não consegui confirmar o arquivo no disco.");
    }

    private static bool LooksLikeExpectedWindow(string command, string title)
    {
        var t = Normalize(title);
        if (Has(command, "calculadora", "calculator", "calc")) return Has(t, "calculadora", "calculator");
        if (Has(command, "bloco de notas", "notepad")) return Has(t, "bloco de notas", "notepad");
        if (Has(command, "chrome", "google chrome")) return Has(t, "chrome", "google");
        if (Has(command, "edge", "microsoft edge")) return Has(t, "edge");
        if (Has(command, "navegador", "browser")) return Has(t, "chrome", "edge", "firefox", "opera", "brave", "vivaldi", "google");
        if (Has(command, "github")) return Has(t, "github", "chrome", "edge", "firefox", "opera", "brave", "vivaldi");
        if (Has(command, "supabase")) return Has(t, "supabase", "chrome", "edge", "firefox", "opera", "brave", "vivaldi");
        if (Has(command, "vercel")) return Has(t, "vercel", "chrome", "edge", "firefox", "opera", "brave", "vivaldi");
        if (Has(command, "youtube")) return Has(t, "youtube", "chrome", "edge", "firefox", "opera", "brave", "vivaldi");
        if (Has(command, "google")) return Has(t, "google", "chrome", "edge", "firefox", "opera", "brave", "vivaldi");
        return true;
    }

    private static string Normalize(string value)
    {
        var form = value.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var chars = form.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark);
        return new string(chars.ToArray()).Normalize(System.Text.NormalizationForm.FormC);
    }

    private static bool Has(string text, params string[] terms) => terms.Any(text.Contains);
}
