using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Text;

namespace FontViewer;

/// <summary>
/// Загруженный шрифт. TTF/OTF - через GDI+ (PrivateFontCollection, без установки в систему),
/// FON - через GDI (AddFontResourceEx FR_PRIVATE), т.к. GDI+ растровые шрифты не поддерживает.
/// </summary>
sealed class LoadedFont : IDisposable
{
    public FontEntry Entry { get; }

    PrivateFontCollection? _pfc;
    FontFamily? _family;
    FontStyle _style = FontStyle.Regular;
    readonly Dictionary<int, Font> _fonts = new();
    readonly Dictionary<int, IntPtr> _hfonts = new();
    string _face = "";
    byte _charset;
    Encoding _enc = Encoding.Latin1;
    bool _resAdded;
    int[]? _codes;

    static readonly StringFormat SfLine = MakeSf(false);
    static readonly StringFormat SfWrap = MakeSf(true);

    static StringFormat MakeSf(bool wrap)
    {
        var sf = new StringFormat(StringFormat.GenericTypographic);
        sf.FormatFlags = StringFormatFlags.NoClip | StringFormatFlags.NoFontFallback
                         | (wrap ? 0 : StringFormatFlags.NoWrap);
        return sf;
    }

    LoadedFont(FontEntry e) { Entry = e; }

    public static LoadedFont? TryLoad(FontEntry entry)
    {
        entry.EnsureInfo();
        var f = new LoadedFont(entry);
        try { f.Init(); return f; }
        catch { f.Dispose(); return null; }
    }

    void Init()
    {
        if (Entry.IsRaster)
        {
            var fi = Entry.Fon ?? throw new InvalidDataException("FON");
            if (Native.AddFontResourceExW(Entry.Path, Native.FR_PRIVATE, IntPtr.Zero) == 0)
                throw new InvalidOperationException("AddFontResourceEx");
            _resAdded = true;
            _face = fi.Face;
            _charset = fi.CharSet;
            _enc = GetEncoding(fi.CharSet);
        }
        else
        {
            _pfc = new PrivateFontCollection();
            _pfc.AddFontFile(Entry.Path);
            if (_pfc.Families.Length == 0) throw new InvalidDataException("no families");
            _family = _pfc.Families[0];
            var styles = new[] { FontStyle.Regular, FontStyle.Bold, FontStyle.Italic, FontStyle.Bold | FontStyle.Italic };
            bool ok = false;
            foreach (var st in styles)
                if (_family.IsStyleAvailable(st)) { _style = st; ok = true; break; }
            if (!ok) throw new InvalidDataException("no styles");
        }
    }

    static Encoding GetEncoding(byte cs)
    {
        int cp = cs switch
        {
            204 => 1251, 238 => 1250, 161 => 1253, 162 => 1254, 177 => 1255, 178 => 1256,
            186 => 1257, 163 => 1258, 222 => 874, 128 => 932, 134 => 936, 129 => 949, 136 => 950,
            255 => (int)Native.GetOEMCP(),
            _ => 1252
        };
        try { return Encoding.GetEncoding(cp); } catch { return Encoding.Latin1; }
    }

    /// <summary>Коды символов, имеющихся в шрифте.</summary>
    public int[] GetCodes()
    {
        if (_codes != null) return _codes;
        if (Entry.IsRaster)
        {
            var fi = Entry.Fon!;
            int a = fi.FirstChar, b = fi.LastChar;
            if (b < a) { a = 32; b = 255; }
            _codes = Enumerable.Range(a, b - a + 1).ToArray();
        }
        else
        {
            int[]? c = null;
            try { c = Sfnt.ReadCodes(Entry.Path); } catch { }
            _codes = c ?? Enumerable.Range(0x20, 0xE0).Where(x => x < 0x7F || x > 0x9F).ToArray();
        }
        return _codes;
    }

    public string GlyphText(int code) =>
        Entry.IsRaster ? ((char)code).ToString() : char.ConvertFromUtf32(code);

    Font GetFont(float px)
    {
        int k = (int)(px * 10);
        if (!_fonts.TryGetValue(k, out var f))
            _fonts[k] = f = new Font(_family!, px, _style, GraphicsUnit.Pixel);
        return f;
    }

    IntPtr GetHFont(int px)
    {
        if (!_hfonts.TryGetValue(px, out var h))
            _hfonts[px] = h = Native.CreateFontW(px, 0, 0, 0, 400, 0, 0, 0, _charset,
                6 /*OUT_RASTER_PRECIS*/, 0, 3 /*NONANTIALIASED_QUALITY*/, 0, _face);
        return h;
    }

    public SizeF Measure(Graphics g, string text, float px, float maxWidth = 0, bool raw = false)
    {
        if (string.IsNullOrEmpty(text)) return SizeF.Empty;
        if (Entry.IsRaster)
        {
            var bytes = (raw ? Encoding.Latin1 : _enc).GetBytes(text);
            IntPtr dc = g.GetHdc();
            try
            {
                var old = Native.SelectObject(dc, GetHFont(Math.Max(6, (int)Math.Round(px))));
                Native.GetTextExtentPoint32A(dc, bytes, bytes.Length, out Size sz);
                Native.SelectObject(dc, old);
                return sz;
            }
            finally { g.ReleaseHdc(dc); }
        }
        var f = GetFont(px);
        return maxWidth > 0
            ? g.MeasureString(text, f, new SizeF(maxWidth, 100000), SfWrap)
            : g.MeasureString(text, f, new SizeF(100000, 100000), SfLine);
    }

    public void Draw(Graphics g, string text, float px, Color color, float x, float y, float maxWidth = 0, bool raw = false)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (Entry.IsRaster)
        {
            var bytes = (raw ? Encoding.Latin1 : _enc).GetBytes(text);
            IntPtr dc = g.GetHdc();
            try
            {
                var old = Native.SelectObject(dc, GetHFont(Math.Max(6, (int)Math.Round(px))));
                Native.SetBkMode(dc, 1);
                Native.SetTextColor(dc, (uint)(color.R | (color.G << 8) | (color.B << 16)));
                Native.SetTextAlign(dc, 0);
                Native.TextOutA(dc, (int)Math.Round(x), (int)Math.Round(y), bytes, bytes.Length);
                Native.SelectObject(dc, old);
            }
            finally { g.ReleaseHdc(dc); }
            return;
        }
        using var br = new SolidBrush(color);
        var f = GetFont(px);
        if (maxWidth > 0) g.DrawString(text, f, br, new RectangleF(x, y, maxWidth, 100000), SfWrap);
        else g.DrawString(text, f, br, x, y, SfLine);
    }

    public void Dispose()
    {
        foreach (var f in _fonts.Values) f.Dispose();
        _fonts.Clear();
        foreach (var h in _hfonts.Values) Native.DeleteObject(h);
        _hfonts.Clear();
        _pfc?.Dispose();
        _pfc = null;
        if (_resAdded)
        {
            Native.RemoveFontResourceExW(Entry.Path, Native.FR_PRIVATE, IntPtr.Zero);
            _resAdded = false;
        }
    }
}

/// <summary>Небольшой кэш загруженных шрифтов для видимых элементов списка.</summary>
sealed class FontCache : IDisposable
{
    const int Max = 96;
    readonly Dictionary<FontEntry, LoadedFont> _map = new();
    readonly Queue<FontEntry> _order = new();

    public LoadedFont? Get(FontEntry e)
    {
        if (e.LoadFailed) return null;
        if (_map.TryGetValue(e, out var f)) return f;
        f = LoadedFont.TryLoad(e);
        if (f == null) { e.LoadFailed = true; return null; }
        _map[e] = f;
        _order.Enqueue(e);
        while (_map.Count > Max)
        {
            var old = _order.Dequeue();
            if (_map.Remove(old, out var of)) of.Dispose();
        }
        return f;
    }

    public void Clear()
    {
        foreach (var f in _map.Values) f.Dispose();
        _map.Clear();
        _order.Clear();
    }

    public void Dispose() => Clear();
}

/// <summary>
/// Раскладка и отрисовка «листа» шрифта: название, образец, таблица символов.
/// Один и тот же код рисует и окно просмотра (только видимая часть), и экспорт в PNG.
/// </summary>
sealed class FontSheet : IDisposable
{
    readonly LoadedFont _font;
    readonly int[] _codes;
    readonly string _title, _info, _sample;
    readonly float _k, _samplePx, _glyphPx;
    readonly int _fixedCols, _cellW, _cellH, _codeLine, _glyphH;
    readonly Font _titleFont, _infoFont, _codeFont;
    readonly StringFormat _sfTrim = new() { FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter };
    readonly StringFormat _sfCenter;

    int _titleY, _infoY, _sampleY, _ruleY, _captionY, _gridTop, _rows;
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int Cols { get; private set; }

    int S(int v) => (int)Math.Round(v * _k);

    public int PreferredWidth => 2 * S(12) + (_fixedCols > 0 ? _fixedCols : 16) * _cellW;

    public FontSheet(LoadedFont font, FontEntry entry, string sample, int samplePx, int maxChars, float scale, int fixedCols = 0)
    {
        _font = font;
        _k = scale;
        _fixedCols = fixedCols;
        _sample = sample ?? "";
        _samplePx = Math.Max(6, samplePx) * scale;
        _glyphPx = 24 * scale;
        _cellW = S(48);
        _cellH = S(62);
        _codeLine = S(12);
        _glyphH = _cellH - 2 * _codeLine;

        var all = font.GetCodes();
        _codes = maxChars < all.Length ? all[..maxChars] : all;

        long size = 0;
        try { size = new FileInfo(entry.Path).Length; } catch { }
        string cnt = _codes.Length == all.Length ? all.Length.ToString() : $"{_codes.Length} из {all.Length}";
        _title = entry.DisplayName;
        _info = $"{entry.FileName}  ·  {FormatSize(size)}  ·  символов: {cnt}";
        if (entry.IsRaster && entry.Fon != null)
            _info += "  ·  размеры: " + string.Join(", ", entry.Fon.Heights) + " px";

        _titleFont = new Font("Segoe UI", 20 * scale, FontStyle.Bold, GraphicsUnit.Pixel);
        _infoFont = new Font("Segoe UI", 12 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        _codeFont = new Font("Segoe UI", 10 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        _sfCenter = new StringFormat { Alignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
    }

    static string FormatSize(long b) =>
        b >= 1024 * 1024 ? $"{b / 1048576.0:0.0} МБ" : $"{Math.Max(1, b / 1024)} КБ";

    int ChooseCols(int width)
    {
        if (_fixedCols > 0) return _fixedCols;
        int avail = Math.Max(1, (width - 2 * S(12)) / _cellW);
        int c = 1;
        while (c * 2 <= avail && c < 32) c *= 2;
        return c;
    }

    public void Layout(Graphics g, int width)
    {
        Width = Math.Max(S(100), width);
        int m = S(12);
        Cols = ChooseCols(Width);

        var sz = _font.Measure(g, _sample, _samplePx, Width - 2 * m);
        int sampleH = (int)Math.Ceiling(sz.Height);

        int y = m;
        _titleY = y; y += S(30);
        _infoY = y; y += S(20);
        _sampleY = y + S(6); y = _sampleY + sampleH + S(12);
        _ruleY = y; y += S(10);
        _captionY = y; y += S(22);
        _gridTop = y;
        _rows = (_codes.Length + Cols - 1) / Cols;
        Height = _gridTop + _rows * _cellH + m;
    }

    public void Draw(Graphics g, int oy, Rectangle clip, Color back, Color fore)
    {
        using (var bb = new SolidBrush(back)) g.FillRectangle(bb, clip);
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        int m = S(12);

        using var foreBr = new SolidBrush(fore);
        using var grayBr = new SolidBrush(Color.FromArgb(110, 110, 110));
        using var hexBr = new SolidBrush(Color.FromArgb(30, 90, 170));
        using var rulePen = new Pen(Color.FromArgb(200, 200, 200));
        using var gridPen = new Pen(Color.FromArgb(215, 215, 215));

        g.DrawString(_title, _titleFont, foreBr, new RectangleF(m, _titleY + oy, Width - 2 * m, S(30)), _sfTrim);
        g.DrawString(_info, _infoFont, grayBr, new RectangleF(m, _infoY + oy, Width - 2 * m, S(20)), _sfTrim);
        _font.Draw(g, _sample, _samplePx, fore, m, _sampleY + oy, Width - 2 * m);
        g.DrawLine(rulePen, m, _ruleY + oy, Width - m, _ruleY + oy);
        g.DrawString("Таблица символов  (сверху HEX, снизу DEC)", _infoFont, grayBr,
            new RectangleF(m, _captionY + oy, Width - 2 * m, S(20)), _sfTrim);

        if (_rows == 0) return;
        int r0 = Math.Max(0, (clip.Top - oy - _gridTop) / _cellH);
        int r1 = Math.Min(_rows - 1, (clip.Bottom - oy - _gridTop) / _cellH);

        for (int r = r0; r <= r1; r++)
        {
            for (int c = 0; c < Cols; c++)
            {
                int i = r * Cols + c;
                if (i >= _codes.Length) break;
                int code = _codes[i];
                int x = m + c * _cellW;
                int y = _gridTop + r * _cellH + oy;

                g.DrawRectangle(gridPen, x, y, _cellW - 1, _cellH - 1);

                var st = g.Save();
                try
                {
                    g.SetClip(new Rectangle(x + 1, y + 1, _cellW - 2, _glyphH - 1), CombineMode.Intersect);
                    string t = _font.GlyphText(code);
                    float w = _font.Measure(g, t, _glyphPx, 0, true).Width;
                    _font.Draw(g, t, _glyphPx, fore, x + (_cellW - w) / 2f, y + S(2), 0, true);
                }
                catch { }
                finally { g.Restore(st); }

                string hex = code.ToString(code < 0x100 ? "X2" : "X4");
                g.DrawString(hex, _codeFont, hexBr, new RectangleF(x, y + _glyphH, _cellW, _codeLine), _sfCenter);
                g.DrawString(code.ToString(), _codeFont, grayBr, new RectangleF(x, y + _glyphH + _codeLine, _cellW, _codeLine), _sfCenter);
            }
        }
    }

    public void Dispose()
    {
        _titleFont.Dispose();
        _infoFont.Dispose();
        _codeFont.Dispose();
        _sfTrim.Dispose();
        _sfCenter.Dispose();
    }
}
