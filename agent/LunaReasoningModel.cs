namespace LunaPC;

internal sealed record LunaModelRequest(
    string Goal,
    LunaCognitiveContext Context,
    IReadOnlyList<LunaIntent> CandidateIntents);

internal sealed record LunaModelResponse(
    LunaIntent? Intent,
    string Strategy,
    double Confidence,
    bool NeedsClarification,
    string Summary);

/// <summary>
/// Contract for the reasoning engine that will eventually be backed by an
/// on-device language model. Keeping this contract local means the model can
/// evolve independently from Windows control, memory, vision and safety.
/// </summary>
internal interface ILunaReasoningModel
{
    Task<LunaModelResponse> ReasonAsync(LunaModelRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Transitional local reasoner. It does not pretend to be a language model;
/// it selects among already grounded intents using context and confidence.
/// This is the safe bridge to a future fully local LLM.
/// </summary>
internal sealed class LunaLocalReasoningModel : ILunaReasoningModel
{
    public Task<LunaModelResponse> ReasonAsync(LunaModelRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var intent = request.CandidateIntents
            .OrderByDescending(i => i.Confidence)
            .FirstOrDefault(i => i.Kind != LunaIntentKind.Unknown);

        if (intent is null)
            return Task.FromResult(new LunaModelResponse(null, "clarify", 0.25, true,
                "Ainda não tenho evidência local suficiente para escolher uma intenção com segurança."));

        var strategy = intent.Kind switch
        {
            LunaIntentKind.OpenConfiguredProject => "preserve-context-semantic-click-verify",
            LunaIntentKind.OpenWebsite => "preserve-browser-use-new-tab-verify",
            LunaIntentKind.ClickElement => "observe-locate-click-observe-verify",
            LunaIntentKind.ObserveScreen => "observe-without-action",
            LunaIntentKind.CaptureScreen => "capture-and-verify",
            _ => "execute-local-tool-and-verify"
        };

        var confidence = Math.Min(request.Context.Confidence, intent.Confidence);
        var summary = $"Intenção selecionada: {intent.Kind}. Estratégia local: {strategy}.";
        return Task.FromResult(new LunaModelResponse(intent, strategy, confidence, confidence < 0.45, summary));
    }
}
