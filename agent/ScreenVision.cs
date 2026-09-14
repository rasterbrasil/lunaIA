using System.Drawing;
using System.Security.Cryptography;

namespace LunaPC;

internal sealed record ScreenSnapshot(string Path, int Width, int Height, string Fingerprint, bool HasContent);

internal static class ScreenVision
{
    public static LunaResult Capture() =>
        TryCapture(savePermanent: true, out var snapshot)
            ? new($"Capturei a tela localmente. A imagem foi salva em {snapshot.Path}.", true)
            : new("Não consegui capturar a tela.");

    public static ScreenSnapshot? ObserveSnapshot()
    {
        return TryCapture(savePermanent: false, out var snapshot) ? snapshot : null;
    }

    private static bool TryCapture(bool savePermanent, out ScreenSnapshot snapshot)
    {
        snapshot = new("", 0, 0, "", false);
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LunaPC", "vision");
            Directory.CreateDirectory(dir);

            var bounds = System.Windows.Forms.Screen.AllScreens
                .Select(s => s.Bounds)
                .Aggregate(Rectangle.Empty, Rectangle.Union);
            if (bounds.Width <= 0 || bounds.Height <= 0) return false;

            var file = savePermanent
                ? Path.Combine(dir, $"screen-{DateTime.Now:yyyyMMdd-HHmmssfff}.png")
                : Path.Combine(Path.GetTempPath(), $"luna-observe-{Guid.NewGuid():N}.png");

            using (var bitmap = new Bitmap(bounds.Width, bounds.Height))
            {
                using var graphics = Graphics.FromImage(bitmap);
                graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
                bitmap.Save(file, System.Drawing.Imaging.ImageFormat.Png);
            }

            using var stream = File.OpenRead(file);
            var hash = Convert.ToHexString(SHA256.HashData(stream));
            var info = new FileInfo(file);
            snapshot = new(file, bounds.Width, bounds.Height, hash, info.Length > 0);

            if (!savePermanent)
            {
                try { File.Delete(file); } catch { }
            }
            return true;
        }
        catch { return false; }
    }
}
