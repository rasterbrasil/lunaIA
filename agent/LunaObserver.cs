using System.Drawing;

namespace LunaPC;

internal sealed record LunaObservation(
    string ActiveWindow,
    string? ScreenshotPath,
    int ScreenWidth,
    int ScreenHeight,
    bool ScreenHasContent,
    string ScreenFingerprint,
    DateTime Timestamp);

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

        var snapshot = ScreenVision.ObserveSnapshot();
        return new(title, snapshot?.Path, snapshot?.Width ?? width, snapshot?.Height ?? height,
            snapshot?.HasContent ?? hasContent, snapshot?.Fingerprint ?? string.Empty, DateTime.Now);
    }

    public static LunaResult Describe()
    {
        var observation = Observe();
        var fingerprint = string.IsNullOrEmpty(observation.ScreenFingerprint)
            ? "não disponível"
            : observation.ScreenFingerprint[..Math.Min(12, observation.ScreenFingerprint.Length)];

        return new(
            $"Observei o estado local da tela. Janela ativa: {observation.ActiveWindow}. " +
            $"Área detectada: {observation.ScreenWidth}x{observation.ScreenHeight}. " +
            (observation.ScreenHasContent
                ? $"A tela está disponível para análise. Impressão visual local: {fingerprint}."
                : "Não consegui determinar a área da tela."),
            observation.ScreenHasContent);
    }
}
