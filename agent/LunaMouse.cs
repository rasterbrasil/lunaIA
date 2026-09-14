using System.Runtime.InteropServices;

namespace LunaPC;

internal static class LunaMouse
{
    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);

    private const uint LeftDown = 0x0002;
    private const uint LeftUp = 0x0004;

    public static LunaResult MoveAndClick(int x, int y)
    {
        try
        {
            if (!SetCursorPos(x, y)) return new("Não consegui mover o mouse para o ponto encontrado.");
            mouse_event(LeftDown | LeftUp, (uint)x, (uint)y, 0, UIntPtr.Zero);
            return new($"Cliquei no ponto ({x}, {y}).", true);
        }
        catch (Exception ex)
        {
            return new($"Não consegui clicar com o mouse: {ex.Message}");
        }
    }
}
