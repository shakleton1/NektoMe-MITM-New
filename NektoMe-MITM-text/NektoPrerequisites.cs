using System.Diagnostics;
using NAudio.CoreAudioApi;

namespace NektoMe_MITM_text;

public static class NektoPrerequisites
{
    public static bool EnsureVoicePrerequisitesInteractive()
    {
        if (HasVirtualAudioEndpoints())
            return true;

        Console.WriteLine("Не найдены виртуальные аудио устройства (Voicemeeter/VB-CABLE).");
        Console.WriteLine("Запускаю автонастройку (скачает и установит зависимости через winget)...");

        var scriptPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "scripts", "setup-audiochat.ps1"));
        if (!File.Exists(scriptPath))
        {
            Console.WriteLine($"Скрипт не найден: {scriptPath}");
            return false;
        }

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\"",
            UseShellExecute = false,
        };

        using var process = Process.Start(psi);
        process?.WaitForExit();

        if (process?.ExitCode != 0)
        {
            Console.WriteLine("Автонастройка завершилась с ошибкой.");
            return false;
        }

        if (HasVirtualAudioEndpoints())
        {
            Console.WriteLine("Виртуальные аудио устройства найдены.");
            return true;
        }

        Console.WriteLine("Устройства пока не появились. Обычно помогает перезагрузка системы.");
        return false;
    }

    private static bool HasVirtualAudioEndpoints()
    {
        using var enumerator = new MMDeviceEnumerator();
        var all = enumerator
            .EnumerateAudioEndPoints(DataFlow.All, DeviceState.Active)
            .Select(d => d.FriendlyName)
            .ToList();

        return all.Any(n =>
            n.Contains("voicemeeter", StringComparison.OrdinalIgnoreCase)
            || n.Contains("vb-audio", StringComparison.OrdinalIgnoreCase)
            || n.Contains("cable", StringComparison.OrdinalIgnoreCase)
        );
    }
}
