using System.Text;
using System.Text.Json;

namespace LunaPC;

internal sealed class AiBrain
{
    private const string DefaultEndpoint = "http://127.0.0.1:11434/api/chat";
    private const string DefaultModel = "qwen3:4b-instruct";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly string _endpoint;
    private readonly string _model;

    public AiBrain()
    {
        _endpoint = Environment.GetEnvironmentVariable("LUNA_LOCAL_AI_URL") ?? DefaultEndpoint;
        _model = Environment.GetEnvironmentVariable("LUNA_LOCAL_AI_MODEL") ?? DefaultModel;
    }

    public string Model => _model;

    public async Task<string?> AskAsync(string userText, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            model = _model,
            stream = false,
            options = new
            {
                temperature = 0.7,
                num_ctx = 8192
            },
            messages = new[]
            {
                new
                {
                    role = "system",
                    content = "Você é LUNA, a inteligência artificial pessoal e privada do Marcos. Você roda localmente no computador dele e deve continuar funcionando sem internet. Responda em português do Brasil, de forma natural, objetiva e útil. Nunca diga que depende de uma API externa. Você é o cérebro conversacional da LUNA PC. Quando o pedido exigir uma ação no computador, explique o que pretende fazer; ações sensíveis ou destrutivas deverão passar por confirmação explícita antes de serem executadas. Não invente que executou uma ação quando apenas respondeu."
                },
                new
                {
                    role = "user",
                    content = userText
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint);
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                if ((int)response.StatusCode == 404)
                    return $"Meu cérebro local está instalado, mas o modelo {_model} ainda não foi encontrado. Instale o modelo uma vez e depois poderei funcionar sem internet.";

                return $"Meu cérebro local respondeu com o código {(int)response.StatusCode}.";
            }

            return ExtractMessageText(body);
        }
        catch (HttpRequestException)
        {
            return "Meu cérebro local ainda não está ligado. Inicie o motor local de IA e tente novamente.";
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return "Meu cérebro local demorou demais para responder. Tente novamente.";
        }
    }

    private static string? ExtractMessageText(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.TryGetProperty("message", out var message) &&
            message.TryGetProperty("content", out var content) &&
            content.ValueKind == JsonValueKind.String)
            return content.GetString();

        return null;
    }
}
