namespace LunaPC;

internal sealed record LunaDecision(
    LunaIntent Intent,
    LunaTool? Tool,
    string Explanation,
    bool RequiresConfirmation);

internal sealed class LunaDecisionEngine
{
    private readonly LunaToolRegistry _registry;

    public LunaDecisionEngine(LunaToolRegistry registry) => _registry = registry;

    public LunaDecision Decide(LunaIntent intent)
    {
        if (intent.Kind == LunaIntentKind.Unknown)
            return new(intent, null, "Não encontrei uma ferramenta local adequada para esta intenção.", false);

        var tool = ResolveTool(intent);
        if (tool is null)
            return new(intent, null, "A intenção foi reconhecida, mas ainda não existe uma ferramenta ligada a ela.", false);

        return new(intent, tool, $"Intenção '{intent.Kind}' resolvida para a ferramenta '{tool.Id}'.", tool.Risk == LunaRisk.Confirm);
    }

    private LunaTool? ResolveTool(LunaIntent intent)
    {
        var raw = Normalize(intent.RawText);
        return intent.Kind switch
        {
            LunaIntentKind.OpenApplication when intent.Target == "calculator" => _registry.Tools.FirstOrDefault(t => t.Id == "windows.calculator"),
            LunaIntentKind.OpenApplication when intent.Target == "notepad" => _registry.Tools.FirstOrDefault(t => t.Id == "windows.notepad"),
            LunaIntentKind.OpenWebsite when intent.Target == "chrome" => _registry.Tools.FirstOrDefault(t => t.Id == "web.chrome"),
            LunaIntentKind.OpenWebsite when intent.Target == "edge" => _registry.Tools.FirstOrDefault(t => t.Id == "web.edge"),
            LunaIntentKind.OpenWebsite when intent.Target == "github" => _registry.Tools.FirstOrDefault(t => t.Id == "web.github"),
            LunaIntentKind.OpenWebsite when intent.Target == "supabase" => _registry.Tools.FirstOrDefault(t => t.Id == "web.supabase"),
            LunaIntentKind.OpenWebsite when intent.Target == "vercel" => _registry.Tools.FirstOrDefault(t => t.Id == "web.vercel"),
            LunaIntentKind.OpenWebsite when intent.Target == "youtube" => _registry.Tools.FirstOrDefault(t => t.Id == "web.youtube"),
            LunaIntentKind.OpenWebsite when intent.Target == "google" => _registry.Tools.FirstOrDefault(t => t.Id == "web.google"),
            LunaIntentKind.OpenWebsite when intent.Target == "default-browser" => _registry.Tools.FirstOrDefault(t => t.Id == "web.browser"),
            _ => _registry.Resolve(raw)
        };
    }

    private static string Normalize(string value)
    {
        var form = value.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var chars = form.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark);
        return new string(chars.ToArray()).Normalize(System.Text.NormalizationForm.FormC);
    }
}
