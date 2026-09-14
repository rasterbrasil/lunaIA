namespace LunaKernel;

/// <summary>
/// Núcleo mínimo da LUNA PC 2.0.
/// Não conhece APIs de IA, nuvem, voz ou fornecedores.
/// </summary>
public sealed class LunaKernel
{
    public string Name => "LUNA";
    public string Version => "2.0.0-zero";

    public Task<string> ProcessAsync(string input, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Task.FromResult("Estou pronta. Diga o que você precisa.");

        var text = input.Trim();
        var normalized = text.ToLowerInvariant();

        if (normalized.Contains("quem é você") || normalized.Contains("quem e voce"))
            return Task.FromResult("Eu sou a LUNA. Este é o meu novo núcleo local, construído para evoluir sem depender de uma API de IA em nuvem.");

        if (normalized.Contains("status"))
            return Task.FromResult("Núcleo LUNA 2.0 ativo. O motor cognitivo ainda será conectado a este núcleo por uma interface local.");

        return Task.FromResult($"Recebi: {text}. O núcleo está funcionando. O próximo módulo será o cérebro local.");
    }
}
