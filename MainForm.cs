using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace FontViewer;

sealed class FontListBox : ListBox
{
    public FontListBox()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        IntegralHeight = false;
        HorizontalScrollbar = false;
        BorderStyle = BorderStyle.None;
        DoubleBuffered = true;
    }
}

/// <summary>Прокручиваемая панель: рисует только видимую часть листа шрифта.</summary>
sealed class PreviewPanel : Panel
{
    FontSheet? _sheet;
    string? _message;
    bool _busy;

    public PreviewPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        AutoScroll = true;
        BackColor = Color.White;
    }

    public void SetSheet(FontSheet? sheet, string? message)
    {
        _sheet = sheet;
        _message = message;
        AutoScrollPosition = new Point(0, 0);
        Relayout();
        Invalidate();
    }

    void Relayout()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            if (_sheet == null) { AutoScrollMinSize = Size.Empty; return; }
            int w = ClientSize.Width - (VScroll ? 0 : SystemInformation.VerticalScrollBarWidth);
            using var g = CreateGraphics();
            _sheet.Layout(g, w);
            AutoScrollMinSize = new Size(0, _sheet.Height);
        }
        finally { _busy = false; }
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        if (_sheet != null) { Relayout(); Invalidate(); }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (_sheet == null) return;
        int step = Ui.S(60) * Math.Sign(e.Delta) * Math.Max(1, Math.Abs(e.Delta) / 120);
        AutoScrollPosition = new Point(0, Math.Max(0, -AutoScrollPosition.Y - step));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        if (_sheet == null)
        {
            g.Clear(BackColor);
            TextRenderer.DrawText(g, _message ?? "Выберите шрифт в списке слева", Font, ClientRectangle,
                Color.Gray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }
        _sheet.Draw(g, AutoScrollPosition.Y, e.ClipRectangle, Color.White, Color.Black);
    }
}

/// <summary>Поле поиска с крестиком очистки.</summary>
sealed class SearchBox : Panel
{
    readonly TextBox _tb = new() { BorderStyle = BorderStyle.None, PlaceholderText = "Поиск: название или файл…" };
    readonly Label _clear = new()
    {
        Text = "×", AutoSize = false, TextAlign = ContentAlignment.MiddleCenter,
        Cursor = Cursors.Hand, ForeColor = Color.Gray, Visible = false
    };
    public event EventHandler? QueryChanged;
    public string Query => _tb.Text;

    public SearchBox()
    {
        BorderStyle = BorderStyle.FixedSingle;
        BackColor = SystemColors.Window;
        _clear.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
        Controls.Add(_tb);
        Controls.Add(_clear);
        _tb.TextChanged += (_, _) =>
        {
            _clear.Visible = _tb.TextLength > 0;
            QueryChanged?.Invoke(this, EventArgs.Empty);
        };
        _clear.Click += (_, _) => { _tb.Clear(); _tb.Focus(); };
        _clear.MouseEnter += (_, _) => _clear.ForeColor = Color.Black;
        _clear.MouseLeave += (_, _) => _clear.ForeColor = Color.Gray;
        _tb.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape && _tb.TextLength > 0)
            {
                _tb.Clear();
                e.Handled = e.SuppressKeyPress = true;
            }
        };
    }

    public void FocusInput() { _tb.Focus(); _tb.SelectAll(); }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int cw = Ui.S(22);
        _clear.SetBounds(ClientSize.Width - cw, 0, cw, ClientSize.Height);
        _tb.SetBounds(Ui.S(5), Math.Max(0, (ClientSize.Height - _tb.Height) / 2),
            Math.Max(10, ClientSize.Width - cw - Ui.S(7)), _tb.Height);
    }
}

sealed class MainForm : Form
{
    readonly AppSettings _settings;
    readonly ToolStripButton _btnFolder, _btnExport, _btnInstall, _btnSettings;
    readonly SearchBox _search;
    readonly ContextMenuStrip _menu;
    readonly System.Windows.Forms.Timer _filterTimer = new() { Interval = 150 };
    readonly ToolStripStatusLabel _lblStatus;
    readonly ToolStripProgressBar _progress;
    readonly SplitContainer _split;
    readonly FontListBox _list;
    readonly PreviewPanel _preview;
    readonly FontCache _cache = new();
    readonly System.Windows.Forms.Timer _selTimer = new() { Interval = 60 };
    readonly Font _fileFont;

    LoadedFont? _previewFont;
    FontSheet? _previewSheet;
    FontEntry? _previewEntry;
    string _currentFolder = "";
    volatile int _scanId;
    bool _busy;
    int _filterId;
    FontEntry[] _all = Array.Empty<FontEntry>();
    bool _suppressSel;
    bool _ctxBlocked;

    public MainForm(AppSettings settings)
    {
        _settings = settings;
        AutoScaleMode = AutoScaleMode.None;
        Text = "Просмотр шрифтов";
        ClientSize = new Size(Ui.S(1100), Ui.S(700));
        MinimumSize = new Size(Ui.S(640), Ui.S(360));
        StartPosition = FormStartPosition.CenterScreen;
        _fileFont = new Font("Segoe UI", 11 * Ui.Scale, FontStyle.Regular, GraphicsUnit.Pixel);

        // Панель инструментов
        int ic = Ui.S(20);
        var tool = new ToolStrip
        {
            GripStyle = ToolStripGripStyle.Hidden,
            Padding = new Padding(4, 2, 4, 2),
            ImageScalingSize = new Size(ic, ic)
        };
        ToolStripButton Btn(string text, Bitmap img, string tip) => new(text, img)
        {
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            TextImageRelation = TextImageRelation.ImageBeforeText,
            ToolTipText = tip
        };
        _btnFolder = Btn("Выбрать папку…", Icons.OpenFolder(ic), "Выбрать папку со шрифтами");
        _btnExport = Btn("Экспортировать картинку…", Icons.Picture(ic), "Сохранить картинку выбранного шрифта");
        _btnExport.Enabled = false;
        _btnInstall = Btn("Установить шрифт", Icons.Install(ic), "Установить выбранный шрифт в систему");
        _btnInstall.Enabled = false;
        _btnSettings = Btn("Настройки…", Icons.Gear(ic), "Настройки программы");

        _search = new SearchBox { Size = new Size(Ui.S(250), Ui.S(24)) };
        var searchHost = new ToolStripControlHost(_search)
        {
            Alignment = ToolStripItemAlignment.Right,
            AutoSize = false,
            Size = _search.Size,
            Margin = new Padding(0, 1, 6, 1)
        };
        tool.Items.AddRange(new ToolStripItem[]
        {
            _btnFolder, _btnExport, _btnInstall, new ToolStripSeparator(), _btnSettings, searchHost
        });

        // Контекстное меню списка
        int mic = Ui.S(16);
        _menu = new ContextMenuStrip { ImageScalingSize = new Size(mic, mic) };
        _menu.Items.Add(new ToolStripMenuItem("Установить шрифт", Icons.Install(mic), (_, _) => InstallSelected()));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Свойства…", Icons.Info(mic), (_, _) => ShowProperties()));
        _menu.Opening += (_, e) =>
        {
            e.Cancel = _list == null || _list.SelectedItem == null || _ctxBlocked;
            _ctxBlocked = false;
        };

        // Строка состояния
        var status = new StatusStrip();
        _progress = new ToolStripProgressBar
        {
            AutoSize = false,
            Size = new Size(Ui.S(180), Ui.S(16)),
            Visible = false,
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 30
        };
        _lblStatus = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft, Text = "Загрузка…" };
        status.Items.Add(_progress);
        status.Items.Add(_lblStatus);

        // Список + просмотр
        _list = new FontListBox { Dock = DockStyle.Fill, ItemHeight = Ui.S(44) };
        _preview = new PreviewPanel { Dock = DockStyle.Fill };
        _split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterWidth = Ui.S(6) };
        _split.Panel1.Controls.Add(_list);
        _split.Panel2.Controls.Add(_preview);

        Controls.Add(_split);
        Controls.Add(status);
        Controls.Add(tool);

        _list.DrawItem += List_DrawItem;
        _list.ContextMenuStrip = _menu;
        _list.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            int i = _list.IndexFromPoint(e.Location);
            _ctxBlocked = i < 0;
            if (i >= 0) _list.SelectedIndex = i;
        };
        _list.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressSel) return;
            UpdateButtons();
            _selTimer.Stop();
            _selTimer.Start();
        };
        _search.QueryChanged += (_, _) => { _filterTimer.Stop(); _filterTimer.Start(); };
        _filterTimer.Tick += (_, _) => { _filterTimer.Stop(); _ = ApplyFilterAsync(); };
        _btnInstall.Click += (_, _) => InstallSelected();
        _selTimer.Tick += (_, _) => { _selTimer.Stop(); ShowSelected(); };
        _btnFolder.Click += (_, _) => ChooseFolder();
        _btnExport.Click += (_, _) => ExportImage();
        _btnSettings.Click += (_, _) => OpenSettings();
        FormClosed += (_, _) =>
        {
            _previewSheet?.Dispose();
            _previewFont?.Dispose();
            _cache.Dispose();
        };
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        try
        {
            _split.Panel1MinSize = Ui.S(160);
            _split.SplitterDistance = Ui.S(320);
        }
        catch { }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Update();   // сначала полностью отрисовать интерфейс
        BeginInvoke(new Action(() => _ = ScanAsync(_settings.DefaultFolder)));
    }

    // ---------------- Список ----------------

    void List_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _list.Items.Count) return;
        var entry = (FontEntry)_list.Items[e.Index]!;
        e.DrawBackground();
        bool sel = (e.State & DrawItemState.Selected) != 0;
        var fore = sel ? SystemColors.HighlightText : SystemColors.WindowText;
        var sub = sel ? SystemColors.HighlightText : Color.DimGray;
        var g = e.Graphics;
        var b = e.Bounds;
        int x = b.X + Ui.S(6);

        if (_settings.ShowGrid)
        {
            using var gp = new Pen(Color.FromArgb(sel ? 120 : 214, sel ? SystemColors.HighlightText : Color.Gray));
            g.DrawLine(gp, b.Left, b.Bottom - 1, b.Right, b.Bottom - 1);
        }

        entry.EnsureInfo();
        var st = g.Save();
        try
        {
            g.SetClip(b, CombineMode.Intersect);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            bool drawn = false;
            var lf = _cache.Get(entry);
            if (lf != null)
            {
                try { lf.Draw(g, entry.DisplayName, 18 * Ui.Scale, fore, x, b.Y + Ui.S(3)); drawn = true; }
                catch { }
            }
            if (!drawn)
                TextRenderer.DrawText(g, entry.DisplayName, e.Font, new Point(x, b.Y + Ui.S(6)), fore,
                    TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);

            TextRenderer.DrawText(g, entry.FileName, _fileFont, new Point(x, b.Y + Ui.S(27)), sub,
                TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }
        finally { g.Restore(st); }
    }

    // ---------------- Сканирование ----------------

    static FontEntry[]? ScanFolder(string folder, bool subdirs, Func<bool> cancelled, IProgress<int> progress)
    {
        var opt = new EnumerationOptions
        {
            RecurseSubdirectories = subdirs,
            IgnoreInaccessible = true,
            AttributesToSkip = 0,            // в C:\Windows\Fonts файлы часто Hidden/System
            MatchCasing = MatchCasing.CaseInsensitive
        };
        var list = new List<FontEntry>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long last = 0;
        foreach (var f in Directory.EnumerateFiles(folder, "*.*", opt))
        {
            if (cancelled()) return null;
            if (FontEntry.IsSupported(f)) list.Add(new FontEntry(f));
            if (sw.ElapsedMilliseconds - last >= 100)
            {
                last = sw.ElapsedMilliseconds;
                progress.Report(list.Count);
            }
        }
        progress.Report(list.Count);
        list.Sort((x, y) =>
        {
            int c = string.Compare(x.FileName, y.FileName, StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : string.Compare(x.Path, y.Path, StringComparison.OrdinalIgnoreCase);
        });
        return list.ToArray();
    }

    /// <summary>Читает названия шрифтов в фоне (параллельно), нужно для поиска по названию.</summary>
    void LoadNames(FontEntry[] entries, int id, IProgress<int> progress)
    {
        int done = 0;
        var po = new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount - 1, 1, 4) };
        try
        {
            Parallel.For(0, entries.Length, po, (i, state) =>
            {
                if (id != _scanId) { state.Stop(); return; }
                entries[i].EnsureInfo();
                int d = Interlocked.Increment(ref done);
                if ((d & 31) == 0 || d == entries.Length) progress.Report(d);
            });
        }
        catch { }
    }

    void HideProgress()
    {
        _progress.Visible = false;
        _busy = false;
    }

    async Task ScanAsync(string folder)
    {
        int id = ++_scanId;
        _currentFolder = folder;
        _busy = true;

        // Сразу очищаем интерфейс и показываем прогресс (неопределённый, пока ищем файлы)
        ClearPreview();
        _suppressSel = true;
        _list.Items.Clear();
        _suppressSel = false;
        _all = Array.Empty<FontEntry>();
        UpdateButtons();
        _progress.Style = ProgressBarStyle.Marquee;
        _progress.Visible = true;
        _lblStatus.Text = "Поиск шрифтов: " + folder + " …";

        bool sub = _settings.IncludeSubfolders;
        var findProgress = new Progress<int>(n =>
        {
            if (id == _scanId) _lblStatus.Text = $"Поиск шрифтов… найдено: {n} · {folder}";
        });

        FontEntry[]? entries;
        try
        {
            entries = await Task.Run(() => ScanFolder(folder, sub, () => id != _scanId, findProgress));
        }
        catch (Exception ex)
        {
            if (id == _scanId) { HideProgress(); _lblStatus.Text = "Ошибка: " + ex.Message; }
            return;
        }
        if (entries == null || id != _scanId) return;

        // Список появляется сразу; названия дочитываются в фоне с прогрессом
        _cache.Clear();
        _all = entries;
        await ApplyFilterAsync();
        if (id != _scanId) return;

        if (entries.Length == 0)
        {
            HideProgress();
            UpdateStatus();
            return;
        }

        _progress.Style = ProgressBarStyle.Continuous;
        _progress.Minimum = 0;
        _progress.Maximum = entries.Length;
        _progress.Value = 0;
        _lblStatus.Text = $"Чтение названий шрифтов: 0 из {entries.Length}";
        var readProgress = new Progress<int>(n =>
        {
            if (id != _scanId) return;
            _progress.Value = Math.Max(_progress.Value, Math.Min(n, _progress.Maximum));
            _lblStatus.Text = $"Чтение названий шрифтов: {n} из {entries.Length}";
        });
        await Task.Run(() => LoadNames(entries, id, readProgress));
        if (id != _scanId) return;

        HideProgress();
        UpdateStatus();
    }

    static bool Matches(FontEntry e, string[] tokens)
    {
        e.EnsureInfo();
        foreach (var t in tokens)
            if (e.DisplayName.IndexOf(t, StringComparison.OrdinalIgnoreCase) < 0 &&
                e.FileName.IndexOf(t, StringComparison.OrdinalIgnoreCase) < 0)
                return false;
        return true;
    }

    async Task ApplyFilterAsync()
    {
        int id = ++_filterId;
        string q = _search.Query.Trim();
        var all = _all;
        FontEntry[] result;
        if (q.Length == 0) result = all;
        else
        {
            var tokens = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            result = await Task.Run(() => all.Where(e => Matches(e, tokens)).ToArray());
            if (id != _filterId) return;
        }
        SetListItems(result);
        UpdateStatus();
    }

    void SetListItems(FontEntry[] items)
    {
        var prev = _list.SelectedItem as FontEntry;
        _suppressSel = true;
        _list.BeginUpdate();
        _list.Items.Clear();
        _list.Items.AddRange(items);
        int idx = prev == null ? -1 : Array.IndexOf(items, prev);
        if (idx >= 0) _list.SelectedIndex = idx;
        _list.EndUpdate();
        _suppressSel = false;
        if (prev != null && idx < 0) ClearPreview();
        UpdateButtons();
    }

    void UpdateButtons() => _btnInstall.Enabled = _list.SelectedItem is FontEntry;

    void UpdateStatus()
    {
        if (_busy) return;
        string folder = _currentFolder + (_settings.IncludeSubfolders ? " (с подкаталогами)" : "");
        if (_all.Length == 0) _lblStatus.Text = "Шрифты не найдены · " + folder;
        else if (_list.Items.Count != _all.Length) _lblStatus.Text = $"Найдено: {_list.Items.Count} из {_all.Length} · {folder}";
        else _lblStatus.Text = $"Шрифтов: {_all.Length} · {folder}";
    }

    void ChooseFolder()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Выберите папку со шрифтами",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
            SelectedPath = Directory.Exists(_currentFolder) ? _currentFolder : ""
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) _ = ScanAsync(dlg.SelectedPath);
    }

    // ---------------- Просмотр ----------------

    void ClearPreview()
    {
        _selTimer.Stop();
        _preview.SetSheet(null, null);
        _previewSheet?.Dispose();
        _previewFont?.Dispose();
        _previewSheet = null;
        _previewFont = null;
        _previewEntry = null;
        _btnExport.Enabled = false;
    }

    void ShowSelected()
    {
        if (_list.SelectedItem is not FontEntry entry) return;
        var oldSheet = _previewSheet;
        var oldFont = _previewFont;

        var lf = LoadedFont.TryLoad(entry);
        FontSheet? sheet = null;
        if (lf != null)
        {
            try { sheet = new FontSheet(lf, entry, _settings.SampleText, _settings.SampleSize, int.MaxValue, Ui.Scale); }
            catch { lf.Dispose(); lf = null; }
        }
        _previewFont = lf;
        _previewSheet = sheet;
        _previewEntry = entry;
        _preview.SetSheet(sheet, lf == null ? "Не удалось загрузить шрифт:\n" + entry.FileName : null);
        _btnExport.Enabled = lf != null;

        oldSheet?.Dispose();
        oldFont?.Dispose();
    }

    // ---------------- Экспорт ----------------

    void ExportImage()
    {
        if (_previewFont == null || _previewEntry == null) return;
        string name = string.Concat(_previewEntry.DisplayName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        using var dlg = new SaveFileDialog
        {
            Filter = "PNG (*.png)|*.png",
            DefaultExt = "png",
            FileName = name + ".png",
            Title = "Экспорт картинки"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            UseWaitCursor = true;
            using var sheet = new FontSheet(_previewFont, _previewEntry, _settings.SampleText, _settings.SampleSize,
                _settings.MaxExportChars, 1f, fixedCols: 16);
            using (var tmp = new Bitmap(1, 1))
            using (var gm = Graphics.FromImage(tmp))
                sheet.Layout(gm, sheet.PreferredWidth);

            using var bmp = new Bitmap(sheet.Width, sheet.Height, PixelFormat.Format32bppRgb);
            bmp.SetResolution(96, 96);
            using (var g = Graphics.FromImage(bmp))
                sheet.Draw(g, 0, new Rectangle(0, 0, bmp.Width, bmp.Height), Color.White, Color.Black);
            bmp.Save(dlg.FileName, ImageFormat.Png);
            _lblStatus.Text = "Сохранено: " + dlg.FileName;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Не удалось сохранить картинку:\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { UseWaitCursor = false; }
    }

    // ---------------- Установка и свойства ----------------

    void InstallSelected()
    {
        if (_list.SelectedItem is not FontEntry e) return;
        e.EnsureInfo();
        if (MessageBox.Show(this,
                $"Установить шрифт «{e.DisplayName}» в систему?\n\nФайл будет скопирован в:\n{AppSettings.OsFontsFolder()}",
                "Установка шрифта", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            UseWaitCursor = true;
            string msg = FontInstaller.IsAdmin() ? FontInstaller.Install(e.Path) : FontInstaller.InstallElevated(e.Path);
            MessageBox.Show(this, msg, "Установка шрифта", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (OperationCanceledException) { _lblStatus.Text = "Установка отменена."; }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Не удалось установить шрифт:\n" + ex.Message, "Установка шрифта",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { UseWaitCursor = false; }
    }

    void ShowProperties()
    {
        if (_list.SelectedItem is not FontEntry e) return;
        using var dlg = new FontPropertiesForm(e);
        dlg.ShowDialog(this);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.F)) { _search.FocusInput(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ---------------- Настройки ----------------

    void OpenSettings()
    {
        using var dlg = new SettingsForm(_settings);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        bool rescan = !string.Equals(_settings.DefaultFolder, dlg.Folder, StringComparison.OrdinalIgnoreCase)
                      || _settings.IncludeSubfolders != dlg.Subfolders;
        dlg.Apply(_settings);
        _settings.Save();

        _list.Invalidate();
        if (rescan) _ = ScanAsync(_settings.DefaultFolder);
        else ShowSelected();
    }
}
