namespace LunaPC;

internal static class AndroidIntentRouter
{
    public static void Enrich(string objective, BrainDecision decision)
    {
        if (string.IsNullOrWhiteSpace(objective)) return;
        var t = objective.Trim().ToLowerInvariant();
        var phone = t.Contains("celular") || t.Contains("telefone") || t.Contains("android") || t.Contains("smartphone");
        if (!phone) return;

        if (t.Contains("tela do celular") || t.Contains("tela do telefone") || t.Contains("veja meu celular") || t.Contains("observe o celular"))
        {
            decision.Actions.Clear();
            decision.Actions.Add(new BrainAction { Type = "android_observe", Target = "tela", Risk = "low" });
            decision.Completed = false;
            decision.Intent = "observar_android";
            decision.Plan = new() { "Observar a tela do Android", "Retornar o estado observado ao cérebro" };
            decision.Response = "Vou observar a tela do celular.";
            return;
        }

        if (t.Contains("volte") || t.Contains("voltar") || t.Contains("volte para trás") || t.Contains("volte uma tela"))
        {
            decision.Actions.Clear();
            decision.Actions.Add(new BrainAction { Type = "android_back", Target = "Android", Risk = "low" });
            decision.Completed = false; decision.Intent = "voltar_android"; decision.Plan = new() { "Enviar voltar ao Android", "Verificar a tela retornada" }; decision.Response = "Vou voltar uma tela no celular."; return;
        }

        if (t.Contains("tela inicial") || t.Contains("página inicial") || t.Contains("pagina inicial") || t.Contains("home"))
        {
            decision.Actions.Clear();
            decision.Actions.Add(new BrainAction { Type = "android_home", Target = "Android", Risk = "low" });
            decision.Completed = false; decision.Intent = "home_android"; decision.Plan = new() { "Ir para a tela inicial do Android", "Verificar a tela retornada" }; decision.Response = "Vou para a tela inicial do celular."; return;
        }

        if (t.Contains("aplicativo") || t.Contains("app") || t.Contains("abra ") || t.Contains("abrir ") || t.Contains("inicie ") || t.Contains("iniciar "))
        {
            var target = objective;
            foreach (var marker in new[] { "abra", "abrir", "inicie", "iniciar", "o aplicativo", "o app", "no celular", "no telefone", "no android", "no meu celular", "no meu telefone" })
                target = target.Replace(marker, "", StringComparison.OrdinalIgnoreCase);
            target = target.Trim(' ', '.', ',', '"');
            if (!string.IsNullOrWhiteSpace(target))
            {
                decision.Actions.Clear();
                decision.Actions.Add(new BrainAction { Type = "android_open_app", Target = target, Risk = "low" });
                decision.Completed = false; decision.Intent = "abrir_app_android"; decision.Plan = new() { "Identificar o aplicativo Android", "Abrir o aplicativo", "Observar o estado da tela" }; decision.Response = "Vou abrir " + target + " no celular.";
            }
        }
    }
}
