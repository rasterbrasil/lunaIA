using System.Runtime.InteropServices;
using System.Speech.Synthesis;
using Microsoft.Win32;

namespace LunaPC;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var app = new LunaAgentContext();
        Application.Run();
    }
}

internal sealed class LunaAgentContext : ApplicationContext
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 9001;
    private const uint ModControl = 0x0002;
    private const uint ModAlt = 0x0001;
    private const uint VkL = 0x4C;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly NotifyIcon _tray;
    private readonly HotkeyWindow _hotkeyWindow;
    private readonly SpeechSynthesizer _speech;

    public LunaAgentContext()
    {
        _speech = new SpeechSynthesizer();
        _speech.SetOutputToDefaultAudioDevice();
        _speech.Rate = 0;
        _speech.Volume = 100;

        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "LUNA PC — Assistente"
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Falar com a LUNA", null, (_, _) => Speak("Estou aqui, Marcos. A LUNA PC está ativa."));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => ExitThread());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => Speak("Estou aqui, Marcos.");

        _hotkeyWindow = new HotkeyWindow(OnHotkey);
        if (!RegisterHotKey(_hotkeyWindow.Handle, HotkeyId, ModControl | ModAlt, VkL))
            Speak("LUNA PC iniciada. Não consegui registrar o atalho global Ctrl Alt L.");
        else
            Speak("LUNA PC iniciada. Estou aqui, Marcos.");

        EnableStartup();
    }

    private void OnHotkey()
    {
        Speak("Estou aqui, Marcos. A entrada de voz será conectada na próxima etapa.");
    }

    private void Speak(string text)
    {
        try
        {
            _speech.SpeakAsyncCancelAll();
            _speech.SpeakAsync(text);
        }
        catch { }
    }

    private static void EnableStartup()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            var exe = Application.ExecutablePath;
            key?.SetValue("LunaPC", $"\"{exe}\"");
        }
        catch { }
    }

    protected override void ExitThreadCore()
    {
        UnregisterHotKey(_hotkeyWindow.Handle, HotkeyId);
        _hotkeyWindow.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _speech.Dispose();
        base.ExitThreadCore();
    }

    private sealed class HotkeyWindow : NativeWindow
    {
        private readonly Action _callback;
        public HotkeyWindow(Action callback)
        {
            _callback = callback;
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmHotkey && m.WParam.ToInt32() == HotkeyId)
                _callback();
            base.WndProc(ref m);
        }
    }
}
