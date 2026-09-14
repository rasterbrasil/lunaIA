namespace LunaPC;

internal sealed record LunaReasoningStep(
    string Observation,
    string Interpretation,
    string Decision,
    LunaIntent Intent,
    double Confidence = 1.0);

internal sealed record LunaAutonomyPlan(
    string Goal,
    IReadOnlyList<LunaIntent> Intents,
    IReadOnlyList<LunaReasoningStep> Reasoning,
    LunaCognitiveContext Context);

/// <summary>
/// Autonomous goal layer for LUNA. It combines observation, the local
/// cognitive context and intent inference before handing work to tools.
/// This remains bounded and local; a future local reasoning model can replace
/// the inference strategy while keeping the same agent contract.
/// </summary>
internal sealed class LunaAutonomyEngine
{
    private readonly LunaCognitiveKernel _cognition = new();

    public LunaAutonomyPlan Think(string goal)
    {
        var text = goal.Trim();
        var context = _cognition.BuildContext(text);
        var parts = SplitGoal(text);
        var intents = new List<LunaIntent>();
        var reasoning = new List<LunaReasoningStep>();

        foreach (var part in parts)
        {
            var observation = LunaObserver.Observe();
            var intent = LunaIntentParser.Parse(part);
            intent = InferIfNecessary(part, intent, context);

            var interpretation = DescribeInterpretation(intent);
            var decision = ChooseDecision(intent, observation, context);
            var confidence = Math.Min(intent.Confidence, context.Confidence);
            reasoning.Add(new(observation.ActiveWindow, interpretation, decision, intent, confidence));
            intents.Add(intent);
        }

        intents = ReorderByDependencies(intents);
        return new(text, intents, reasoning, context);
    }

    private static LunaIntent InferIfNecessary(string text, LunaIntent intent, LunaCognitiveContext context)
    {
        if (intent.Kind != LunaIntentKind.Unknown) return intent;
        var n = Normalize(text);

        if (Has(n, "meu projeto", "meu repositorio", "meu repositorio no github", "projeto no github"))
            return new(LunaIntentKind.OpenConfiguredProject, text, Target: "github-project", Confidence: Math.Max(0.72, context.Confidence));

        if (Has(n, "observe", "verifique a tela", "veja a tela", "analise a tela"))
            return new(LunaIntentKind.ObserveScreen, text, Confidence: 0.68);

        if (Has(n, "github"))
            return new(LunaIntentKind.OpenWebsite, text, Target: "github", Confidence: 0.68);

        if (Has(n, "supabase"))
            return new(LunaIntentKind.OpenWebsite, text, Target: "supabase", Confidence: 0.68);

        if (Has(n, "vercel"))
            return new(LunaIntentKind.OpenWebsite, text, Target: "vercel", Confidence: 0.68);

        return intent;
    }

    private static string DescribeInterpretation(LunaIntent intent) => intent.Kind switch
    {
        LunaIntentKind.OpenWebsite => $"Entendi que devo acessar {intent.Target}.",
        LunaIntentKind.OpenConfiguredProject => "Entendi que devo localizar o projeto configurado dentro do ambiente do GitHub.",
        LunaIntentKind.ObserveScreen => "Entendi que preciso observar o estado atual da tela antes de decidir o próximo passo.",
        LunaIntentKind.ClickElement => $"Entendi que devo localizar e clicar em '{intent.Value}'.",
        LunaIntentKind.Unknown => "Não tenho evidência suficiente para escolher uma ação confiável.",
        _ => $"Entendi a intenção '{intent.Kind}'."
    };

    private static string ChooseDecision(LunaIntent intent, LunaObservation observation, LunaCognitiveContext context)
    {
        if (intent.Kind == LunaIntentKind.OpenWebsite && WindowsControl.IsBrowserWindowTitle(observation.ActiveWindow))
            return "O navegador já está disponível; vou preservar a janela existente e usar uma nova aba.";

        if (intent.Kind == LunaIntentKind.OpenConfiguredProject)
            return "Vou localizar o projeto pela visão semântica e pelo clique físico, sem usar a barra de endereço como atalho.";

        if (intent.Kind == LunaIntentKind.ObserveScreen)
            return "Vou observar primeiro e usar o resultado para decidir o próximo passo.";

        if (context.Confidence < 0.5)
            return "A confiança é baixa; devo observar mais antes de executar uma ação ambígua.";

        return "Vou selecionar a ferramenta local adequada e verificar o resultado antes de considerar a ação concluída.";
    }

    private static List<LunaIntent> ReorderByDependencies(List<LunaIntent> intents)
    {
        if (intents.Count < 2) return intents;
        var result = new List<LunaIntent>();
        result.AddRange(intents.Where(i => i.Kind == LunaIntentKind.OpenWebsite));
        result.AddRange(intents.Where(i => i.Kind != LunaIntentKind.OpenWebsite));
        return result;
    }

    private static string[] SplitGoal(string text)
    {
        var parts = System.Text.RegularExpressions.Regex.Split(
                text,
                @"\s+(?:(?:e\s+)?(?:depois|em seguida|entao|então)|e\s+(?=(?:entre|abra|acesse|acessar|clique|clicar|verifique|veja|observe|analise|feche|fechar)\b))\s*",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToArray();
        return parts.Length == 0 ? [text] : parts;
    }

    private static string Normalize(string value)
    {
        var form = value.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var chars = form.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark);
        return new string(chars.ToArray()).Normalize(System.Text.NormalizationForm.FormC);
    }

    private static bool Has(string text, params string[] terms) => terms.Any(text.Contains);
}
