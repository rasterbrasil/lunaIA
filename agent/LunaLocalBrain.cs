namespace LunaPC;

internal sealed record LunaThought(
    string Goal,
    LunaObservation Observation,
    IReadOnlyList<LunaKnowledgeItem> Knowledge,
    IReadOnlyList<string> CandidateStrategies,
    string SelectedStrategy,
    string Rationale,
    double Confidence,
    bool NeedsMoreObservation);

internal sealed record LunaBrainDecision(
    LunaIntent Intent,
    string Strategy,
    string Rationale,
    double Confidence,
    bool NeedsConfirmation);

/// <summary>
/// LUNA's local reasoning brain. It deliberately separates understanding a
/// goal from executing it. Today the deliberation engine is deterministic and
/// local; a future on-device language model can consume the same Thought and
/// return richer interpretations without changing the agent/tool contract.
/// </summary>
internal sealed class LunaLocalBrain
{
    private readonly LunaCognitiveKernel _cognition = new();
    private readonly LunaDecisionEngine _decisions;

    public LunaLocalBrain(LunaToolRegistry tools)
    {
        _decisions = new LunaDecisionEngine(tools);
    }

    public LunaThought Think(string goal)
    {
        var context = _cognition.BuildContext(goal);
        var candidates = BuildCandidates(goal, context.Observation, context.RelevantKnowledge);
        var selected = SelectStrategy(goal, context.Observation, candidates);
        var confidence = ScoreConfidence(goal, context, selected);
        var needsObservation = selected == "observe-first" || confidence < 0.55;
        var rationale = BuildRationale(goal, context, selected, confidence);

        return new(
            goal.Trim(),
            context.Observation,
            context.RelevantKnowledge,
            candidates,
            selected,
            rationale,
            confidence,
            needsObservation);
    }

    public LunaBrainDecision Decide(LunaIntent intent, LunaThought thought)
    {
        var decision = _decisions.Decide(intent);
        var confirmation = decision.RequiresConfirmation;
        var confidence = Math.Min(intent.Confidence, thought.Confidence);
        var strategy = ChooseExecutionStrategy(intent, thought);
        var rationale = $"{thought.Rationale} {decision.Explanation} Estratégia: {strategy}.";
        return new(intent, strategy, rationale, confidence, confirmation);
    }

    public bool ShouldReconsider(LunaThought before, LunaObservation after, LunaResult result)
    {
        if (!result.Executed) return true;
        if (before.IntentWasObservationOnly()) return false;
        return string.Equals(before.Observation.ActiveWindow, after.ActiveWindow, StringComparison.OrdinalIgnoreCase)
            && string.Equals(before.Observation.ScreenFingerprint, after.ScreenFingerprint, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> BuildCandidates(string goal, LunaObservation observation, IReadOnlyList<LunaKnowledgeItem> knowledge)
    {
        var n = Normalize(goal);
        var candidates = new List<string>();

        if (Has(n, "observe", "analise", "veja", "verifique", "olhe")) candidates.Add("observe-first");
        if (Has(n, "github", "projeto", "repositorio", "supabase", "vercel", "youtube", "google")) candidates.Add("preserve-context-and-navigate");
        if (Has(n, "clique", "clicar", "selecione", "selecione")) candidates.Add("locate-semantic-element-then-click");
        if (Has(n, "abra", "abrir", "acesse", "entrar", "entre")) candidates.Add("inspect-current-state-then-open");
        if (knowledge.Count > 0) candidates.Add("use-relevant-local-knowledge");
        if (observation.ScreenHasContent) candidates.Add("compare-before-and-after");
        candidates.Add("execute-with-verification");

        return candidates.Distinct().ToArray();
    }

    private static string SelectStrategy(string goal, LunaObservation observation, IReadOnlyList<string> candidates)
    {
        var n = Normalize(goal);
        if (Has(n, "observe", "analise", "verifique a tela", "veja a tela")) return "observe-first";
        if (Has(n, "meu projeto", "meu repositorio") && WindowsControl.IsBrowserWindowTitle(observation.ActiveWindow))
            return "preserve-browser-context-and-use-semantic-navigation";
        if (Has(n, "clique", "clicar", "selecione")) return "locate-semantic-element-then-click";
        if (Has(n, "abra", "abrir", "acesse", "entre")) return "inspect-state-then-execute-and-verify";
        return candidates.Contains("use-relevant-local-knowledge")
            ? "use-context-and-verify"
            : "execute-with-verification";
    }

    private static double ScoreConfidence(string goal, LunaCognitiveContext context, string selected)
    {
        var score = context.Confidence;
        if (selected == "observe-first") score += 0.08;
        if (selected.Contains("semantic", StringComparison.OrdinalIgnoreCase)) score += 0.06;
        if (context.RelevantKnowledge.Count > 0) score += 0.04;
        if (context.Observation.ScreenHasContent) score += 0.03;
        if (goal.Trim().Length < 4) score -= 0.20;
        return LunaMath.Clamp(score, 0.10, 0.99);
    }

    private static string BuildRationale(string goal, LunaCognitiveContext context, string selected, double confidence)
    {
        var window = string.IsNullOrWhiteSpace(context.Observation.ActiveWindow) ? "sem janela identificada" : context.Observation.ActiveWindow;
        var knowledge = context.RelevantKnowledge.Count == 0 ? "sem conhecimento local adicional" : $"{context.RelevantKnowledge.Count} itens de conhecimento relevantes";
        return $"Objetivo entendido como '{goal.Trim()}'. Estado atual: '{window}'. {knowledge}. Escolhi '{selected}' com confiança {confidence:P0}.";
    }

    private static string ChooseExecutionStrategy(LunaIntent intent, LunaThought thought) => intent.Kind switch
    {
        LunaIntentKind.OpenConfiguredProject => "visão semântica + clique físico + verificação",
        LunaIntentKind.ClickElement => "localização semântica + clique físico + nova observação",
        LunaIntentKind.OpenWebsite => "preservação do navegador + nova aba + verificação",
        LunaIntentKind.ObserveScreen => "observação local sem ação",
        _ when thought.NeedsMoreObservation => "observar novamente antes de uma ação ambígua",
        _ => "ferramenta local + verificação do resultado"
    };

    private static string Normalize(string value)
    {
        var form = value.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var chars = form.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark);
        return new string(chars.ToArray()).Normalize(System.Text.NormalizationForm.FormC);
    }

    private static bool Has(string text, params string[] terms) => terms.Any(text.Contains);
}

internal static class LunaThoughtExtensions
{
    public static bool IntentWasObservationOnly(this LunaThought thought)
        => thought.SelectedStrategy == "observe-first";
}
