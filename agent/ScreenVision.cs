using System.Drawing;

namespace LunaPC;

internal static class ScreenVision
{
    public static LunaResult Capture()
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LunaPC", "vision");
            Directory.CreateDirectory(dir);

            var bounds = System.Windows.Forms.Screen.AllScreens
                .Select(s => s.Bounds)
                .Aggregate(Rectangle.Empty, Rectangle.Union);

            if (bounds.Width <= 0 || bounds.Height <= 0)
                return new("Não consegui localizar a área da tela.");

            var file = Path.Combine(dir, $"screen-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            using var bitmap = new Bitmap(bounds.Width, bounds.Height);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
            }
            bitmap.Save(file, System.Drawing.Imaging.ImageFormat.Png);

            return new($"Capturei a tela localmente. A imagem foi salva em {file}. Ainda não interpreto visualmente o conteúdo; esta é a base para a próxima camada de visão.", true);
        }
        catch (Exception ex)
        {
            return new($"Não consegui capturar a tela: {ex.Message}");
        }
    }
}
