using System.Text;

namespace FontViewer;

/// <summary>Сведения о растровом шрифте (.fon).</summary>
sealed class FonInfo
{
    public string Face = "";
    public byte CharSet;
    public int FirstChar, LastChar;
    public List<int> Heights = new();
}

/// <summary>Файл шрифта в списке. Подробности читаются лениво, только когда элемент виден.</summary>
sealed class FontEntry
{
    public string Path { get; }
    public string FileName { get; }
    public string Ext { get; }
    public bool IsRaster => Ext == ".fon";
    public string DisplayName { get; private set; } = "";
    public FonInfo? Fon { get; private set; }
    public bool LoadFailed;
    volatile bool _infoRead;
    readonly object _lock = new();

    public FontEntry(string path)
    {
        Path = path;
        FileName = System.IO.Path.GetFileName(path);
        Ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
    }

    public static bool IsSupported(string path)
    {
        var e = System.IO.Path.GetExtension(path);
        return e.Equals(".ttf", StringComparison.OrdinalIgnoreCase)
            || e.Equals(".otf", StringComparison.OrdinalIgnoreCase)
            || e.Equals(".fon", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Потокобезопасно: вызывается и из интерфейса, и из фонового потока (поиск/предзагрузка).</summary>
    public void EnsureInfo()
    {
        if (_infoRead) return;
        lock (_lock)
        {
            if (_infoRead) return;
            try
            {
                if (IsRaster)
                {
                    Fon = FonParser.Parse(Path);
                    if (Fon != null) DisplayName = Fon.Face;
                }
                else DisplayName = Sfnt.GetFullName(Path) ?? "";
            }
            catch { }
            if (string.IsNullOrWhiteSpace(DisplayName))
                DisplayName = System.IO.Path.GetFileNameWithoutExtension(Path);
            _infoRead = true;
        }
    }

    public override string ToString() => FileName;
}

/// <summary>Минимальный разбор TrueType/OpenType: таблицы name и cmap.</summary>
static class Sfnt
{
    static int U16(byte[] b, int o) => (b[o] << 8) | b[o + 1];
    static uint U32(byte[] b, int o) => (uint)((b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]);

    public static byte[]? ReadTable(string path, string tag)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.RandomAccess);
        var h = new byte[12];
        fs.ReadExactly(h);
        uint ver = U32(h, 0);
        if (ver != 0x00010000 && ver != 0x4F54544F && ver != 0x74727565) return null; // 1.0 / OTTO / true
        int n = U16(h, 4);
        var rec = new byte[n * 16];
        fs.ReadExactly(rec);
        for (int i = 0; i < n; i++)
        {
            int o = i * 16;
            if (rec[o] == tag[0] && rec[o + 1] == tag[1] && rec[o + 2] == tag[2] && rec[o + 3] == tag[3])
            {
                long off = U32(rec, o + 8), len = U32(rec, o + 12);
                if (len == 0 || len > 32 * 1024 * 1024) return null;
                fs.Seek(off, SeekOrigin.Begin);
                var data = new byte[len];
                fs.ReadExactly(data);
                return data;
            }
        }
        return null;
    }

    // ---------- name ----------
    static Dictionary<int, string> ParseNames(byte[] b)
    {
        var best = new Dictionary<int, (int score, string s)>();
        int count = U16(b, 2), strOff = U16(b, 4);
        for (int i = 0; i < count; i++)
        {
            int o = 6 + i * 12;
            if (o + 12 > b.Length) break;
            int pid = U16(b, o), eid = U16(b, o + 2), lang = U16(b, o + 4);
            int nid = U16(b, o + 6), len = U16(b, o + 8), off = U16(b, o + 10);
            int p = strOff + off;
            if (len == 0 || p + len > b.Length) continue;
            int score; string s;
            if (pid == 3 || pid == 0)
            {
                score = pid == 3 ? (lang == 0x409 ? 4 : 3) : 2;
                s = Encoding.BigEndianUnicode.GetString(b, p, len);
            }
            else if (pid == 1 && eid == 0)
            {
                score = lang == 0 ? 1 : 0;
                s = Encoding.Latin1.GetString(b, p, len);
            }
            else continue;
            if (!best.TryGetValue(nid, out var cur) || cur.score < score) best[nid] = (score, s);
        }
        return best.ToDictionary(k => k.Key, k => k.Value.s);
    }

    public static Dictionary<int, string>? GetNames(string path)
    {
        var t = ReadTable(path, "name");
        return t == null ? null : ParseNames(t);
    }

    public static bool IsCff(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var h = new byte[4];
        fs.ReadExactly(h);
        return U32(h, 0) == 0x4F54544F; // OTTO
    }

    public static string? GetFullName(string path)
    {
        var t = ReadTable(path, "name");
        if (t == null) return null;
        var n = ParseNames(t);
        if (n.TryGetValue(4, out var full) && full.Trim().Length > 0) return full.Trim();
        n.TryGetValue(16, out var fam);
        if (string.IsNullOrWhiteSpace(fam)) n.TryGetValue(1, out fam);
        n.TryGetValue(17, out var sub);
        if (string.IsNullOrWhiteSpace(sub)) n.TryGetValue(2, out sub);
        var r = ((fam ?? "") + " " + (sub ?? "")).Trim();
        return r.Length > 0 ? r : null;
    }

    // ---------- cmap ----------
    static bool Accept(int c) =>
        c >= 0x20 && c <= 0x10FFFF && !(c >= 0x7F && c <= 0x9F) && !(c >= 0xD800 && c <= 0xDFFF) && c != 0xFFFE && c != 0xFFFF;

    /// <summary>Список кодов (Unicode), для которых в шрифте есть глиф.</summary>
    public static int[]? ReadCodes(string path)
    {
        var b = ReadTable(path, "cmap");
        if (b == null || b.Length < 4) return null;
        int n = U16(b, 2), best = -1, bestOff = -1;
        for (int i = 0; i < n; i++)
        {
            int rec = 4 + i * 8;
            if (rec + 8 > b.Length) break;
            int pid = U16(b, rec), eid = U16(b, rec + 2);
            long off = U32(b, rec + 4);
            if (off + 6 > b.Length) continue;
            int fmt = U16(b, (int)off);
            if (fmt != 4 && fmt != 12) continue;
            int sc = (pid, eid) switch
            {
                (3, 10) => 6, (0, 4) => 6, (0, 6) => 6,
                (3, 1) => 5, (0, 3) => 5, (0, _) => 4, (3, 0) => 3,
                _ => -1
            };
            if (sc < 0) continue;
            sc = sc * 2 + (fmt == 12 ? 1 : 0);
            if (sc > best) { best = sc; bestOff = (int)off; }
        }
        if (bestOff < 0) return null;

        var list = new List<int>();
        if (U16(b, bestOff) == 4) ReadFormat4(b, bestOff, list); else ReadFormat12(b, bestOff, list);
        if (list.Count == 0) return null;
        list.Sort();
        return list.Distinct().ToArray();
    }

    static void ReadFormat4(byte[] b, int o, List<int> list)
    {
        int segX2 = U16(b, o + 6), seg = segX2 / 2;
        int endO = o + 14, startO = endO + segX2 + 2, deltaO = startO + segX2, rangeO = deltaO + segX2;
        for (int i = 0; i < seg; i++)
        {
            int end = U16(b, endO + 2 * i), start = U16(b, startO + 2 * i);
            int delta = U16(b, deltaO + 2 * i), ro = U16(b, rangeO + 2 * i);
            for (int c = start; c <= end && c < 0xFFFF; c++)
            {
                int g;
                if (ro == 0) g = (c + delta) & 0xFFFF;
                else
                {
                    int p = rangeO + 2 * i + ro + 2 * (c - start);
                    if (p + 1 >= b.Length) continue;
                    g = U16(b, p);
                    if (g != 0) g = (g + delta) & 0xFFFF;
                }
                if (g != 0 && Accept(c)) list.Add(c);
            }
        }
    }

    static void ReadFormat12(byte[] b, int o, List<int> list)
    {
        int groups = (int)U32(b, o + 12);
        int p = o + 16;
        for (int g = 0; g < groups && p + 12 <= b.Length; g++, p += 12)
        {
            uint s = U32(b, p), e = U32(b, p + 4), sg = U32(b, p + 8);
            if (e > 0x10FFFF) e = 0x10FFFF;
            for (uint c = s; c <= e; c++)
            {
                if (sg + (c - s) == 0) continue;
                if (Accept((int)c)) list.Add((int)c);
                if (list.Count > 120000) return;
            }
        }
    }
}

/// <summary>Разбор .fon (NE и PE контейнеры с ресурсами FNT).</summary>
static class FonParser
{
    static int L16(byte[] b, int o) => b[o] | (b[o + 1] << 8);
    static uint L32(byte[] b, int o) => (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));

    public static FonInfo? Parse(string path)
    {
        var fi = new FileInfo(path);
        if (fi.Length < 64 || fi.Length > 16 * 1024 * 1024) return null;
        var f = File.ReadAllBytes(path);
        var found = new List<FonInfo>();
        try
        {
            if (f[0] == 'M' && f[1] == 'Z')
            {
                int pe = (int)L32(f, 0x3C);
                if (pe > 0 && pe + 0x40 < f.Length)
                {
                    if (f[pe] == 'N' && f[pe + 1] == 'E') ParseNe(f, pe, found);
                    else if (f[pe] == 'P' && f[pe + 1] == 'E') ParsePe(f, pe, found);
                }
            }
            else
            {
                var x = ParseFnt(f, 0); // «голый» FNT
                if (x != null) found.Add(x);
            }
        }
        catch { }
        if (found.Count == 0) return null;
        var r = found[0];
        r.Heights = found.Select(x => x.Heights[0]).Distinct().OrderBy(h => h).ToList();
        return r;
    }

    static void ParseNe(byte[] f, int ne, List<FonInfo> found)
    {
        int rt = ne + L16(f, ne + 0x24);
        int shift = L16(f, rt), p = rt + 2;
        while (p + 2 <= f.Length)
        {
            int type = L16(f, p);
            if (type == 0) break;
            int count = L16(f, p + 2);
            p += 8;
            for (int i = 0; i < count; i++, p += 12)
            {
                if (type != 0x8008) continue; // RT_FONT
                var x = ParseFnt(f, L16(f, p) << shift);
                if (x != null) found.Add(x);
            }
        }
    }

    static void ParsePe(byte[] f, int pe, List<FonInfo> found)
    {
        int nsec = L16(f, pe + 6), optSize = L16(f, pe + 20), opt = pe + 24;
        int magic = L16(f, opt);
        int dd = opt + (magic == 0x20B ? 112 : 96);
        uint rsrcRva = L32(f, dd + 16);
        int secTab = opt + optSize;

        int Rva(uint rva)
        {
            for (int i = 0; i < nsec; i++)
            {
                int s = secTab + i * 40;
                uint vs = L32(f, s + 8), va = L32(f, s + 12), rs = L32(f, s + 16), raw = L32(f, s + 20);
                if (rva >= va && rva < va + Math.Max(vs, rs)) return (int)(rva - va + raw);
            }
            return -1;
        }

        (uint id, uint off)[] Dir(int d)
        {
            int n = L16(f, d + 12) + L16(f, d + 14);
            var r = new (uint, uint)[n];
            for (int i = 0; i < n; i++) r[i] = (L32(f, d + 16 + i * 8), L32(f, d + 20 + i * 8));
            return r;
        }

        int root = Rva(rsrcRva);
        if (root < 0) return;
        foreach (var (id, off) in Dir(root))
        {
            if (id != 8 || (off & 0x80000000) == 0) continue; // RT_FONT
            foreach (var (_, off2) in Dir(root + (int)(off & 0x7FFFFFFF)))
            {
                uint o = off2;
                if ((o & 0x80000000) != 0)
                {
                    var langs = Dir(root + (int)(o & 0x7FFFFFFF));
                    if (langs.Length == 0) continue;
                    o = langs[0].off;
                }
                int de = root + (int)(o & 0x7FFFFFFF);
                int fnt = Rva(L32(f, de));
                if (fnt < 0) continue;
                var x = ParseFnt(f, fnt);
                if (x != null) found.Add(x);
            }
        }
    }

    static FonInfo? ParseFnt(byte[] f, int o)
    {
        if (o < 0 || o + 118 > f.Length) return null;
        int ver = L16(f, o);
        if (ver != 0x200 && ver != 0x300) return null;
        int fo = (int)L32(f, o + 105);
        int p = o + fo;
        if (fo <= 0 || p >= f.Length) return null;
        int e = p;
        while (e < f.Length && f[e] != 0) e++;
        var x = new FonInfo
        {
            Face = Encoding.Latin1.GetString(f, p, e - p),
            CharSet = f[o + 85],
            FirstChar = f[o + 95],
            LastChar = f[o + 96]
        };
        x.Heights.Add(L16(f, o + 88));
        return x;
    }
}
