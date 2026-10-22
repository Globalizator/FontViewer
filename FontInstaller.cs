using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32;

namespace FontViewer;

/// <summary>Установка шрифта в систему: копирование в папку Fonts + запись в реестр + оповещение приложений.</summary>
static class FontInstaller
{
    const string RegPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts";

    public static bool IsAdmin()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>Установлен ли такой шрифт (запись в реестре + файл в системной папке того же размера).</summary>
    public static bool IsInstalled(string srcPath)
    {
        try
        {
            string name = Path.GetFileName(srcPath);
            using var key = Registry.LocalMachine.OpenSubKey(RegPath);
            if (key == null) return false;
            foreach (var vn in key.GetValueNames())
            {
                if (key.GetValue(vn) is not string s) continue;
                if (!s.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                    !s.Equals(srcPath, StringComparison.OrdinalIgnoreCase)) continue;
                string full = Path.IsPathRooted(s) ? s : Path.Combine(AppSettings.OsFontsFolder(), s);
                return File.Exists(full) && new FileInfo(full).Length == new FileInfo(srcPath).Length;
            }
        }
        catch { }
        return false;
    }

    /// <summary>Установка в текущем процессе (нужны права администратора).</summary>
    public static string Install(string src)
    {
        if (!File.Exists(src)) throw new FileNotFoundException("Файл шрифта не найден.", src);
        if (IsInstalled(src)) return "Этот шрифт уже установлен в системе.";

        string dest = Path.Combine(AppSettings.OsFontsFolder(), Path.GetFileName(src));
        bool sameFile = string.Equals(Path.GetFullPath(src), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase);
        bool copied = false;
        if (!sameFile)
        {
            if (File.Exists(dest))
                throw new IOException("В системной папке уже есть другой файл с таким именем:\n" + dest);
            File.Copy(src, dest);
            copied = true;
        }

        string? regValue = null;
        try
        {
            var entry = new FontEntry(dest);
            entry.EnsureInfo();
            string baseName = entry.Ext switch
            {
                ".ttf" => entry.DisplayName + " (TrueType)",
                ".otf" => entry.DisplayName + " (OpenType)",
                _ => entry.DisplayName + (entry.Fon != null ? " (" + string.Join(",", entry.Fon.Heights) + ")" : "")
            };

            using var key = Registry.LocalMachine.OpenSubKey(RegPath, true)
                            ?? throw new InvalidOperationException("Не удалось открыть раздел реестра со шрифтами.");
            string value = baseName;
            if (key.GetValue(value) != null) value += " [" + entry.FileName + "]";
            regValue = value;
            key.SetValue(value, entry.FileName, RegistryValueKind.String);

            if (Native.AddFontResourceW(dest) == 0)
                throw new InvalidDataException("Windows не смог загрузить этот файл как шрифт.");
            // WM_FONTCHANGE всем окнам, чтобы приложения обновили списки шрифтов
            Native.SendMessageTimeout(new IntPtr(0xFFFF), 0x1D, IntPtr.Zero, IntPtr.Zero, 2, 1000, out _);
        }
        catch
        {
            try
            {
                if (regValue != null)
                {
                    using var k = Registry.LocalMachine.OpenSubKey(RegPath, true);
                    k?.DeleteValue(regValue, false);
                }
            }
            catch { }
            try { if (copied) File.Delete(dest); } catch { }
            throw;
        }
        return "Шрифт установлен:\n" + dest;
    }

    /// <summary>Запускает копию программы с запросом UAC, которая выполняет установку.</summary>
    public static string InstallElevated(string src)
    {
        string resultFile = Path.Combine(Path.GetTempPath(), "fontviewer_" + Guid.NewGuid().ToString("N") + ".txt");
        var psi = new ProcessStartInfo(Environment.ProcessPath!, $"--install \"{src}\" \"{resultFile}\"")
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };
        Process? p;
        try { p = Process.Start(psi); }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { throw new OperationCanceledException(); }
        if (p == null) throw new InvalidOperationException("Не удалось запустить процесс установки.");
        p.WaitForExit();

        string text = "";
        try { if (File.Exists(resultFile)) { text = File.ReadAllText(resultFile); File.Delete(resultFile); } } catch { }
        if (p.ExitCode != 0)
            throw new InvalidOperationException(text.Length > 0 ? text : "Установка не выполнена.");
        return text.Length > 0 ? text : "Шрифт установлен.";
    }

    public static int RunHelper(string src, string resultFile)
    {
        try { File.WriteAllText(resultFile, Install(src)); return 0; }
        catch (Exception ex)
        {
            try { File.WriteAllText(resultFile, ex.Message); } catch { }
            return 1;
        }
    }
}
