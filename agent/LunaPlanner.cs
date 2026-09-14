namespace LunaPC;

internal sealed record LunaStep(string Id, string Description, Func<Task<LunaResult>> Execute);

internal sealed class LunaPlan
{
    public string Goal { get; }
    public IReadOnlyList<LunaStep> Steps { get; }

    public LunaPlan(string goal, IReadOnlyList<LunaStep> steps)
    {
        Goal = goal;
        Steps = steps;
    }
}

internal sealed class LunaPlanner
{
    public LunaPlan CreatePlan(string goal, IEnumerable<LunaStep> steps)
        => new(goal.Trim(), steps.ToList());

    public async Task<LunaResult> ExecuteAsync(LunaPlan plan)
    {
        var completed = 0;
        var messages = new List<string>();

        foreach (var step in plan.Steps)
        {
            try
            {
                var result = await step.Execute();
                messages.Add($"{step.Description}: {result.Text}");
                if (result.Executed) completed++;
            }
            catch (Exception ex)
            {
                messages.Add($"{step.Description}: falhou de forma controlada ({ex.Message})");
                break;
            }
        }

        var status = completed == plan.Steps.Count && plan.Steps.Count > 0 ? "Plano concluído." : "Plano executado parcialmente.";
        return new($"{status} {string.Join(" ", messages)}", completed > 0);
    }
}
