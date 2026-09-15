namespace LunaPC;

internal enum LunaIntentKind
{
    Unknown,
    OpenApplication,
    OpenWebsite,
    OpenConfiguredProject,
    SearchWeb,
    ObserveScreen,
    CaptureScreen,
    ClickElement,
    TypeText,
    PressKey,
    OpenFolder,
    AskIdentity,
    AskCapabilities,
    AskTime,
    AskDate,
    AskMemory,
    AskActiveWindow,
    Greeting
}

internal sealed record LunaIntent(
    LunaIntentKind Kind,
    string RawText,
    string? Target = null,
    string? Value = null,
    double Confidence = 1.0);

internal static class LunaIntentParser
{
    public static LunaIntent Parse(string input)
    {
        var text = input.Trim();
        var n = Normalize(text);
        if (string.IsNullOrWhiteSpace(n)) return new(LunaIntentKind.Unknown, text, Confidence: 0);

        if (Has(n, "quem e voce", "o que voce e", "quem e a luna", "o que e a luna")) return new(LunaIntentKind.AskIdentity, text);
        if (Has(n, "o que sabe fazer", "o que voce sabe fazer", "o que consegue fazer", "o que voce consegue fazer", "quais suas capacidades", "quais sao suas capacidades", "o que voce pode fazer", "o que pode fazer", "como voce pode me ajudar")) return new(LunaIntentKind.AskCapabilities, text);
        if (Has(n, "que horas", "hora agora", "horario agora")) return new(LunaIntentKind.AskTime, text);
        if (Has(n, "que dia", "data de hoje", "hoje e")) return new(LunaIntentKind.AskDate, text);
        if (Has(n, "memoria", "o que voce lembra")) return new(LunaIntentKind.AskMemory, text);
        if (Has(n, "qual janela esta ativa", "qual janela ativa", "qual e a janela ativa", "qual e a janela que esta ativa", "qual janela esta aberta"))
            return new(LunaIntentKind.AskActiveWindow, text);
        if (Has(n, "ola", "oi", "bom dia", "boa tarde", "boa noite")) return new(LunaIntentKind.Greeting, text);
        if (Has(n, "observar tela", "observe minha tela", "observe a tela", "o que esta na tela", "olhe minha tela", "analise minha tela", "analise a tela"))
            return new(LunaIntentKind.ObserveScreen, text);
        if (Has(n, "tire uma foto da tela", "tire uma foto da minha tela", "captura de tela", "capturar tela", "print da tela", "screenshot", "veja minha tela"))
            return new(LunaIntentKind.CaptureScreen, text);

        var click = System.Text.RegularExpressions.Regex.Match(text, @"^\s*(?:luna[, ]*)?(?:clique|clicar|clique em|clicar em|pressione o botao|selecione)\s+(.+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (click.Success) return new(LunaIntentKind.ClickElement, text, Value: click.Groups[1].Value.Trim());

        if (Has(n, "entre no meu projeto", "entre no projeto", "abra meu projeto", "abrir meu projeto", "acesse meu projeto", "acessar meu projeto", "va para meu projeto", "ir para meu projeto", "me leve ao meu projeto"))
            return new(LunaIntentKind.OpenConfiguredProject, text, Target: "github-project");

        var search = System.Text.RegularExpressions.Regex.Match(text, @"^\s*(?:luna[, ]*)?(?:pesquise|pesquisar|procure|procurar|busque|buscar|pesquisa|pesquisa na internet)\s+(.+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (search.Success) return new(LunaIntentKind.SearchWeb, text, Value: search.Groups[1].Value.Trim());

        var type = System.Text.RegularExpressions.Regex.Match(text, @"^\s*(?:luna[, ]*)?(?:digite|escreva|escrever|coloque|preencha)\s+(.+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (type.Success) return new(LunaIntentKind.TypeText, text, Value: type.Groups[1].Value.Trim());

        var key = System.Text.RegularExpressions.Regex.Match(text, @"^\s*(?:luna[, ]*)?(?:pressione|aperte|tecla)\s+(.+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (key.Success) return new(LunaIntentKind.PressKey, text, Value: key.Groups[1].Value.Trim());

        if (Has(n, "downloads", "pasta downloads")) return new(LunaIntentKind.OpenFolder, text, Target: "Downloads");
        if (Has(n, "meus documentos", "documentos", "pasta documentos")) return new(LunaIntentKind.OpenFolder, text, Target: "Documents");
        if (Has(n, "bloco de notas", "notepad")) return new(LunaIntentKind.OpenApplication, text, Target: "notepad");
        if (Has(n, "calculadora", "calculator", "calc")) return new(LunaIntentKind.OpenApplication, text, Target: "calculator");
        if (Has(n, "chrome", "google chrome")) return new(LunaIntentKind.OpenWebsite, text, Target: "chrome");
        if (Has(n, "edge", "microsoft edge")) return new(LunaIntentKind.OpenWebsite, text, Target: "edge");
        if (Has(n, "github", "site do github", "pagina do github")) return new(LunaIntentKind.OpenWebsite, text, Target: "github");
        if (Has(n, "supabase", "site do supabase")) return new(LunaIntentKind.OpenWebsite, text, Target: "supabase");
        if (Has(n, "vercel", "site da vercel")) return new(LunaIntentKind.OpenWebsite, text, Target: "vercel");
        if (Has(n, "youtube", "site do youtube")) return new(LunaIntentKind.OpenWebsite, text, Target: "youtube");
        if (Has(n, "google", "site do google")) return new(LunaIntentKind.OpenWebsite, text, Target: "google");
        if (Has(n, "navegador", "browser", "internet", "aba do navegador", "aba no navegador")) return new(LunaIntentKind.OpenWebsite, text, Target: "default-browser");
        if (Has(n, "nova aba", "nova guia", "abra uma aba")) return new(LunaIntentKind.PressKey, text, Value: "ctrl+t");
        if (Has(n, "selecionar tudo", "selecione tudo")) return new(LunaIntentKind.PressKey, text, Value: "ctrl+a");
        if (Has(n, "copiar")) return new(LunaIntentKind.PressKey, text, Value: "ctrl+c");
        if (Has(n, "colar")) return new(LunaIntentKind.PressKey, text, Value: "ctrl+v");
        if (Has(n, "atualizar pagina", "recarregar pagina", "atualize a pagina")) return new(LunaIntentKind.PressKey, text, Value: "f5");

        return new(LunaIntentKind.Unknown, text, Confidence: 0.1);
    }

    private static string Normalize(string value)
    {
        var form = value.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var chars = form.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark);
        return new string(chars.ToArray()).Normalize(System.Text.NormalizationForm.FormC);
    }

    private static bool Has(string text, params string[] terms) => terms.Any(text.Contains);
}
