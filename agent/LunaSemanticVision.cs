using System.Windows.Automation;

namespace LunaPC;

internal sealed record LunaUiElement(string Name, string ControlType, int X, int Y, int Width, int Height);

internal static class LunaSemanticVision
{
    public static IReadOnlyList<LunaUiElement> Inspect(int maxItems = 80)
    {
        var result = new List<LunaUiElement>();
        try
        {
            var root = AutomationElement.RootElement;
            var condition = new PropertyCondition(AutomationElement.IsOffscreenProperty, false);
            foreach (AutomationElement element in root.FindAll(TreeScope.Descendants, condition))
            {
                if (result.Count >= maxItems) break;
                var name = element.Current.Name?.Trim();
                if (string.IsNullOrWhiteSpace(name)) continue;
                var rect = element.Current.BoundingRectangle;
                if (rect.Width <= 0 || rect.Height <= 0) continue;
                result.Add(new LunaUiElement(
                    name,
                    element.Current.ControlType?.ProgrammaticName?.Replace("ControlType.", "") ?? "Unknown",
                    (int)rect.X,
                    (int)rect.Y,
                    (int)rect.Width,
                    (int)rect.Height));
            }
        }
        catch { }
        return result;
    }

    public static LunaResult Describe()
    {
        var elements = Inspect();
        var window = WindowsControl.ActiveWindowTitle();
        if (elements.Count == 0)
            return new($"Visão semântica local: janela ativa '{window}', mas nenhum elemento acessível foi exposto pelo Windows.");

        var sample = string.Join(" | ", elements.Take(20).Select(e => $"{e.Name} [{e.ControlType}]") );
        return new($"Visão semântica local: janela '{window}'. Elementos encontrados: {elements.Count}. Amostra: {sample}");
    }

    public static LunaResult ClickByName(string name)
    {
        var target = name.Trim();
        if (string.IsNullOrWhiteSpace(target)) return new("Não recebi o nome do elemento para clicar.");

        try
        {
            var root = AutomationElement.RootElement;
            var condition = new PropertyCondition(AutomationElement.NameProperty, target);
            var element = root.FindFirst(TreeScope.Descendants, condition);
            if (element is null) return new($"Não encontrei na tela um elemento chamado '{target}'.");

            if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var invokeObject) && invokeObject is InvokePattern invoke)
            {
                invoke.Invoke();
                return new($"Ativei o elemento '{target}' pela interface de acessibilidade.", true);
            }

            var rect = element.Current.BoundingRectangle;
            if (rect.Width > 0 && rect.Height > 0)
                return LunaMouse.MoveAndClick((int)(rect.X + rect.Width / 2), (int)(rect.Y + rect.Height / 2));

            return new($"Encontrei '{target}', mas ele não possui uma área clicável utilizável.");
        }
        catch (Exception ex)
        {
            return new($"Não consegui ativar '{target}': {ex.Message}");
        }
    }
}
