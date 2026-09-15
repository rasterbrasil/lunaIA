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
    public List<BrainAction> Actions { get; set; } = new();
    public string Response { get; set; } = "";
}

internal sealed class BrainAction
{
    public string Type { get; set; } = "";
    public string Target { get; set; } = "";
    public string Value { get; set; } = "";
    public string Path { get; set; } = "";
    public string Destination { get; set; } = "";
    public string Url { get; set; } = "";
    public string Arguments { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public string Risk { get; set; } = "safe";
}
