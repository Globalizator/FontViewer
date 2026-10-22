namespace FontViewer;

sealed class FontPropertiesForm : Form
{
    public FontPropertiesForm(FontEntry e)
    {
        e.EnsureInfo();
        AutoScaleMode = AutoScaleMode.None;
        Text = "Свойства шрифта";
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        ClientSize = new Size(Ui.S(600), Ui.S(520));
        MinimumSize = new Size(Ui.S(420), Ui.S(360));

        var tl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(Ui.S(10))
        };
        tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tl.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tl.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.S(28)));
        tl.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tl.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.S(50)));
        tl.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tl.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        tl.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.S(42)));

        Label Cap(string t) => new() { Text = t, AutoSize = true, Margin = new Padding(0, Ui.S(6), 0, Ui.S(2)) };
        TextBox Box(string text, bool multi) => new()
        {
            Text = text, ReadOnly = true, Multiline = multi, Dock = DockStyle.Fill,
            BackColor = SystemColors.Window, WordWrap = true
        };

        var tbName = Box(e.DisplayName, false);
        tbName.Font = new Font(Font, FontStyle.Bold);
        var tbPath = Box(e.Path, true);
        var tbProps = Box(string.Join("\r\n", Build(e).Select(p => p.Item1 + ": " + p.Item2.Replace("\r", " ").Replace("\n", " "))), true);
        tbProps.ScrollBars = ScrollBars.Vertical;

        var close = new Button { Text = "Закрыть", Size = new Size(Ui.S(90), Ui.S(28)), Anchor = AnchorStyles.Right, DialogResult = DialogResult.Cancel };

        tl.Controls.Add(Cap("Название"), 0, 0);
        tl.Controls.Add(tbName, 0, 1);
        tl.Controls.Add(Cap("Полный путь"), 0, 2);
        tl.Controls.Add(tbPath, 0, 3);
        tl.Controls.Add(Cap("Свойства"), 0, 4);
        tl.Controls.Add(tbProps, 0, 5);
        tl.Controls.Add(close, 0, 6);
        Controls.Add(tl);
        CancelButton = close;
        AcceptButton = close;
        Shown += (_, _) => close.Focus();
    }

    static string CharsetName(int cs) => cs switch
    {
        0 => "ANSI", 1 => "DEFAULT", 2 => "SYMBOL", 128 => "Японская (Shift-JIS)", 129 => "Корейская",
        134 => "Китайская упрощённая", 136 => "Китайская традиционная", 161 => "Греческая", 162 => "Турецкая",
        163 => "Вьетнамская", 177 => "Иврит", 178 => "Арабская", 186 => "Балтийская", 204 => "Кириллица",
        222 => "Тайская", 238 => "Центральноевропейская", 255 => "OEM", _ => "Код " + cs
    };

    static string FormatSize(long b) =>
        b >= 1024 * 1024 ? $"{b / 1048576.0:0.0} МБ" : $"{Math.Max(1, b / 1024)} КБ";

    static List<(string, string)> Build(FontEntry e)
    {
        var l = new List<(string, string)>();
        if (e.IsRaster)
        {
            l.Add(("Тип", "Растровый шрифт Windows (FON)"));
            if (e.Fon != null)
            {
                l.Add(("Гарнитура", e.Fon.Face));
                l.Add(("Кодировка", CharsetName(e.Fon.CharSet)));
                l.Add(("Диапазон символов", $"{e.Fon.FirstChar}–{e.Fon.LastChar}"));
                l.Add(("Размеры, px", string.Join(", ", e.Fon.Heights)));
            }
        }
        else
        {
            try { l.Add(("Тип", Sfnt.IsCff(e.Path) ? "OpenType (контуры CFF)" : "TrueType / OpenType (контуры TrueType)")); } catch { }
            Dictionary<int, string> n = new();
            try { n = Sfnt.GetNames(e.Path) ?? n; } catch { }
            void N(string title, params int[] ids)
            {
                foreach (var id in ids)
                    if (n.TryGetValue(id, out var v) && !string.IsNullOrWhiteSpace(v)) { l.Add((title, v.Trim())); return; }
            }
            N("Семейство", 16, 1);
            N("Начертание", 17, 2);
            N("Полное имя", 4);
            N("PostScript-имя", 6);
            N("Версия", 5);
            N("Авторские права", 0);
            N("Товарный знак", 7);
            N("Производитель", 8);
            N("Разработчик", 9);
            N("Описание", 10);
            N("Лицензия", 13);
            N("URL лицензии", 14);
            N("URL производителя", 11);
            N("URL разработчика", 12);
            try { l.Add(("Символов в шрифте", Sfnt.ReadCodes(e.Path)?.Length.ToString() ?? "—")); } catch { }
        }

        try
        {
            var fi = new FileInfo(e.Path);
            l.Add(("Размер файла", $"{fi.Length:N0} байт ({FormatSize(fi.Length)})"));
            l.Add(("Создан", fi.CreationTime.ToString("g")));
            l.Add(("Изменён", fi.LastWriteTime.ToString("g")));
            string dir = fi.DirectoryName ?? "";
            bool inSys = string.Equals(dir.TrimEnd('\\'), AppSettings.OsFontsFolder().TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            l.Add(("Расположение", inSys ? "Системная папка шрифтов" : "Не в системной папке шрифтов"));
        }
        catch { }
        l.Add(("Установлен в системе", FontInstaller.IsInstalled(e.Path) ? "да" : "нет"));
        return l;
    }
}
