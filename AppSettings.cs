using System.Text.Json;

namespace FontViewer;

sealed class AppSettings
{
    public string DefaultFolder { get; set; } = "";
    public bool IncludeSubfolders { get; set; } = false;
    public bool ShowGrid { get; set; } = true;
    public string SampleText { get; set; } = "Quick brown fox 123";
    public int SampleSize { get; set; } = 32;
    public int MaxExportChars { get; set; } = 2048;

    static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FontViewer", "settings.json");

    /// <summary>Папка шрифтов Windows (из настроек ОС).</summary>
    public static string OsFontsFolder()
    {
        var p = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        if (string.IsNullOrEmpty(p))
            p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
        return p;
    }

    public static AppSettings Load()
    {
        AppSettings? s = null;
        try
        {
            if (File.Exists(FilePath))
                s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
        }
        catch { }
        s ??= new AppSettings();
        s.Normalize();
        return s;
    }

    void Normalize()
    {
        SampleSize = Math.Clamp(SampleSize, 8, 300);
        MaxExportChars = Math.Clamp(MaxExportChars, 64, 4096);
        SampleText ??= "";
        if (string.IsNullOrWhiteSpace(DefaultFolder) || !Directory.Exists(DefaultFolder))
            DefaultFolder = OsFontsFolder();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
