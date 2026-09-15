using System.Reflection;
using System.Text.Json;

const string AssemblyName = "LunaPC";
var failures = new List<string>();
var assembly = Assembly.Load(AssemblyName);

static object InvokeStatic(Type type, string method, params object[] args)
{
    var info = type.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
        ?? throw new MissingMethodException(type.FullName, method);
    return info.Invoke(null, args) ?? throw new InvalidOperationException($"{method} returned null");
}

var brainType = assembly.GetType("LunaPC.AiBrain") ?? throw new InvalidOperationException("AiBrain not found");
var knowledgeQuestion = new[]
{
    "O QUE VOCÊ SABE SOBRE O BRASIL?",
    "O QUE É UM COMPUTADOR?",
    "EXPLIQUE INTELIGÊNCIA ARTIFICIAL",
    "PESQUISE SOBRE O BRASIL"
};
foreach (var input in knowledgeQuestion)
{
    var result = (bool)InvokeStatic(brainType, "IsKnowledgeQuestion", input.ToLowerInvariant());
    if (!result) failures.Add($"Knowledge routing failed: {input}");
}

var statusQuestions = new[] { "O QUE APRENDEU?", "STATUS DO TREINAMENTO", "O TREINAMENTO TERMINOU?" };
foreach (var input in statusQuestions)
{
    var result = (bool)InvokeStatic(brainType, "IsTrainingStatusQuestion", input.ToLowerInvariant());
    if (!result) failures.Add($"Training-status routing failed: {input}");
}

var trainingCommands = new[] { "LUNA, TREINE NA INTERNET", "TREINAR COM A WIKIPEDIA", "INICIE O TREINAMENTO ONLINE" };
foreach (var input in trainingCommands)
{
    var result = (bool)InvokeStatic(brainType, "IsInternetTrainingCommand", input.ToLowerInvariant());
    if (!result) failures.Add($"Internet-training routing failed: {input}");
}

var negativeTraining = new[] { "O QUE VOCÊ SABE SOBRE O BRASIL?", "EXPLIQUE O QUE É UM COMPUTADOR" };
foreach (var input in negativeTraining)
{
    var result = (bool)InvokeStatic(brainType, "IsInternetTrainingCommand", input.ToLowerInvariant());
    if (result) failures.Add($"Knowledge question incorrectly routed to training: {input}");
}

var temp = Path.Combine(Path.GetTempPath(), "LunaPreflight-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
try
{
    var corpusPath = Path.Combine(temp, "internet-corpus-v1.jsonl");
    File.WriteAllText(corpusPath, JsonSerializer.Serialize(new { Title = "Brasil", Text = "Brasil é um país da América do Sul. Sua capital é Brasília." }) + Environment.NewLine +
                              JsonSerializer.Serialize(new { Title = "Computador", Text = "Um computador é uma máquina eletrônica capaz de processar dados." }) + Environment.NewLine);

    var knowledgeType = assembly.GetType("LunaPC.KnowledgeMemory") ?? throw new InvalidOperationException("KnowledgeMemory not found");
    var knowledge = Activator.CreateInstance(knowledgeType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { temp }, null)
        ?? throw new InvalidOperationException("Could not create KnowledgeMemory");
    var searchMethod = knowledgeType.GetMethod("Search") ?? throw new MissingMethodException("KnowledgeMemory.Search");
    var matches = searchMethod.Invoke(knowledge, new object[] { "O QUE VOCÊ SABE SOBRE O BRASIL?", 5 }) as System.Collections.IEnumerable;
    var firstTitle = matches?.Cast<object>().Select(x => x.GetType().GetProperty("Title")?.GetValue(x)?.ToString()).FirstOrDefault();
    if (!string.Equals(firstTitle, "Brasil", StringComparison.Ordinal))
        failures.Add($"Knowledge ranking failed: expected Brasil first, got {firstTitle ?? "<none>"}");

    var indexPath = Path.Combine(temp, "knowledge-index-v1.json");
    if (!File.Exists(indexPath)) failures.Add("Knowledge index was not persisted.");

    var statusPath = Path.Combine(temp, "internet-training-status.json");
    File.WriteAllText(statusPath, JsonSerializer.Serialize(new
    {
        running = true,
        documents = 12,
        trainedChunks = 4,
        updatedAt = DateTimeOffset.Now.AddMinutes(-10)
    }));
    var trainingType = assembly.GetType("LunaPC.InternetTrainingService") ?? throw new InvalidOperationException("InternetTrainingService not found");
    using var service = Activator.CreateInstance(trainingType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { temp }, null) as IDisposable;
    using (var statusDoc = JsonDocument.Parse(File.ReadAllText(statusPath)))
    {
        if (statusDoc.RootElement.GetProperty("running").GetBoolean())
            failures.Add("Stale interrupted-training marker was not recovered.");
    }
}
finally
{
    try { Directory.Delete(temp, true); } catch { }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine("LUNA PREFLIGHT: FAIL");
    foreach (var failure in failures) Console.Error.WriteLine(" - " + failure);
    Environment.Exit(1);
}

Console.WriteLine("LUNA PREFLIGHT: PASS");
Console.WriteLine("Routing, local knowledge indexing/ranking, and interrupted-training recovery passed.");
