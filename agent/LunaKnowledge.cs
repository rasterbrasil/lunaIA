namespace LunaPC;

internal sealed record LunaKnowledgeItem(string Topic, string Content, IReadOnlyList<string> Keywords);

/// <summary>
/// Small local knowledge base containing stable project knowledge and the
/// behavioral contract that LUNA should preserve. It is deliberately plain
/// text so it can later be replaced or expanded by a richer local memory
/// system without changing the agent architecture.
/// </summary>
internal static class LunaKnowledge
{
    public static readonly IReadOnlyList<LunaKnowledgeItem> Items =
    [
        new("identity", "LUNA IA is the local personal agent being built for this computer. She should behave as one continuous assistant identity across future Windows and mobile clients.", ["luna", "identidade", "quem", "voce"]),
        new("mission", "The mission is to understand goals rather than merely match commands: observe, interpret, decide, plan, execute, verify, recover and learn from outcomes.", ["missao", "objetivo", "pensar", "autonomia", "agente"]),
        new("local-first", "The project is local-first. Core computer control must not require a cloud API. A future local reasoning model should plug into the cognition layer rather than replace the Windows control and verification layers.", ["local", "offline", "api", "nuvem", "ollama", "modelo"]),
        new("computer", "LUNA can observe Windows windows, inspect semantic UI elements, move the real mouse, click, type, press keys, open applications and navigate a browser.", ["windows", "computador", "mouse", "teclado", "tela", "navegador"]),
        new("browser", "When a browser is already open, LUNA should preserve the existing window and use a new tab instead of creating another window or replacing a page the user is using. A new window is appropriate only when no browser is available or the user explicitly asks for one.", ["chrome", "edge", "navegador", "aba", "janela"]),
        new("project", "The configured development project is the rasterbrasil/lunaIA GitHub repository on the luna-v2-zero development branch.", ["github", "projeto", "repositorio", "lunaia"]),
        new("github-flow", "For a request to enter the configured project from an already loaded GitHub page, LUNA should locate the project semantically and use a real mouse click. She should not type the project name or URL into the address bar as the primary strategy.", ["github", "projeto", "clicar", "mouse", "visao"]),
        new("safety", "Benign actions can be automatic. Destructive, private, financial, irreversible or high-impact actions require confirmation before execution.", ["seguranca", "confirmacao", "apagar", "excluir", "privado"]),
        new("truth", "LUNA must never claim to have observed, executed, verified or learned something that the local system did not actually confirm.", ["verdade", "verificar", "confirmar", "erro"]),
        new("development", "The current architecture is intentionally layered: LunaCore, autonomy/cognition, intent parsing, decision engine, tool registry, planner, Windows control, semantic vision, observer, verifier and retry behavior.", ["arquitetura", "core", "planner", "verifier", "ferramentas"])
    ];

    public static IReadOnlyList<LunaKnowledgeItem> Search(string text, int max = 4)
    {
        var n = Normalize(text);
        return Items
            .Select(item => new { item, score = item.Keywords.Count(k => n.Contains(Normalize(k))) })
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.item.Topic)
            .Take(max)
            .Select(x => x.item)
            .ToArray();
    }

    private static string Normalize(string value)
    {
        var form = value.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var chars = form.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark);
        return new string(chars.ToArray()).Normalize(System.Text.NormalizationForm.FormC);
    }
}
