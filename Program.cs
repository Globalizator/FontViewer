using System.Runtime.InteropServices;
using System.Text;

namespace FontViewer;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        // Режим «помощника»: повышенный процесс, который только устанавливает шрифт
        if (args.Length >= 3 && args[0] == "--install")
            return FontInstaller.RunHelper(args[1], args[2]);

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        using (var g = Graphics.FromHwnd(IntPtr.Zero)) Ui.Scale = g.DpiX / 96f;

        var settings = AppSettings.Load();
        settings.Save();

        Application.AddMessageFilter(new WheelRedirector());
        Application.Run(new MainForm(settings));
        return 0;
    }
}

/// <summary>Масштаб интерфейса (DPI).</summary>
static class Ui
{
    public static float Scale = 1f;
    public static int S(int v) => (int)Math.Round(v * Scale);
}

/// <summary>Колесо мыши - в элемент под курсором, а не в элемент с фокусом.</summary>
sealed class WheelRedirector : IMessageFilter
{
    const int WM_MOUSEWHEEL = 0x020A;

    public bool PreFilterMessage(ref Message m)
    {
        if (m.Msg != WM_MOUSEWHEEL) return false;
        long l = m.LParam.ToInt64();
        var pt = new Point((short)(l & 0xFFFF), (short)((l >> 16) & 0xFFFF));
        var h = Native.WindowFromPoint(pt);
        if (h == IntPtr.Zero || h == m.HWnd) return false;
        var c = Control.FromHandle(h);
        if (c == null || c.FindForm() != Form.ActiveForm) return false;
        Native.SendMessage(h, m.Msg, m.WParam, m.LParam);
        return true;
    }
}

static class Native
{
    public const uint FR_PRIVATE = 0x10;

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    public static extern int AddFontResourceExW(string name, uint fl, IntPtr res);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    public static extern bool RemoveFontResourceExW(string name, uint fl, IntPtr res);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateFontW(int h, int w, int esc, int orient, int weight, uint italic,
        uint underline, uint strike, uint charset, uint outPrec, uint clipPrec, uint quality, uint pitch, string face);

    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] public static extern int SetBkMode(IntPtr hdc, int mode);
    [DllImport("gdi32.dll")] public static extern uint SetTextColor(IntPtr hdc, uint color);
    [DllImport("gdi32.dll")] public static extern uint SetTextAlign(IntPtr hdc, uint align);
    [DllImport("gdi32.dll")] public static extern bool TextOutA(IntPtr hdc, int x, int y, byte[] s, int c);
    [DllImport("gdi32.dll")] public static extern bool GetTextExtentPoint32A(IntPtr hdc, byte[] s, int c, out Size size);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    public static extern int AddFontResourceW(string name);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
        uint flags, uint timeout, out IntPtr result);

    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point p);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll")] public static extern uint GetOEMCP();
}
