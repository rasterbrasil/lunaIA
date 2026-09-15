using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LunaPC;

internal sealed class AutonomyEngine
{
    private readonly AiBrain _brain;
    private readonly Perception _perception;
    private readonly ActionEngine _actions;
    private readonly OperationalMemory _memory;
    private readonly int _maxCycles;

    public AutonomyEngine(AiBrain brain, Perception perception, ActionEngine actions, OperationalMemory? memory = null, int maxCycles = 12)
    {
        _brain = brain;
        _perception = perception;
        _actions = actions;
        _memory = memory ?? new OperationalMemory();
        _maxCycles = Math.Clamp(maxCycles, 1, 30);
    }

    public async Task<AutonomyResult> ExecuteAsync(string objective, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(objective))
            return new(false, "Objetivo vazio.", new List<string>());

        var trace = new List<string>();
        trace.Add("Memória operacional consultada.");
        _ = _perception.Capture();
        trace.Add("Observação inicial concluída.");

        for (var cycle = 1; cycle <= _maxCycles; cycle++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var observation = _perception.CaptureForBrain();
            var learned = _memory.ForBrain(objective, maxItems: 20, maxEpisodes: 10);
            var prompt = $"""
OBJETIVO DO USUÁRIO:
{objective.Trim()}

CICLO ATUAL: {cycle} de {_maxCycles}

ESTADO OBSERVADO AGORA:
{observation}

MEMÓRIA OPERACIONAL RELEVANTE:
{learned}

Você está operando como agente autônomo. Compare o estado atual com o objetivo e decida o próximo passo.
Use experiências anteriores quando forem realmente relevantes, mas nunca trate uma experiência antiga como prova do estado atual.
Se uma solução anterior falhou, não repita cegamente a mesma abordagem; adapte ou escolha outra.
Se for conversa, pergunta ou explicação sem ação no computador, responda normalmente com actions=[].
Se o objetivo já foi alcançado e isso estiver confirmado pelo estado observado, marque completed=true e actions=[].
Se ainda não foi alcançado, gere SOMENTE as ações necessárias para o próximo passo.
Depois da execução, o sistema observará novamente o computador e você decidirá o próximo passo.
Nunca diga que concluiu algo que não foi verificado.
""";

            var decision = await _brain.ThinkAsync(prompt, cancellationToken);
            if (decision is null)
                return new(false, "Não consegui tomar uma decisão no ciclo autônomo.", trace);

            if (decision.NeedsClarification)
                return new(false, decision.ClarificationQuestion, trace);

            if (decision.Completed)
            {
                trace.Add($"Ciclo {cycle}: conclusão confirmada pelo cérebro.");
                _memory.RecordEpisode(
                    objective,
                    "verificar estado atual",
                    "Objetivo confirmado como concluído.",
                    decision.Response,
                    true,
                    0.95,
                    new[] { "conclusao", decision.Intent });
                return new(true, decision.Response, trace);
            }

            if (decision.Actions.Count == 0)
            {
                trace.Add($"Ciclo {cycle}: nenhuma ação necessária; resposta entregue.");
                return new(true, decision.Response, trace);
            }

            var actionSummary = JsonSerializer.Serialize(decision.Actions.Select(a => new
            {
                a.Type, a.Target, a.Value, a.Path, a.Destination, a.Url, a.Arguments, a.X, a.Y, a.Risk
            }));

            trace.Add($"Ciclo {cycle}: executando {decision.Actions.Count} ação(ões).");
            var execution = await _actions.ExecuteAsync(decision.Actions, cancellationToken);
            trace.AddRange(execution.Results.Select(r => $"Ciclo {cycle}: {r}"));

            var cancelled = execution.Results.Any(r => r.StartsWith("Cancelada pelo usuário:", StringComparison.OrdinalIgnoreCase));
            var failed = execution.Results.Any(r => r.StartsWith("Falhou:", StringComparison.OrdinalIgnoreCase));
            var resultText = string.Join(" | ", execution.Results);
            var solutionText = string.Join(" → ", decision.Plan.DefaultIfEmpty(decision.Response));
            var success = !cancelled && !failed;

            _memory.RecordEpisode(
                objective,
                actionSummary,
                resultText,
                solutionText,
                success,
                success ? 0.90 : 0.82,
                new[] { decision.Intent, success ? "funcionou" : "falhou" });

            if (cancelled)
                return new(false, "Parei porque uma ação precisava de confirmação e ela não foi autorizada.", trace);

            if (failed)
                trace.Add($"Ciclo {cycle}: falha detectada; próximo ciclo fará nova observação, consultará o erro aprendido e replanejará.");

            await Task.Delay(150, cancellationToken);
        }

        return new(false, $"Cheguei ao limite de {_maxCycles} ciclos sem confirmar a conclusão do objetivo.", trace);
    }
}

internal sealed record AutonomyResult(bool Completed, string Response, List<string> Trace);
