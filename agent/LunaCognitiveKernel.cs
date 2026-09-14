namespace LunaPC;

internal sealed record LunaCognitiveContext(
    string Goal,
    LunaObservation Observation,
    IReadOnlyList<string> RelevantPrinciples,
    IReadOnlyList<string> AvailableCapabilities,
    IReadOnlyList<LunaKnowledgeItem> RelevantKnowledge,
    double Confidence);

/// <summary>
/// Local cognition layer. It combines the goal, live desktop observation,
/// LUNA's identity, project knowledge and available capabilities. It is a
/// bounded reasoning layer today; a future local reasoning model can consume
/// this same context without changing the agent's tools or safety pipeline.
/// </summary>
internal sealed class LunaCognitiveKernel
{
    public LunaCognitiveContext BuildContext(string goal)
    {
        var observation = LunaObserver.Observe();
        var n = Normalize(goal);
        var principles = new List<string>();

        if (Has(n, "abra", "abrir", "acesse", "entrar", "entre", "navegador", "site"))
            principles.Add(LunaIdentity.Principles[1]);
        if (Has(n, "clique", "clicar", "selecione", "selecionar"))
            principles.Add(LunaIdentity.Principles[0]);
        if (Has(n, "apague", "apagar", "exclua", "excluir", "delete", "remova", "remover", "envie", "enviar", "publique", "publicar"))
            principles.Add(LunaIdentity.Principles[7]);
        if (Has(n, "falhou", "erro", "nao funcionou"))
            principles.Add(LunaIdentity.Principles[5]);
        if (principles.Count == 0)
            principles.Add(LunaIdentity.Principles[3]);

        var knowledge = LunaKnowledge.Search(goal);
        var confidence = EstimateConfidence(n, knowledge.Count);
        return new(
            goal.Trim(),
            observation,
            principles.Distinct().ToArray(),
            LunaIdentity.Capabilities,
            knowledge,
            LunaMath.Clamp(confidence, 0.15, 0.98));
    }

    private static double EstimateConfidence(string n, int knowledgeHits)
    {
        var score = 0.30;
        if (Has(n, "luna")) score += 0.05;
        if (Has(n, "abra", "abrir", "acesse", "entre", "clique", "observe", "digite", "pressione", "verifique")) score += 0.25;
        if (Has(n, "github", "chrome", "edge", "supabase", "vercel", "youtube", "google", "calculadora", "notepad")) score += 0.20;
        score += Math.Min(0.15, knowledgeHits * 0.04);
        if (n.Length > 20) score += 0.05;
        return score;
    }

    private static string Normalize(string value)
    {
        var form = value.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var chars = form.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark);
        return new string(chars.ToArray()).Normalize(System.Text.NormalizationForm.FormC);
    }

    private static bool Has(string text, params string[] terms) => terms.Any(text.Contains);
}

internal static class LunaMath
{
    public static double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(max, value));
}
