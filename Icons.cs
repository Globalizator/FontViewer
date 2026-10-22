using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace FontViewer;

/// <summary>Иконки, нарисованные кодом (никаких внешних файлов и ресурсов).</summary>
static class Icons
{
    static Bitmap Make(int size, Action<Graphics> draw)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.ScaleTransform(size / 32f, size / 32f);
        draw(g);
        return bmp;
    }

    /// <summary>Открытая папка.</summary>
    public static Bitmap OpenFolder(int size) => Make(size, g =>
    {
        using var back = new SolidBrush(Color.FromArgb(0xD9, 0x9A, 0x1E));
        g.FillPolygon(back, new PointF[] { new(3, 7), new(12, 7), new(15, 10), new(26, 10), new(26, 24), new(3, 24) });
        var pts = new PointF[] { new(1.5f, 26), new(6.5f, 14.5f), new(30.5f, 14.5f), new(25.5f, 26) };
        using var front = new LinearGradientBrush(new Rectangle(0, 14, 32, 13),
            Color.FromArgb(0xFF, 0xE0, 0x82), Color.FromArgb(0xFF, 0xC1, 0x07), 90f);
        g.FillPolygon(front, pts);
        using var pen = new Pen(Color.FromArgb(0xB2, 0x7A, 0x00), 1.2f);
        g.DrawPolygon(pen, pts);
    });

    /// <summary>Изображение (рамка, солнце, горы).</summary>
    public static Bitmap Picture(int size) => Make(size, g =>
    {
        var rect = new Rectangle(3, 5, 26, 22);
        using var sky = new SolidBrush(Color.FromArgb(0xD6, 0xEA, 0xFB));
        using var sun = new SolidBrush(Color.FromArgb(0xFF, 0xC1, 0x07));
        using var hill = new SolidBrush(Color.FromArgb(0x43, 0xA0, 0x47));
        using var pen = new Pen(Color.FromArgb(0x3F, 0x7F, 0xBF), 1.8f);
        g.FillRectangle(sky, rect);
        g.FillEllipse(sun, 18, 8, 6, 6);
        g.FillPolygon(hill, new PointF[] { new(4, 26), new(11, 15), new(16, 21), new(20, 17), new(28, 26) });
        g.DrawRectangle(pen, rect);
    });

    /// <summary>Шестерёнка.</summary>
    public static Bitmap Gear(int size) => Make(size, g =>
    {
        PointF Pt(double a, double r) => new(16 + (float)(r * Math.Cos(a)), 16 + (float)(r * Math.Sin(a)));
        var pts = new List<PointF>();
        for (int k = 0; k < 8; k++)
        {
            double a = k * Math.PI / 4;
            pts.Add(Pt(a - 0.30, 9.5));
            pts.Add(Pt(a - 0.17, 13.5));
            pts.Add(Pt(a + 0.17, 13.5));
            pts.Add(Pt(a + 0.30, 9.5));
        }
        using var path = new GraphicsPath(FillMode.Alternate);
        path.AddPolygon(pts.ToArray());
        path.AddEllipse(11.5f, 11.5f, 9, 9);
        using var br = new SolidBrush(Color.FromArgb(0x78, 0x90, 0x9C));
        using var pen = new Pen(Color.FromArgb(0x45, 0x5A, 0x64), 1.2f) { LineJoin = LineJoin.Round };
        g.FillPath(br, path);
        g.DrawPath(pen, path);
    });

    /// <summary>Установка: стрелка вниз в лоток.</summary>
    public static Bitmap Install(int size) => Make(size, g =>
    {
        using var tray = new Pen(Color.FromArgb(0x54, 0x6E, 0x7A), 3f)
        { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLines(tray, new PointF[] { new(5, 19), new(5, 27), new(27, 27), new(27, 19) });
        using var br = new SolidBrush(Color.FromArgb(0x2E, 0x9E, 0x4F));
        using var pen = new Pen(Color.FromArgb(0x1B, 0x6E, 0x34), 1.2f) { LineJoin = LineJoin.Round };
        var arrow = new PointF[] { new(13, 3), new(19, 3), new(19, 12), new(24, 12), new(16, 22), new(8, 12), new(13, 12) };
        g.FillPolygon(br, arrow);
        g.DrawPolygon(pen, arrow);
    });

    /// <summary>Информация («i» в круге).</summary>
    public static Bitmap Info(int size) => Make(size, g =>
    {
        using var blue = new SolidBrush(Color.FromArgb(0x2F, 0x80, 0xED));
        using var white = new SolidBrush(Color.White);
        g.FillEllipse(blue, 3, 3, 26, 26);
        g.FillEllipse(white, 14.2f, 7.5f, 3.6f, 3.6f);
        g.FillRectangle(white, 14.2f, 13f, 3.6f, 10f);
    });
}
