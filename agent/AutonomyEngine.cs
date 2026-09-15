using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LunaPC;

internal sealed class AutonomyEngine
{
    private readonly AiBrain _brain;
    private readonly Perception _perception;
    private readonly ActionEngine _actions;
    private readonly int _maxCycles;

    public AutonomyEngine(AiBrain brain, Perception perception, ActionEngine actions, int maxCycles = 12)
    {
        _brain = brain;
        _perception = perception;
        _actions = actions;
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
            var prompt = $"""
OBJETIVO DO USUÁRIO:
{objective.Trim()}

CICLO ATUAL: {cycle} de {_maxCycles}

ESTADO OBSERVADO AGORA:
{observation}

Você está operando como agente autônomo. Compare o estado atual com o objetivo e decida o próximo passo.
Se for uma conversa, pergunta ou explicação que não exige ação no computador, responda normalmente com actions=[].
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
                return new(true, decision.Response, trace);
            }

            if (decision.Actions.Count == 0)
            {
                trace.Add($"Ciclo {cycle}: nenhuma ação necessária; resposta entregue.");
                return new(true, decision.Response, trace);
            }

            trace.Add($"Ciclo {cycle}: executando {decision.Actions.Count} ação(ões).");
            var execution = await _actions.ExecuteAsync(decision.Actions, cancellationToken);
            trace.AddRange(execution.Results.Select(r => $"Ciclo {cycle}: {r}"));

            if (execution.Results.Any(r => r.StartsWith("Cancelada pelo usuário:", StringComparison.OrdinalIgnoreCase)))
                return new(false, "Parei porque uma ação precisava de confirmação e ela não foi autorizada.", trace);

            if (execution.Results.Any(r => r.StartsWith("Falhou:", StringComparison.OrdinalIgnoreCase)))
                trace.Add($"Ciclo {cycle}: falha detectada; próximo ciclo fará nova observação e replanejamento.");

            await Task.Delay(150, cancellationToken);
        }

        return new(false, $"Cheguei ao limite de {_maxCycles} ciclos sem confirmar a conclusão do objetivo.", trace);
    }
}

internal sealed record AutonomyResult(bool Completed, string Response, List<string> Trace);
