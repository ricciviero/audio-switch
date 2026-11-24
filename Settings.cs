using System.Text.Json;
using Microsoft.Win32;

namespace CambiaAudio;

/// <summary>Impostazioni salvate in %APPDATA%\CambiaAudio\config.json.</summary>
sealed class AppSettings
{
    /// <summary>Uscite tra cui alterna la scorciatoia. Se vuota, alterna tra tutte quelle attive.</summary>
    public List<string> CycleDeviceIds { get; set; } = new();

    static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CambiaAudio");
    public static string FilePath => Path.Combine(Folder, "config.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch
        {
            // File illeggibile: si riparte dalle impostazioni vuote.
        }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}

/// <summary>Avvio automatico con Windows tramite la chiave Run dell'utente.</summary>
static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    // Stato che Gestione attività scrive quando l'utente disattiva un programma all'avvio.
    const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    const string ValueName = "CambiaAudio";

    static string Command => $"\"{Environment.ProcessPath}\"";

    public static bool IsEnabled()
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunKey);
        if (run?.GetValue(ValueName) is not string cmd || cmd.Length == 0) return false;

        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
        // Primo byte pari = attivo, dispari = disattivato da Gestione attività.
        return approved?.GetValue(ValueName) is not byte[] state || state.Length == 0 || state[0] % 2 == 0;
    }

    public static void Set(bool enabled)
    {
        using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            if (enabled) run.SetValue(ValueName, Command);
            else run.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        // Toglie un eventuale "disattivato" lasciato da Gestione attività.
        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
        approved?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
