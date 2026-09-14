using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace LunaPC;

internal sealed class AiBrain
{
    private readonly HttpClient _http = new();
    private readonly string? _apiKey;
    private readonly string _model;

    public AiBrain()
    {
        _apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        _model = Environment.GetEnvironmentVariable("LUNA_AI_MODEL") ?? "gpt-5.6";
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public async Task<string?> AskAsync(string userText, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            return null;

        var payload = new
        {
            model = _model,
            input = new[]
            {
                new
                {
                    role = "system",
                    content = "Você é LUNA, uma assistente pessoal para Windows. Responda em português do Brasil, de forma natural, objetiva e útil. Não diga que é um chatbot. Quando o pedido exigir uma ação no computador, explique o que pretende fazer; ações sensíveis ou destrutivas deverão passar por confirmação explícita antes de serem executadas."
                },
                new
                {
                    role = "user",
                    content = userText
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        using var response = await _http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            return $"A conexão com meu cérebro de IA falhou. Código {((int)response.StatusCode)}.";

        return ExtractOutputText(body);
    }

    private static string? ExtractOutputText(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.TryGetProperty("output_text", out var outputText) &&
            outputText.ValueKind == JsonValueKind.String)
            return outputText.GetString();

        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var part in content.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    return text.GetString();
            }
        }

        return null;
    }
}
