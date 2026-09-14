namespace LunaPC;

internal sealed record LunaDecision(
    LunaIntent Intent,
    LunaTool? Tool,
    string Explanation,
    bool RequiresConfirmation);

internal sealed class LunaDecisionEngine
{
    private readonly LunaToolRegistry _registry;

    public LunaDecisionEngine(LunaToolRegistry registry) => _registry = registry;

    public LunaDecision Decide(LunaIntent intent)
    {
        if (intent.Kind == LunaIntentKind.Unknown)
            return new(intent, null, "Não encontrei uma ferramenta local adequada para esta intenção.", false);

        var tool = _registry.Resolve(intent);
        if (tool is null)
            return new(intent, null, "A intenção foi reconhecida, mas ainda não existe uma ferramenta ligada a ela.", false);

        return new(intent, tool, $"Escolhi a ferramenta '{tool.Id}' para executar '{intent.Kind}'.", tool.RequiresConfirmation);
    }
}
