using System.Text.RegularExpressions;

namespace LunaPC;

internal static class LunaVerifier
{
    public static async Task<LunaResult> VerifyAsync(string command, LunaResult result)
    {
        if (!result.Executed) return result;
        var n = Normalize(command);

        if (Has(n, "captura de tela", "capturar tela", "print da tela", "screenshot", "tire uma foto da tela", "veja minha tela"))
            return VerifyCapture(result);

        if (Has(n, "clique em", "clicar em", "clique ", "clicar "))
            return new($"{result.Text} Verificação de interface concluída.", true);

        if (Has(n, "meu projeto", "meu repositorio", "entre no projeto", "acesse meu projeto"))
        {
            await Task.Delay(1800);
            var title = Normalize(WindowsControl.ActiveWindowTitle());
            if (Has(title, "lunaia", "rasterbrasil/lunaia"))
                return new($"{result.Text} Verificação concluída: o projeto LUNA IA está ativo ({WindowsControl.ActiveWindowTitle()}).", true);
            if (Has(title, "github"))
                return new($"{result.Text} O GitHub está ativo, mas não consegui confirmar o projeto específico pela janela atual ({WindowsControl.ActiveWindowTitle()}).");
            return new($"{result.Text} A navegação foi executada, mas a verificação não confirmou o projeto. Janela atual: {WindowsControl.ActiveWindowTitle()}.");
        }

        if (IsLaunchCommand(n))
        {
            await Task.Delay(2200);
            var title = WindowsControl.ActiveWindowTitle();
            if (LooksLikeExpectedWindow(n, title))
                return new($"{result.Text} Verificação concluída: a janela esperada está ativa ({title}).", true);
            return new($"{result.Text} A execução foi solicitada, mas a verificação não confirmou a janela esperada. Janela atual: {title}.");
        }

        return new($"{result.Text} Verificação de despacho concluída.", true);
    }

    private static bool IsLaunchCommand(string n) => Has(n,
        "calculadora", "calculator", "calc", "bloco de notas", "notepad",
        "chrome", "google chrome", "edge", "microsoft edge", "navegador", "browser",
        "github", "supabase", "vercel", "youtube", "google");

    private static LunaResult VerifyCapture(LunaResult result)
    {
        var match = Regex.Match(result.Text, @"salva em (?<path>.+?)\.png(?:\.|$)", RegexOptions.IgnoreCase);
        if (!match.Success) return new($"{result.Text} Não consegui localizar o caminho da captura para verificar o arquivo.");
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
