using System.Windows.Automation;

namespace LunaPC;

internal sealed record LunaUiElement(string Name, string ControlType, int X, int Y, int Width, int Height);

internal static class LunaSemanticVision
{
    public static IReadOnlyList<LunaUiElement> Inspect(int maxItems = 120)
        => InspectWindow(AutomationElement.RootElement, maxItems);

    public static IReadOnlyList<LunaUiElement> InspectBrowser(int maxItems = 200)
    {
        try
        {
            var handle = WindowsControl.FindBrowserWindowHandle();
            if (handle == IntPtr.Zero) return [];
            WindowsControl.ActivateWindow(handle);
            Thread.Sleep(180);
            return InspectWindow(AutomationElement.FromHandle(handle), maxItems);
        }
        catch { return []; }
    }

    public static LunaResult Describe()
    {
        var elements = Inspect();
        var window = WindowsControl.ActiveWindowTitle();
        if (elements.Count == 0) return new($"Visão semântica local: janela ativa '{window}', mas nenhum elemento acessível foi exposto pelo Windows.");
        var sample = string.Join(" | ", elements.Take(20).Select(e => $"{e.Name} [{e.ControlType}]"));
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
            return ClickElement(element, target);
        }
        catch (Exception ex) { return new($"Não consegui ativar '{target}': {ex.Message}"); }
    }

    public static LunaResult ClickByNameContains(params string[] terms)
        => ClickByNameContainsInBrowser(terms);

    public static LunaResult ClickByNameContainsInBrowser(params string[] terms)
    {
        var normalizedTerms = terms.Where(t => !string.IsNullOrWhiteSpace(t)).Select(Normalize).ToArray();
        if (normalizedTerms.Length == 0) return new("Não recebi termos para localizar o elemento.");
        try
        {
            var handle = WindowsControl.FindBrowserWindowHandle();
            if (handle == IntPtr.Zero) return new("Não encontrei uma janela de navegador para procurar o elemento.");
            if (!WindowsControl.ActivateWindow(handle)) return new("Encontrei o navegador, mas não consegui colocá-lo em primeiro plano.");
            Thread.Sleep(250);

            var browserRoot = AutomationElement.FromHandle(handle);
            foreach (AutomationElement element in browserRoot.FindAll(TreeScope.Descendants, ConditionForVisibleNamedElements))
            {
                var name = element.Current.Name?.Trim();
                if (string.IsNullOrWhiteSpace(name)) continue;
                var normalizedName = Normalize(name);
                if (!normalizedTerms.Any(normalizedName.Contains)) continue;

                var rect = element.Current.BoundingRectangle;
                if (rect.Width <= 0 || rect.Height <= 0) continue;
                var type = element.Current.ControlType?.ProgrammaticName ?? string.Empty;
                if (!type.Contains("Hyperlink", StringComparison.OrdinalIgnoreCase) &&
                    !type.Contains("Button", StringComparison.OrdinalIgnoreCase) &&
                    !type.Contains("ListItem", StringComparison.OrdinalIgnoreCase) &&
                    !type.Contains("Text", StringComparison.OrdinalIgnoreCase))
                    continue;

                return ClickElement(element, name);
            }
        }
        catch (Exception ex) { return new($"Não consegui procurar visualmente o elemento dentro do navegador: {ex.Message}"); }
        return new($"Não encontrei no navegador nenhum elemento correspondente a: {string.Join(", ", terms)}");
    }

    public static LunaResult ClickByNames(params string[] names)
    {
        var handle = WindowsControl.FindBrowserWindowHandle();
        if (handle != IntPtr.Zero) WindowsControl.ActivateWindow(handle);
        foreach (var name in names)
        {
            var result = ClickByNameInBrowser(name);
            if (result.Executed) return result;
        }
        return new($"Não encontrei nenhum dos elementos esperados no navegador: {string.Join(", ", names)}");
    }

    private static LunaResult ClickByNameInBrowser(string name)
    {
        var target = name.Trim();
        if (string.IsNullOrWhiteSpace(target)) return new("Não recebi o nome do elemento para clicar.");
        try
        {
            var handle = WindowsControl.FindBrowserWindowHandle();
            if (handle == IntPtr.Zero || !WindowsControl.ActivateWindow(handle)) return new($"Não consegui ativar o navegador para procurar '{target}'.");
            Thread.Sleep(120);
            var root = AutomationElement.FromHandle(handle);
            var condition = new PropertyCondition(AutomationElement.NameProperty, target);
            var element = root.FindFirst(TreeScope.Descendants, condition);
            return ClickElement(element, target);
        }
        catch (Exception ex) { return new($"Não consegui procurar '{target}' no navegador: {ex.Message}"); }
    }

    private static readonly Condition ConditionForVisibleNamedElements = new AndCondition(
        new PropertyCondition(AutomationElement.IsOffscreenProperty, false),
        new PropertyCondition(AutomationElement.IsEnabledProperty, true));

    private static IReadOnlyList<LunaUiElement> InspectWindow(AutomationElement root, int maxItems)
    {
        var result = new List<LunaUiElement>();
        try
        {
            foreach (AutomationElement element in root.FindAll(TreeScope.Descendants, ConditionForVisibleNamedElements))
            {
                if (result.Count >= maxItems) break;
                var name = element.Current.Name?.Trim();
                if (string.IsNullOrWhiteSpace(name)) continue;
                var rect = element.Current.BoundingRectangle;
                if (rect.Width <= 0 || rect.Height <= 0) continue;
                result.Add(new LunaUiElement(name, element.Current.ControlType?.ProgrammaticName?.Replace("ControlType.", "") ?? "Unknown", (int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height));
            }
        }
        catch { }
        return result;
    }

    private static LunaResult ClickElement(AutomationElement? element, string target)
    {
        if (element is null) return new($"Não encontrei na tela um elemento chamado '{target}'.");
        var rect = element.Current.BoundingRectangle;
        if (rect.Width <= 0 || rect.Height <= 0) return new($"Encontrei '{target}', mas ele não possui uma área visível utilizável.");
        var x = (int)(rect.X + rect.Width / 2);
        var y = (int)(rect.Y + rect.Height / 2);
        var clicked = LunaMouse.MoveAndClick(x, y);
        return clicked.Executed ? new($"Encontrei '{target}', movi o cursor até ele e cliquei.", true) : clicked;
    }

    private static string Normalize(string value)
    {
        var form = value.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var chars = form.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark);
        return new string(chars.ToArray()).Normalize(System.Text.NormalizationForm.FormC);
    }
}
