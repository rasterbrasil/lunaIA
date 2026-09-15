namespace LunaPC;

internal sealed class LunaIntelligentAgent : IDisposable
{
    private readonly LunaLocalLanguageEngine _language = new();
    private readonly LunaToolRegistry _tools = new();
    private readonly LunaDecisionEngine _decision;
    private readonly LunaIntelligentPlanner _planner;
    private readonly LunaCore _fallback;
    private bool _disposed;

    public LunaIntelligentAgent()
    {
        _decision = new LunaDecisionEngine(_tools);
        _planner = new LunaIntelligentPlanner(_language, _tools);
        _fallback = new LunaCore();
    }

    public async Task<LunaResult> ProcessAsync(string input, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LunaIntelligentAgent));
        var text = input.Trim();
        if (string.IsNullOrWhiteSpace(text)) return new("Estou ouvindo. Diga o objetivo que você quer alcançar.");

        // Conversas triviais continuam rápidas e determinísticas.
        var direct = LunaIntentParser.Parse(text);
        if (IsDirectConversation(direct))
            return await _fallback.ProcessAsync(text);

        var context = BuildObservationContext();
        LunaIntelligentPlan? plan;
        try
        {
            // Keep every model operation away from the WinForms message loop.
            plan = await Task.Run(() => _planner.CreateAsync(text, context, cancellationToken), cancellationToken).Unwrap();
        }
        catch (OperationCanceledException) { return new("Interrompi o planejamento desta tarefa."); }
        catch { plan = null; }

        if (plan is not null)
        {
            if (plan.Mode.Equals("answer", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(plan.Answer))
                return new(plan.Answer.Trim());

            if (plan.Steps.Count > 0)
                return await ExecutePlanAsync(plan, cancellationToken);
        }

        // If the local planner cannot produce a valid plan, use the existing
        // deterministic core rather than inventing an action or pretending success.
        return await _fallback.ProcessAsync(text);
    }

    private async Task<LunaResult> ExecutePlanAsync(LunaIntelligentPlan plan, CancellationToken cancellationToken)
    {
        var completed = 0;
        var messages = new List<string>();

        for (var index = 0; index < plan.Steps.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var step = plan.Steps[index];
            var intent = ToIntent(step);
            if (intent is null)
            {
                messages.Add($"Etapa {index + 1} ignorada: não consegui converter a decisão do planejador em uma ação segura.");
                break;
            }

            var decision = _decision.Decide(intent);
            if (decision.Tool is null)
            {
                messages.Add($"Etapa {index + 1} não executada: ferramenta '{step.ToolId}' não está disponível.");
                break;
            }
            if (decision.RequiresConfirmation)
            {
                messages.Add($"Etapa {index + 1} aguardando confirmação: {decision.Tool.Description}.");
                break;
            }

            var before = LunaObserver.Observe();
            LunaResult result;
            try
            {
                result = await decision.Tool.Execute(intent);
            }
            catch (Exception ex)
            {
                messages.Add($"Etapa {index + 1} falhou de forma controlada: {ex.Message}");
                break;
            }

            if (!result.Executed)
            {
                messages.Add($"Etapa {index + 1}: {result.Text}");
                break;
            }

            var after = LunaObserver.Observe();
            var verified = await LunaVerifier.VerifyAsync(intent.RawText, result);
            if (!verified.Executed && CanRetry(step.ToolId))
            {
                var stateChanged = !string.Equals(before.ActiveWindow, after.ActiveWindow, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(before.ScreenFingerprint, after.ScreenFingerprint, StringComparison.OrdinalIgnoreCase);
                if (!stateChanged)
                {
                    await Task.Delay(500, cancellationToken);
                    result = await decision.Tool.Execute(intent);
                    if (result.Executed)
                        verified = await LunaVerifier.VerifyAsync(intent.RawText, result);
                }
            }

            if (!verified.Executed)
            {
                messages.Add($"Etapa {index + 1} não foi confirmada: {verified.Text}");
                break;
            }

            completed++;
            messages.Add($"Etapa {index + 1}: {verified.Text}");
        }

        var status = completed == plan.Steps.Count ? "Plano inteligente concluído." : "Plano inteligente executado parcialmente.";
        return new($"{status} {string.Join(" ", messages)}", completed > 0);
    }

    private static LunaIntent? ToIntent(LunaPlannedStep step)
    {
        var raw = string.IsNullOrWhiteSpace(step.Description) ? step.ToolId : step.Description;
        return step.ToolId.ToLowerInvariant() switch
        {
            "windows.calculator" => new(LunaIntentKind.OpenApplication, raw, Target: "calculator", Confidence: step.Confidence),
            "windows.notepad" => new(LunaIntentKind.OpenApplication, raw, Target: "notepad", Confidence: step.Confidence),
            "windows.type" => new(LunaIntentKind.TypeText, raw, Value: step.Value ?? string.Empty, Confidence: step.Confidence),
            "windows.key" => new(LunaIntentKind.PressKey, raw, Value: step.Value ?? string.Empty, Confidence: step.Confidence),
            "windows.downloads" => new(LunaIntentKind.OpenFolder, raw, Target: "Downloads", Confidence: step.Confidence),
            "windows.documents" => new(LunaIntentKind.OpenFolder, raw, Target: "Documents", Confidence: step.Confidence),
            "web.chrome" => new(LunaIntentKind.OpenWebsite, raw, Target: "chrome", Confidence: step.Confidence),
            "web.edge" => new(LunaIntentKind.OpenWebsite, raw, Target: "edge", Confidence: step.Confidence),
            "web.browser" => new(LunaIntentKind.OpenWebsite, raw, Target: "default-browser", Confidence: step.Confidence),
            "web.github" => new(LunaIntentKind.OpenWebsite, raw, Target: "github", Confidence: step.Confidence),
            "web.github-project" => new(LunaIntentKind.OpenConfiguredProject, raw, Target: "github-project", Confidence: step.Confidence),
            "web.supabase" => new(LunaIntentKind.OpenWebsite, raw, Target: "supabase", Confidence: step.Confidence),
            "web.vercel" => new(LunaIntentKind.OpenWebsite, raw, Target: "vercel", Confidence: step.Confidence),
            "web.youtube" => new(LunaIntentKind.OpenWebsite, raw, Target: "youtube", Confidence: step.Confidence),
            "web.google" => new(LunaIntentKind.OpenWebsite, raw, Target: "google", Confidence: step.Confidence),
            "screen.observe" => new(LunaIntentKind.ObserveScreen, raw, Confidence: step.Confidence),
            "screen.capture" => new(LunaIntentKind.CaptureScreen, raw, Confidence: step.Confidence),
            "screen.click" => new(LunaIntentKind.ClickElement, raw, Value: step.Value ?? step.Target ?? string.Empty, Confidence: step.Confidence),
            _ => null
        };
    }

    private static bool CanRetry(string toolId)
        => toolId.StartsWith("web.", StringComparison.OrdinalIgnoreCase)
            || toolId is "windows.calculator" or "windows.notepad" or "screen.observe";

    private static bool IsDirectConversation(LunaIntent intent)
        => intent.Kind is LunaIntentKind.Greeting
            or LunaIntentKind.AskIdentity
            or LunaIntentKind.AskTime
            or LunaIntentKind.AskDate
            or LunaIntentKind.AskMemory
            or LunaIntentKind.AskActiveWindow;

    private static string BuildObservationContext()
    {
        var observation = LunaObserver.Observe();
        return $"Janela ativa: {observation.ActiveWindow}\nTela: {observation.ScreenWidth}x{observation.ScreenHeight}\nTela possui conteúdo observável: {observation.ScreenHasContent}\nFingerprint da tela: {observation.ScreenFingerprint}";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _language.Dispose();
        _fallback.Dispose();
    }
}
