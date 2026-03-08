using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using System.Text.Json;

namespace NektoMe_MITM_text;

public sealed class NektoAudioChatManager : IDisposable
{
    private readonly BrowserKind _browser;
    private ChromeDriver? _driverA;
    private ChromeDriver? _driverB;

    public NektoAudioChatManager(BrowserKind browser)
    {
        _browser = browser;
    }

    public void RunInteractive()
    {
        Console.WriteLine();
        Console.WriteLine("=== AudioChat MITM режим ===");
        Console.WriteLine("Будут открыты 2 браузера на https://nekto.me/audiochat/");
        Console.WriteLine("После запуска нажмите Enter, чтобы авто-подставить токены.");
        Console.ReadLine();

        StartBrowsers();

        Console.WriteLine("Войдите в оба аккаунта (если нужно), затем нажмите Enter...");
        Console.ReadLine();

        var tokenA = ReadAuthToken(_driverA!);
        var tokenB = ReadAuthToken(_driverB!);

        if (string.IsNullOrWhiteSpace(tokenA) || string.IsNullOrWhiteSpace(tokenB))
        {
            Console.WriteLine("Не удалось прочитать токены из localStorage. Проверьте вход и повторите.");
            return;
        }

        Console.WriteLine("Токены AudioChat получены. Переинициализирую сессию на обоих окнах...");
        InjectAuthToken(_driverA!, tokenA);
        InjectAuthToken(_driverB!, tokenB);

        _driverA!.Navigate().GoToUrl("https://nekto.me/audiochat/");
        _driverB!.Navigate().GoToUrl("https://nekto.me/audiochat/");
        _driverA.Navigate().Refresh();
        _driverB.Navigate().Refresh();

        ConfigureBrowserAudioDevices(_driverA, _driverB);

        PrintRoutingHelp();

        Console.WriteLine("Окна audiochat готовы.");
        Console.WriteLine("Если нужен мост, запусти его из главного меню (пункт 3).\n");
        Console.WriteLine("Нажмите Enter для возврата в главное меню...");
        Console.ReadLine();
    }

    private void StartBrowsers()
    {
        var optionsA = BuildOptions();
        var optionsB = BuildOptions();

        _driverA = NektoBrowserSupport.CreateDriver(_browser, optionsA);
        _driverB = NektoBrowserSupport.CreateDriver(_browser, optionsB);

        _driverA.Navigate().GoToUrl("https://nekto.me/audiochat/");
        _driverB.Navigate().GoToUrl("https://nekto.me/audiochat/");
    }

    private ChromeOptions BuildOptions()
    {
        var options = NektoBrowserSupport.BuildOptions(_browser);
        options.AddArgument("--use-fake-ui-for-media-stream");
        return options;
    }

    private static string? ReadAuthToken(IWebDriver driver)
    {
        var js = (IJavaScriptExecutor)driver;
        var token = js.ExecuteScript(
            @"try {
  const fromAudio = JSON.parse(localStorage.getItem('storage_audio_v2') || '{}')?.user?.authToken;
  if (fromAudio) return fromAudio;
  const fromCommon = JSON.parse(localStorage.getItem('storage_v2') || '{}')?.user?.authToken;
  return fromCommon || null;
} catch(e) {
  return null;
}"
        );

        return token as string;
    }

    private static void InjectAuthToken(IWebDriver driver, string token)
    {
        ((IJavaScriptExecutor)driver).ExecuteScript(
            @"const token = arguments[0];
function putToken(key) {
  let data = {};
  try { data = JSON.parse(localStorage.getItem(key) || '{}') || {}; } catch (e) { data = {}; }
  if (typeof data !== 'object' || data === null) data = {};
  if (!data.user || typeof data.user !== 'object') data.user = {};
  data.user.authToken = token;
  localStorage.setItem(key, JSON.stringify(data));
}
putToken('storage_audio_v2');
putToken('storage_v2');",
            token
        );
    }

    private static void PrintRoutingHelp()
    {
        Console.WriteLine();
        Console.WriteLine("Настройка browser-only завершена:");
        Console.WriteLine("1) Для окна A закреплены отдельные mic/speaker");
        Console.WriteLine("2) Для окна B закреплены отдельные mic/speaker");
        Console.WriteLine("3) В обоих окнах нажмите 'Начать разговор'");
        Console.WriteLine();
    }

    private static void ConfigureBrowserAudioDevices(IWebDriver driverA, IWebDriver driverB)
    {
        Console.WriteLine("Настройка аудио по окнам браузера (speaker + microphone отдельно для каждого окна).");
        Console.WriteLine("Маршрутизация выполняется только средствами браузера.\n");
        if (NektoAudioRouteProfile.TryGet(out var profile))
        {
            Console.WriteLine($"Найден профиль из моста ({profile.SavedAt:HH:mm:ss}). Пробую применить его автоматически...");

            var inputsAProfile = GetAudioInputs(driverA);
            var inputsBProfile = GetAudioInputs(driverB);
            var outputsAProfile = GetAudioOutputs(driverA);
            var outputsBProfile = GetAudioOutputs(driverB);

            var micA = FindBestMatchByLabel(inputsAProfile, profile.FirstMicLabel);
            var micB = FindBestMatchByLabel(inputsBProfile, profile.SecondMicLabel);
            var outA = FindBestMatchByLabel(outputsAProfile, profile.FirstOutputLabel);
            var outB = FindBestMatchByLabel(outputsBProfile, profile.SecondOutputLabel);

            var micAOk = micA is not null && ApplyMicrophoneId(driverA, micA.DeviceId);
            var micBOk = micB is not null && ApplyMicrophoneId(driverB, micB.DeviceId);
            var outAOk = outA is not null && ApplySinkId(driverA, outA.DeviceId);
            var outBOk = outB is not null && ApplySinkId(driverB, outB.DeviceId);

            if (micAOk && micBOk && outAOk && outBOk)
            {
                Console.WriteLine($"Окно A: mic={micA!.Label}, speaker={outA!.Label}");
                Console.WriteLine($"Окно B: mic={micB!.Label}, speaker={outB!.Label}");
                Console.WriteLine("Профиль применен автоматически. Повторный ручной выбор не нужен.");
                return;
            }

            Console.WriteLine("Автоприменение профиля сработало не полностью. Перехожу к ручному уточнению.");
        }

        var inputsA = GetAudioInputs(driverA);
        if (inputsA.Count == 0)
        {
            Console.WriteLine("Не удалось получить список microphones из окна A. Оставляю системный default.");
        }
        else
        {
            Console.WriteLine("\nОкно A: выбери микрофон:");
            PrintAudioDevices(inputsA);
            var selectedMicA = SelectAudioDevice(inputsA, "Индекс microphone для окна A: ");
            var appliedMicA = ApplyMicrophoneId(driverA, selectedMicA.DeviceId);
            Console.WriteLine(appliedMicA
                ? $"Окно A mic закреплен на: {selectedMicA.Label}"
                : "Окно A: microphone не закрепился, будет использован системный default.");
        }

        var inputsB = GetAudioInputs(driverB);
        if (inputsB.Count == 0)
        {
            Console.WriteLine("Не удалось получить список microphones из окна B. Оставляю системный default.");
        }
        else
        {
            Console.WriteLine("\nОкно B: выбери микрофон:");
            PrintAudioDevices(inputsB);
            var selectedMicB = SelectAudioDevice(inputsB, "Индекс microphone для окна B: ");
            var appliedMicB = ApplyMicrophoneId(driverB, selectedMicB.DeviceId);
            Console.WriteLine(appliedMicB
                ? $"Окно B mic закреплен на: {selectedMicB.Label}"
                : "Окно B: microphone не закрепился, будет использован системный default.");
        }

        Console.WriteLine();
        Console.WriteLine("Теперь выбери устройства вывода (speaker) для каждого окна.");

        var outputsA = GetAudioOutputs(driverA);
        if (outputsA.Count == 0)
        {
            Console.WriteLine("Не удалось получить список audio output из окна A. Оставляю системный default.");
            return;
        }

        Console.WriteLine("\nОкно A: выбери устройство вывода звука:");
        PrintAudioDevices(outputsA);
        var selectedA = SelectAudioDevice(outputsA, "Индекс output для окна A: ");
        var appliedA = ApplySinkId(driverA, selectedA.DeviceId);

        var outputsB = GetAudioOutputs(driverB);
        if (outputsB.Count == 0)
        {
            Console.WriteLine("Не удалось получить список audio output из окна B. Оставляю системный default.");
            return;
        }

        Console.WriteLine("\nОкно B: выбери устройство вывода звука:");
        PrintAudioDevices(outputsB);
        var selectedB = SelectAudioDevice(outputsB, "Индекс output для окна B: ");
        var appliedB = ApplySinkId(driverB, selectedB.DeviceId);

        Console.WriteLine();
        Console.WriteLine(appliedA
                ? $"Окно A закреплено на: {selectedA.Label}"
                : "Окно A: setSinkId не применился, будет использован системный default.");
        Console.WriteLine(appliedB
                ? $"Окно B закреплено на: {selectedB.Label}"
                : "Окно B: setSinkId не применился, будет использован системный default.");
    }

    private static AudioDevice? FindBestMatchByLabel(IReadOnlyList<AudioDevice> devices, string profileLabel)
    {
        if (devices.Count == 0 || string.IsNullOrWhiteSpace(profileLabel))
            return null;

        var exact = devices.FirstOrDefault(d =>
            string.Equals(d.Label, profileLabel, StringComparison.OrdinalIgnoreCase)
        );
        if (exact is not null)
            return exact;

        var contains = devices.FirstOrDefault(d =>
            d.Label.Contains(profileLabel, StringComparison.OrdinalIgnoreCase)
            || profileLabel.Contains(d.Label, StringComparison.OrdinalIgnoreCase)
        );

        return contains;
    }

    private static List<AudioDevice> GetAudioInputs(IWebDriver driver)
    {
        var js = (IJavaScriptExecutor)driver;
        var raw = js.ExecuteAsyncScript(
                @"const done = arguments[arguments.length - 1];
(async () => {
    try {
        try {
            const s = await navigator.mediaDevices.getUserMedia({ audio: true });
            s.getTracks().forEach(t => t.stop());
        } catch (e) {}

        const list = (await navigator.mediaDevices.enumerateDevices())
            .filter(d => d.kind === 'audioinput')
            .map(d => ({ id: d.deviceId, label: d.label || '(без имени)' }));
        done(JSON.stringify(list));
    } catch (e) {
        done('[]');
    }
})();"
        )?.ToString();

        return ParseAudioDevices(raw);
    }

    private static List<AudioDevice> GetAudioOutputs(IWebDriver driver)
    {
        var js = (IJavaScriptExecutor)driver;
        var raw = js.ExecuteAsyncScript(
                @"const done = arguments[arguments.length - 1];
(async () => {
    try {
        // Пробуем открыть аудио-права, чтобы labels у output-устройств были заполнены.
        try {
            const s = await navigator.mediaDevices.getUserMedia({ audio: true });
            s.getTracks().forEach(t => t.stop());
        } catch (e) {}

        const list = (await navigator.mediaDevices.enumerateDevices())
            .filter(d => d.kind === 'audiooutput')
            .map(d => ({ id: d.deviceId, label: d.label || '(без имени)' }));
        done(JSON.stringify(list));
    } catch (e) {
        done('[]');
    }
})();"
        )?.ToString();

        return ParseAudioDevices(raw);
    }

    private static List<AudioDevice> ParseAudioDevices(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new List<AudioDevice>();

        try
        {
            var doc = JsonDocument.Parse(raw);
            var result = new List<AudioDevice>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var id = item.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                var label = item.TryGetProperty("label", out var lblEl)
                        ? lblEl.GetString()
                        : "(без имени)";

                if (!string.IsNullOrWhiteSpace(id))
                    result.Add(new AudioDevice(id!, label ?? "(без имени)"));
            }

            return result;
        }
        catch
        {
            return new List<AudioDevice>();
        }
    }

    private static void PrintAudioDevices(IReadOnlyList<AudioDevice> outputs)
    {
        for (var i = 0; i < outputs.Count; i++)
            Console.WriteLine($"[{i}] {outputs[i].Label}");
    }

    private static AudioDevice SelectAudioDevice(IReadOnlyList<AudioDevice> outputs, string prompt)
    {
        while (true)
        {
            Console.Write(prompt);
            var text = (Console.ReadLine() ?? string.Empty).Trim();
            if (int.TryParse(text, out var index) && index >= 0 && index < outputs.Count)
                return outputs[index];

            Console.WriteLine("Неверный индекс. Попробуйте снова.");
        }
    }

    private static bool ApplyMicrophoneId(IWebDriver driver, string deviceId)
    {
        var js = (IJavaScriptExecutor)driver;
        var result = js.ExecuteAsyncScript(
                @"const micId = arguments[0];
const done = arguments[arguments.length - 1];

(async () => {
    try {
        if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
            done('fail');
            return;
        }

        if (!window.__nektoOriginalGetUserMedia) {
            window.__nektoOriginalGetUserMedia = navigator.mediaDevices.getUserMedia.bind(navigator.mediaDevices);
        }

        window.__nektoPreferredMicId = micId;

        navigator.mediaDevices.getUserMedia = function(constraints) {
            const original = window.__nektoOriginalGetUserMedia;
            const c = constraints && typeof constraints === 'object'
                ? JSON.parse(JSON.stringify(constraints))
                : { audio: true };

            if (c.audio !== false) {
                if (c.audio === true || c.audio == null) c.audio = {};
                if (typeof c.audio !== 'object') c.audio = {};
                c.audio.deviceId = { exact: window.__nektoPreferredMicId };
            }

            return original(c);
        };

        // Пробный запрос, чтобы закрепить и проверить выбранный mic.
        try {
            const s = await navigator.mediaDevices.getUserMedia({ audio: true });
            s.getTracks().forEach(t => t.stop());
        } catch (e) {}

        done('ok');
    } catch (e) {
        done('fail');
    }
})();",
                deviceId
        )?.ToString();

        return string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ApplySinkId(IWebDriver driver, string deviceId)
    {
        var js = (IJavaScriptExecutor)driver;
        var result = js.ExecuteAsyncScript(
                @"const sinkId = arguments[0];
const done = arguments[arguments.length - 1];

(async () => {
    try {
        const applyToMedia = async () => {
            const media = Array.from(document.querySelectorAll('audio,video'));
            for (const el of media) {
                if (typeof el.setSinkId === 'function') {
                    try { await el.setSinkId(sinkId); } catch (e) {}
                }
            }
        };

        await applyToMedia();

        if (window.__nektoSinkObserver) {
            try { window.__nektoSinkObserver.disconnect(); } catch (e) {}
        }

        const observer = new MutationObserver(() => { applyToMedia(); });
        observer.observe(document.documentElement, { childList: true, subtree: true });
        window.__nektoSinkObserver = observer;

        done('ok');
    } catch (e) {
        done('fail');
    }
})();",
                deviceId
        )?.ToString();

        return string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record AudioDevice(string DeviceId, string Label);

    public void Dispose()
    {
        try
        {
            _driverA?.Quit();
        }
        catch { }

        try
        {
            _driverB?.Quit();
        }
        catch { }
    }
}