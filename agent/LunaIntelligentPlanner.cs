using System.Text.Json;
using System.Text.RegularExpressions;

namespace LunaPC;

internal sealed record LunaPlannedStep(
    string ToolId,
    string Description,
    string? Target = null,
    string? Value = null,
    double Confidence = 0.0);

internal sealed record LunaIntelligentPlan(
    string Goal,
    string Mode,
    string? Answer,
    IReadOnlyList<LunaPlannedStep> Steps);

internal sealed class LunaIntelligentPlanner
{
    private readonly LunaLocalLanguageEngine _language;
    private readonly LunaToolRegistry _tools;

    public LunaIntelligentPlanner(LunaLocalLanguageEngine language, LunaToolRegistry tools)
    {
        _language = language;
        _tools = tools;
    }

    public async Task<LunaIntelligentPlan?> CreateAsync(string goal, string context, CancellationToken cancellationToken = default)
    {
        var catalog = string.Join("\n", _tools.Tools.Select(t => $"- {t.Id}: {t.Description}"));
        var prompt = """
Você é o planejador executivo local da LUNA IA.
Sua função é transformar o objetivo do usuário em uma decisão executável.
Não invente ferramentas. Use somente as ferramentas do catálogo abaixo.
Se o pedido for apenas uma conversa/pergunta que não precisa de ferramenta, use mode=answer e escreva uma resposta curta em answer.
Se o pedido exigir ações, use mode=execute e produza uma sequência ordenada de steps.
Cada step deve representar uma ação concreta que possa ser executada por uma ferramenta do catálogo.
Você pode usar várias etapas e deve considerar dependências: primeiro abrir/ativar o ambiente, depois localizar, clicar, digitar ou verificar quando houver ferramenta adequada.
Não diga que uma ação foi executada: apenas planeje.
Não inclua markdown, explicações fora do JSON ou blocos de código.
Responda SOMENTE com JSON válido neste formato:
{
  "goal":"objetivo resumido",
  "mode":"execute|answer",
  "answer":"resposta se mode=answer, caso contrário null",
  "steps":[
    {"toolId":"id do catálogo","description":"o que esta etapa fará","target":"alvo ou null","value":"valor ou null","confidence":0.0}
  ]
}

Catálogo de ferramentas:
""" + catalog + """

Contexto observado:
""" + context + """

Objetivo do usuário:
""" + goal;

        try
        {
            var raw = await _language.ChatAsync(goal, prompt, cancellationToken);
            var json = ExtractJson(raw);
            if (json is null) return null;
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var mode = root.TryGetProperty("mode", out var modeElement) ? modeElement.GetString() ?? "execute" : "execute";
            var answer = root.TryGetProperty("answer", out var answerElement) && answerElement.ValueKind != JsonValueKind.Null
                ? answerElement.GetString()
                : null;
            var steps = new List<LunaPlannedStep>();
            if (root.TryGetProperty("steps", out var stepsElement) && stepsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in stepsElement.EnumerateArray().Take(8))
                {
                    var toolId = GetString(item, "toolId");
                    if (string.IsNullOrWhiteSpace(toolId) || _tools.FindById(toolId) is null) continue;
                    steps.Add(new LunaPlannedStep(
                        toolId,
                        GetString(item, "description") ?? toolId,
                        GetString(item, "target"),
                        GetString(item, "value"),
                        GetDouble(item, "confidence")));
                }
            }

            if (mode.Equals("answer", StringComparison.OrdinalIgnoreCase))
                return new(goal, "answer", answer, steps);
            if (steps.Count == 0) return null;
            return new(goal, "execute", answer, steps);
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractJson(string raw)
    {
        var text = raw.Trim();
        text = Regex.Replace(text, @"^```(?:json)?\s*|\s*```$", string.Empty, RegexOptions.IgnoreCase).Trim();
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        return text[start..(end + 1)];
    }

    private static string? GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetString()
            : null;

    private static double GetDouble(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.TryGetDouble(out var number) ? number : 0.0;
}
