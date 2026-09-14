namespace LunaPC;

internal sealed record LunaCognitiveContext(
    string Goal,
    LunaObservation Observation,
    IReadOnlyList<string> RelevantPrinciples,
    IReadOnlyList<string> AvailableCapabilities,
    double Confidence);

/// <summary>
/// Local reasoning layer. It does not pretend to be a full language model.
/// It combines the goal, desktop observation, LUNA's charter and known
/// capabilities to produce a bounded context that planners can use.
/// A future local model can consume the same context without changing the
/// execution and verification layers.
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
        if (Has(n, "falhou", "erro", "nao funcionou", "não funcionou"))
            principles.Add(LunaIdentity.Principles[5]);
        if (principles.Count == 0)
            principles.Add(LunaIdentity.Principles[3]);

        var confidence = EstimateConfidence(n);
        return new(goal.Trim(), observation, principles.Distinct().ToArray(), LunaMath.Clamp(confidence, 0.15, 0.98));
    }

    private static double EstimateConfidence(string n)
    {
        var score = 0.35;
        if (Has(n, "luna")) score += 0.05;
        if (Has(n, "abra", "abrir", "acesse", "entre", "clique", "observe", "digite", "pressione")) score += 0.25;
        if (Has(n, "github", "chrome", "edge", "supabase", "vercel", "youtube", "google", "calculadora", "notepad")) score += 0.20;
        if (n.Length > 12) score += 0.08;
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
