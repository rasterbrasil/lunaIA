using System.Diagnostics;
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

// Teste de integração do AiBrain isolado: executa o mesmo ThinkAsync usado pelo agente.
try
{
    var brain = Activator.CreateInstance(brainType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, null, null)
        ?? throw new InvalidOperationException("Could not create AiBrain");
    try
    {
        var thinkMethod = brainType.GetMethod("ThinkAsync", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMethodException("AiBrain.ThinkAsync");
        var stopwatch = Stopwatch.StartNew();
        var task = thinkMethod.Invoke(brain, new object[] { "Quem é você?", CancellationToken.None }) as Task
            ?? throw new InvalidOperationException("ThinkAsync did not return a Task");
        task.GetAwaiter().GetResult();
        stopwatch.Stop();

        var decision = task.GetType().GetProperty("Result")?.GetValue(task);
        var response = decision?.GetType().GetProperty("Response")?.GetValue(decision)?.ToString() ?? string.Empty;
        if (!response.Contains("Eu sou a LUNA", StringComparison.Ordinal))
            failures.Add($"Real ThinkAsync identity response failed: {response}");
        if (response.Contains("último treinamento", StringComparison.OrdinalIgnoreCase) || response.Contains("documentos públicos no corpus", StringComparison.OrdinalIgnoreCase))
            failures.Add($"Real ThinkAsync identity response was contaminated by training status: {response}");
        if (stopwatch.ElapsedMilliseconds > 2000)
            failures.Add($"Real ThinkAsync identity response was too slow: {stopwatch.ElapsedMilliseconds} ms");

        Console.WriteLine($"REAL THINK TEST: PASS ({stopwatch.ElapsedMilliseconds} ms)");
    }
    finally
    {
        (brain as IDisposable)?.Dispose();
    }
}
catch (Exception ex)
{
    failures.Add($"Real ThinkAsync test crashed: {ex.GetBaseException().Message}");
}

// Teste de regressão do caminho COMPLETO usado pela interface:
// interface -> AutonomyEngine.ExecuteAsync -> AiBrain.ThinkAsync -> BrainDecision.Response.
// Este teste existe especificamente para impedir que o prompt executivo/memória contamine
// uma pergunta conversacional como "Quem é você?".
try
{
    var brain = Activator.CreateInstance(brainType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, null, null)
        ?? throw new InvalidOperationException("Could not create AiBrain for autonomy test");
    try
    {
        var perceptionType = assembly.GetType("LunaPC.Perception") ?? throw new InvalidOperationException("Perception not found");
        var actionType = assembly.GetType("LunaPC.ActionEngine") ?? throw new InvalidOperationException("ActionEngine not found");
        var memoryType = assembly.GetType("LunaPC.OperationalMemory") ?? throw new InvalidOperationException("OperationalMemory not found");
        var autonomyType = assembly.GetType("LunaPC.AutonomyEngine") ?? throw new InvalidOperationException("AutonomyEngine not found");

        var perception = Activator.CreateInstance(perceptionType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, null, null)
            ?? throw new InvalidOperationException("Could not create Perception");
        var actions = Activator.CreateInstance(actionType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, null, null)
            ?? throw new InvalidOperationException("Could not create ActionEngine");
        var memory = Activator.CreateInstance(memoryType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, null, null)
            ?? throw new InvalidOperationException("Could not create OperationalMemory");
        var autonomy = Activator.CreateInstance(autonomyType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
            new[] { brain, perception, actions, memory, 2 }, null)
            ?? throw new InvalidOperationException("Could not create AutonomyEngine");

        var execute = autonomyType.GetMethod("ExecuteAsync", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMethodException("AutonomyEngine.ExecuteAsync");

        foreach (var input in new[] { "Quem é você?", "Como você funciona?" })
        {
            var stopwatch = Stopwatch.StartNew();
            var task = execute.Invoke(autonomy, new object[] { input, CancellationToken.None }) as Task
                ?? throw new InvalidOperationException("ExecuteAsync did not return a Task");
            task.GetAwaiter().GetResult();
            stopwatch.Stop();

            var result = task.GetType().GetProperty("Result")?.GetValue(task)
                ?? throw new InvalidOperationException("AutonomyResult was null");
            var response = result.GetType().GetProperty("Response")?.GetValue(result)?.ToString() ?? string.Empty;

            if (input.StartsWith("Quem", StringComparison.Ordinal) && !response.Contains("Eu sou a LUNA", StringComparison.Ordinal))
                failures.Add($"END-TO-END identity failed: {response}");
            if (response.Contains("último treinamento", StringComparison.OrdinalIgnoreCase) || response.Contains("documentos públicos no corpus", StringComparison.OrdinalIgnoreCase))
                failures.Add($"END-TO-END response contaminated by training status for '{input}': {response}");
            if (stopwatch.ElapsedMilliseconds > 2000)
                failures.Add($"END-TO-END conversation too slow for '{input}': {stopwatch.ElapsedMilliseconds} ms");

            Console.WriteLine($"END-TO-END TEST [{input}]: PASS ({stopwatch.ElapsedMilliseconds} ms)");
        }
    }
    finally
    {
        (brain as IDisposable)?.Dispose();
    }
}
catch (Exception ex)
{
    failures.Add($"END-TO-END AutonomyEngine test crashed: {ex.GetBaseException().Message}");
}

var temp = Path.Combine(Path.GetTempPath(), "LunaPreflight-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
try
{
    var corpusPath = Path.Combine(temp, "internet-corpus-v1.jsonl");
    File.WriteAllText(corpusPath,
        JsonSerializer.Serialize(new
        {
            Title = "Brasil",
            Text = "Brasil é um país da América do Sul e possui Brasília como sua capital federal, além de grande diversidade geográfica e cultural."
        }) + Environment.NewLine +
        JsonSerializer.Serialize(new
        {
            Title = "Computador",
            Text = "Um computador é uma máquina eletrônica capaz de processar dados, executar programas e armazenar informações de maneira controlada."
        }) + Environment.NewLine);

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
Console.WriteLine("End-to-end AutonomyEngine conversation path, routing, local knowledge indexing/ranking, and interrupted-training recovery passed.");
