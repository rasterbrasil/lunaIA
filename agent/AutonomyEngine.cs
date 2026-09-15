using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LunaPC;

internal sealed class AutonomyEngine
{
    private readonly AiBrain _brain; private readonly Perception _perception; private readonly ActionEngine _actions; private readonly OperationalMemory _memory; private readonly int _maxCycles;
    public AutonomyEngine(AiBrain brain,Perception perception,ActionEngine actions,OperationalMemory? memory=null,int maxCycles=12){_brain=brain;_perception=perception;_actions=actions;_memory=memory??new OperationalMemory();_maxCycles=Math.Clamp(maxCycles,1,30);}

    public async Task<AutonomyResult> ExecuteAsync(string objective,CancellationToken cancellationToken=default)
    {
        if(string.IsNullOrWhiteSpace(objective))return new(false,"Objetivo vazio.",new List<string>());
        var trace=new List<string>{"Memória operacional consultada."}; _=_perception.Capture(); trace.Add("Observação inicial concluída.");
        for(var cycle=1;cycle<=_maxCycles;cycle++)
        {
            cancellationToken.ThrowIfCancellationRequested(); var observation=_perception.CaptureForBrain(); var learned=_memory.ForBrain(objective,20,10);
            var directRequest=IsDirectLanguageRequest(objective);
            var brainInput=directRequest?objective.Trim():$"""
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
            var decision=await _brain.ThinkAsync(brainInput,cancellationToken);
            if(decision is null)return new(false,"Não consegui tomar uma decisão no ciclo autônomo.",trace);
            AndroidIntentRouter.Enrich(objective,decision);
            if(decision.NeedsClarification)return new(false,decision.ClarificationQuestion,trace);
            if(decision.Completed){trace.Add($"Ciclo {cycle}: conclusão confirmada pelo cérebro.");_memory.RecordEpisode(objective,"verificar estado atual","Objetivo confirmado como concluído.",decision.Response,true,.95,new[]{"conclusao",decision.Intent});return new(true,decision.Response,trace);}
            if(decision.Actions.Count==0){trace.Add($"Ciclo {cycle}: nenhuma ação necessária; resposta entregue.");return new(true,decision.Response,trace);}
            var actionSummary=JsonSerializer.Serialize(decision.Actions.Select(a=>new{a.Type,a.Target,a.Value,a.Path,a.Destination,a.Url,a.Arguments,a.X,a.Y,a.Risk})); trace.Add($"Ciclo {cycle}: executando {decision.Actions.Count} ação(ões).");
            var execution=await _actions.ExecuteAsync(decision.Actions,cancellationToken); trace.AddRange(execution.Results.Select(r=>$"Ciclo {cycle}: {r}"));
            var cancelled=execution.Results.Any(r=>r.StartsWith("Cancelada pelo usuário:",StringComparison.OrdinalIgnoreCase)); var failed=execution.Results.Any(r=>r.StartsWith("Falhou:",StringComparison.OrdinalIgnoreCase)); var resultText=string.Join(" | ",execution.Results); var solutionText=string.Join(" → ",decision.Plan.DefaultIfEmpty(decision.Response)); var success=!cancelled&&!failed;
            _memory.RecordEpisode(objective,actionSummary,resultText,solutionText,success,success?.90:.82,new[]{decision.Intent,success?"funcionou":"falhou"});
            if(cancelled)return new(false,"Parei porque uma ação precisava de confirmação e ela não foi autorizada.",trace);
            if(failed)trace.Add($"Ciclo {cycle}: falha detectada; próximo ciclo fará nova observação, consultará o erro aprendido e replanejará.");
            await Task.Delay(150,cancellationToken);
        }
        return new(false,$"Cheguei ao limite de {_maxCycles} ciclos sem confirmar a conclusão do objetivo.",trace);
    }

    private static bool IsDirectLanguageRequest(string objective)
    {
        var text=objective.Trim().ToLowerInvariant(); if(text.Length==0)return false;
        var actionMarkers=new[]{"abra ","abrir ","acesse ","acessar ","clique","clicar","digite ","digitar ","escreva ","escrever ","mova ","mover ","crie ","criar ","copie ","copiar ","delete ","deletar ","apague ","apagar ","execute ","executar ","rode ","rodar ","instale ","instalar ","baixe ","baixar ","envie ","enviar ","salve ","salvar ","renomeie ","renomear ","feche ","fechar ","bloqueie ","desbloqueie "};
        if(actionMarkers.Any(text.Contains))return false;
        if(text is "quem é você?" or "quem é você" or "quem e você?" or "quem e você" or "o que você é?" or "o que você é" or "o que voce e?" or "o que voce e" or "como você funciona?" or "como você funciona" or "como voce funciona?" or "como voce funciona" or "obrigado" or "obrigada" or "teste" or "testando")return true;
        if(text.Contains("o que você sabe")||text.Contains("o que voce sabe")||text.Contains("o que sabe sobre")||text.Contains("o que você conhece")||text.Contains("o que voce conhece")||text.Contains("fale sobre")||text.Contains("explique ")||text.Contains("defina ")||text.Contains("quem foi ")||text.Contains("onde fica ")||text.Contains("por que ")||text.Contains("porque ")||text.Contains("como funciona ")||text.Contains("o que é ")||text.Contains("o que e ")||text.StartsWith("pesquise ")||text.StartsWith("pesquisa ")||text.StartsWith("procure ")||text.StartsWith("busque "))return true;
        if(text.Contains("o que aprendeu")||text.Contains("o que voce aprendeu")||text.Contains("o que você aprendeu")||text.Contains("quanto aprendeu")||text.Contains("status do treinamento")||text.Contains("como esta o treinamento")||text.Contains("como está o treinamento")||text.Contains("terminou o treinamento")||text.Contains("treinamento terminou"))return true;
        if((text.Contains("treine")||text.Contains("treinar")||text.Contains("treinamento"))&&(text.Contains("internet")||text.Contains("web")||text.Contains("online")||text.Contains("wikipedia")))return true;
        return false;
    }
}

internal sealed record AutonomyResult(bool Completed,string Response,List<string> Trace);