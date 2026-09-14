using System.Drawing;

namespace LunaPC;

internal sealed record LunaObservation(
    string ActiveWindow,
    string? ScreenshotPath,
    int ScreenWidth,
    int ScreenHeight,
    bool ScreenHasContent);

internal static class LunaObserver
{
    public static LunaObservation Observe()
    {
        var title = WindowsControl.ActiveWindowTitle();
        var width = 0;
        var height = 0;
        var hasContent = false;

        try
        {
            var bounds = System.Windows.Forms.Screen.AllScreens
                .Select(s => s.Bounds)
                .Aggregate(Rectangle.Empty, Rectangle.Union);
            width = bounds.Width;
            height = bounds.Height;
            hasContent = width > 0 && height > 0;
        }
        catch { }

        return new(title, null, width, height, hasContent);
    }

    public static LunaResult Describe()
    {
        var observation = Observe();
        return new(
            $"Observei a tela. Janela ativa: {observation.ActiveWindow}. " +
            $"Área detectada: {observation.ScreenWidth}x{observation.ScreenHeight}. " +
            (observation.ScreenHasContent ? "A tela está disponível para captura e análise." : "Não consegui determinar a área da tela."),
            observation.ScreenHasContent);
    }
}
