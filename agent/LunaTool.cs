namespace LunaPC;

internal sealed record LunaTool(
    string Id,
    string Description,
    Func<LunaIntent, Task<LunaResult>> Execute,
    bool RequiresConfirmation = false);
