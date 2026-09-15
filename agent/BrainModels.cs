namespace LunaPC;

internal sealed class BrainDecision
{
    public string Intent { get; set; } = "conversar";
    public string Goal { get; set; } = "";
    public string Interpretation { get; set; } = "";
    public List<string> Plan { get; set; } = new();
    public List<string> Assumptions { get; set; } = new();
    public List<string> RelevantContext { get; set; } = new();
    public bool NeedsClarification { get; set; }
    public string ClarificationQuestion { get; set; } = "";
    public string Response { get; set; } = "";
}
