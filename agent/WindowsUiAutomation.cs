using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace LunaPC;

/// <summary>
/// Camada de percepção/ação semântica do Windows.
/// Em vez de depender apenas de coordenadas, localiza controles pelo nome,
/// AutomationId e tipo e usa os padrões nativos da UI Automation.
/// </summary>
internal static class WindowsUiAutomation
{
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_RESTORE = 9;

    public static bool TryActivateWindow(string titleOrProcess, out string message)
    {
        var root = AutomationElement.RootElement;
        var windows = root.FindAll(TreeScope.Children, Condition.TrueCondition);
        foreach (AutomationElement window in windows)
        {
            var name = window.Current.Name ?? "";
            var process = window.Current.ProcessId;
            if (name.Contains(titleOrProcess, StringComparison.OrdinalIgnoreCase))
            {
                var handle = new IntPtr(window.Current.NativeWindowHandle);
                if (handle != IntPtr.Zero)
                {
                    ShowWindow(handle, SW_RESTORE);
                    SetForegroundWindow(handle);
                    message = $"Janela '{name}' ativada.";
                    return true;
                }
            }
        }

        message = $"Não encontrei uma janela visível contendo '{titleOrProcess}'.";
        return false;
    }

    public static bool TryInvoke(string windowTitle, string controlName, string? automationId, out string message)
    {
        if (!TryFindControl(windowTitle, controlName, automationId, out var element, out message))
            return false;

        try
        {
            if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var invokeObject))
            {
                ((InvokePattern)invokeObject).Invoke();
                message = $"Acionei '{element.Current.Name}'.";
                return true;
            }

            if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var selectionObject))
            {
                ((SelectionItemPattern)selectionObject).Select();
                message = $"Selecionei '{element.Current.Name}'.";
                return true;
            }

            element.SetFocus();
            message = $"Foquei '{element.Current.Name}'. O controle não expôs um padrão Invoke/SelectionItem.";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Falha ao acionar '{controlName}': {ex.Message}";
            return false;
        }
    }

    public static bool TrySetValue(string windowTitle, string controlName, string value, string? automationId, out string message)
    {
        if (!TryFindControl(windowTitle, controlName, automationId, out var element, out message))
            return false;

        try
        {
            if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out var valueObject))
            {
                element.SetFocus();
                message = $"'{element.Current.Name}' não expôs ValuePattern.";
                return false;
            }

            ((ValuePattern)valueObject).SetValue(value ?? "");
            message = $"Defini o valor de '{element.Current.Name}'.";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Falha ao preencher '{controlName}': {ex.Message}";
            return false;
        }
    }

    private static bool TryFindControl(string windowTitle, string controlName, string? automationId, out AutomationElement element, out string message)
    {
        element = null!;
        if (!TryGetWindow(windowTitle, out var window, out message))
            return false;

        var conditions = new List<Condition>();
        if (!string.IsNullOrWhiteSpace(controlName))
            conditions.Add(new PropertyCondition(AutomationElement.NameProperty, controlName, PropertyConditionFlags.IgnoreCase));
        if (!string.IsNullOrWhiteSpace(automationId))
            conditions.Add(new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));

        var condition = conditions.Count switch
        {
            0 => Condition.TrueCondition,
            1 => conditions[0],
            _ => new AndCondition(conditions.ToArray())
        };

        element = window.FindFirst(TreeScope.Descendants, condition)!;
        if (element is null)
        {
            message = $"Não encontrei o controle '{controlName}' na janela '{windowTitle}'.";
            return false;
        }

        message = "Controle encontrado.";
        return true;
    }

    private static bool TryGetWindow(string title, out AutomationElement window, out string message)
    {
        window = null!;
        var root = AutomationElement.RootElement;
        var windows = root.FindAll(TreeScope.Children, Condition.TrueCondition);
        foreach (AutomationElement candidate in windows)
        {
            if ((candidate.Current.Name ?? "").Contains(title, StringComparison.OrdinalIgnoreCase))
            {
                window = candidate;
                message = "Janela encontrada.";
                return true;
            }
        }

        message = $"Não encontrei a janela '{title}'.";
        return false;
    }
}
