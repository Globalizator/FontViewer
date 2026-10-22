namespace FontViewer;

sealed class SettingsForm : Form
{
    readonly TextBox _folder = new();
    readonly TextBox _sample = new();
    readonly CheckBox _sub = new();
    readonly CheckBox _grid = new();
    readonly NumericUpDown _size = new();
    readonly NumericUpDown _max = new();

    public string Folder => _folder.Text.Trim();
    public bool Subfolders => _sub.Checked;

    public SettingsForm(AppSettings s)
    {
        AutoScaleMode = AutoScaleMode.None;
        Text = "Настройки";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(Ui.S(540), Ui.S(284));

        int x1 = Ui.S(14), x2 = Ui.S(210), y = Ui.S(16), dy = Ui.S(34);

        Label Lbl(string t, int yy) => new() { Text = t, AutoSize = true, Location = new Point(x1, yy + Ui.S(3)) };

        Controls.Add(Lbl("Папка по умолчанию:", y));
        _folder.Location = new Point(x2, y);
        _folder.Width = Ui.S(240);
        _folder.Text = s.DefaultFolder;
        var browse = new Button { Text = "Обзор…", Location = new Point(x2 + Ui.S(246), y - 1), Size = new Size(Ui.S(74), Ui.S(26)) };
        browse.Click += (_, _) =>
        {
            using var d = new FolderBrowserDialog
            {
                Description = "Папка со шрифтами по умолчанию",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false,
                SelectedPath = Directory.Exists(_folder.Text) ? _folder.Text : ""
            };
            if (d.ShowDialog(this) == DialogResult.OK) _folder.Text = d.SelectedPath;
        };
        Controls.AddRange(new Control[] { _folder, browse });

        y += dy;
        _sub.Text = "Сканировать подкаталоги";
        _sub.AutoSize = true;
        _sub.Location = new Point(x2, y);
        _sub.Checked = s.IncludeSubfolders;
        Controls.Add(_sub);

        y += dy;
        _grid.Text = "Показывать сетку в списке";
        _grid.AutoSize = true;
        _grid.Location = new Point(x2, y);
        _grid.Checked = s.ShowGrid;
        Controls.Add(_grid);

        y += dy;
        Controls.Add(Lbl("Текст образца:", y));
        _sample.Location = new Point(x2, y);
        _sample.Width = Ui.S(320);
        _sample.Text = s.SampleText;
        Controls.Add(_sample);

        y += dy;
        Controls.Add(Lbl("Размер образца, px:", y));
        _size.Location = new Point(x2, y);
        _size.Width = Ui.S(80);
        _size.Minimum = 8; _size.Maximum = 300;
        _size.Value = Math.Clamp(s.SampleSize, 8, 300);
        Controls.Add(_size);

        y += dy;
        Controls.Add(Lbl("Макс. символов в экспорте:", y));
        _max.Location = new Point(x2, y);
        _max.Width = Ui.S(80);
        _max.Minimum = 64; _max.Maximum = 4096; _max.Increment = 64;
        _max.Value = Math.Clamp(s.MaxExportChars, 64, 4096);
        Controls.Add(_max);

        y += dy + Ui.S(8);
        var ok = new Button { Text = "OK", Location = new Point(ClientSize.Width - Ui.S(180), y), Size = new Size(Ui.S(80), Ui.S(28)) };
        var cancel = new Button { Text = "Отмена", Location = new Point(ClientSize.Width - Ui.S(94), y), Size = new Size(Ui.S(80), Ui.S(28)), DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) =>
        {
            if (!Directory.Exists(Folder))
            {
                MessageBox.Show(this, "Указанная папка не существует.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
        };
        Controls.AddRange(new Control[] { ok, cancel });
        AcceptButton = ok;
        CancelButton = cancel;
    }

    public void Apply(AppSettings s)
    {
        s.DefaultFolder = Folder;
        s.IncludeSubfolders = _sub.Checked;
        s.ShowGrid = _grid.Checked;
        s.SampleText = _sample.Text;
        s.SampleSize = (int)_size.Value;
        s.MaxExportChars = (int)_max.Value;
    }
}
