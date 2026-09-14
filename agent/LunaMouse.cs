using System.Runtime.InteropServices;

namespace LunaPC;

internal static class LunaMouse
{
    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    private const uint LeftDown = 0x0002;
    private const uint LeftUp = 0x0004;

    private readonly record struct Point(int X, int Y);

    public static LunaResult MoveTo(int x, int y, int durationMs = 650)
    {
        try
        {
            if (!GetCursorPos(out var start))
                return new("Não consegui ler a posição atual do mouse.");

            var steps = Math.Max(12, durationMs / 16);
            for (var i = 1; i <= steps; i++)
            {
                var progress = (double)i / steps;
                var eased = 1 - Math.Pow(1 - progress, 3);
                var nextX = (int)Math.Round(start.X + (x - start.X) * eased);
                var nextY = (int)Math.Round(start.Y + (y - start.Y) * eased);
                if (!SetCursorPos(nextX, nextY))
                    return new("Não consegui mover o mouse até o ponto encontrado.");
                Thread.Sleep(Math.Max(1, durationMs / steps));
            }

            Thread.Sleep(120);
            return new($"Mova o cursor até ({x}, {y}).", true);
        }
        catch (Exception ex)
        {
            return new($"Não consegui mover o mouse: {ex.Message}");
        }
    }

    public static LunaResult MoveAndClick(int x, int y, int moveDurationMs = 650)
    {
        var moved = MoveTo(x, y, moveDurationMs);
        if (!moved.Executed) return moved;

        try
        {
            mouse_event(LeftDown, (uint)x, (uint)y, 0, UIntPtr.Zero);
            Thread.Sleep(90);
            mouse_event(LeftUp, (uint)x, (uint)y, 0, UIntPtr.Zero);
            Thread.Sleep(180);
            return new($"Cliquei no ponto ({x}, {y}) após mover o cursor até ele.", true);
        }
        catch (Exception ex)
        {
            return new($"Mudei o cursor, mas não consegui clicar: {ex.Message}");
        }
    }
}
