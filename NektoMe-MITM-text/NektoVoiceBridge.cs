using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Text.Json;

namespace NektoMe_MITM_text;

public sealed class NektoVoiceBridge : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private AudioBridgeLink? _linkAToB;
    private AudioBridgeLink? _linkBToA;
    private AudioBridgeLink? _monitorA;
    private AudioBridgeLink? _monitorB;
    private static readonly string PresetsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "NektoMe-MITM",
        "bridge-presets.json"
    );

    public IReadOnlyList<BridgeDeviceInfo> GetCaptureDevices() =>
        _enumerator
            .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.All)
            .Select(d => new BridgeDeviceInfo(d.ID, d.FriendlyName, d.State == DeviceState.Active, d.State.ToString()))
            .ToList();

    public IReadOnlyList<BridgeDeviceInfo> GetRenderDevices() =>
        _enumerator
            .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.All)
            .Select(d => new BridgeDeviceInfo(d.ID, d.FriendlyName, d.State == DeviceState.Active, d.State.ToString()))
            .ToList();

    public void StartManual(
        string firstMicId,
        string firstOutputId,
        string secondMicId,
        string secondOutputId,
        bool enableMonitoring,
        string? monitorOutputId
    )
    {
        var captures = _enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.All).ToList();
        var renders = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.All).ToList();

        var firstMic = FindActiveById(captures, firstMicId);
        var secondMic = FindActiveById(captures, secondMicId);
        var firstOutput = FindActiveById(renders, firstOutputId);
        var secondOutput = FindActiveById(renders, secondOutputId);

        MMDevice? monitorOutput = null;
        if (enableMonitoring)
        {
            if (string.IsNullOrWhiteSpace(monitorOutputId))
                throw new InvalidOperationException("Для мониторинга нужно выбрать output устройство.");

            monitorOutput = FindActiveById(renders, monitorOutputId);
        }

        NektoAudioRouteProfile.Save(
            firstMic.FriendlyName,
            firstOutput.FriendlyName,
            secondMic.FriendlyName,
            secondOutput.FriendlyName
        );

        StartLinks(firstMic, secondOutput, secondMic, firstOutput, monitorOutput);
    }

    public static IReadOnlyList<BridgePreset> LoadPresets()
    {
        try
        {
            if (!File.Exists(PresetsPath))
                return new List<BridgePreset>();

            var json = File.ReadAllText(PresetsPath);
            var presets = JsonSerializer.Deserialize<List<BridgePreset>>(json);
            return presets ?? new List<BridgePreset>();
        }
        catch
        {
            return new List<BridgePreset>();
        }
    }

    public static void SavePreset(BridgePreset preset)
    {
        if (string.IsNullOrWhiteSpace(preset.Name))
            throw new InvalidOperationException("Имя пресета не может быть пустым.");

        var list = LoadPresets().ToList();
        list.RemoveAll(p => string.Equals(p.Name, preset.Name, StringComparison.OrdinalIgnoreCase));
        list.Add(preset with { SavedAt = DateTimeOffset.Now });

        var dir = Path.GetDirectoryName(PresetsPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(PresetsPath, json);
    }

    public static BridgePreset? BuildAutoPreset(IReadOnlyList<BridgeDeviceInfo> captures, IReadOnlyList<BridgeDeviceInfo> renders)
    {
        var firstMic = FindByTokens(captures, "cable a output", "voicemeeter output", "line 1 output");
        var secondMic = FindByTokens(captures, "cable b output", "voicemeeter aux output", "line 2 output");
        var firstOut = FindByTokens(renders, "cable a input", "voicemeeter input", "line 1 input");
        var secondOut = FindByTokens(renders, "cable b input", "voicemeeter aux input", "line 2 input");
        var monitor = renders.FirstOrDefault(d => d.IsActive && !IsVirtual(d.Name));
        var monitorId = string.IsNullOrWhiteSpace(monitor.Id) ? null : monitor.Id;

        if (firstMic is null || secondMic is null || firstOut is null || secondOut is null)
            return null;

        return new BridgePreset(
            "Auto Cable A/B",
            firstMic.Value.Id,
            firstOut.Value.Id,
            secondMic.Value.Id,
            secondOut.Value.Id,
            monitorId,
            EnableMonitoring: false,
            DateTimeOffset.Now
        );
    }

    public static void PlayTestTone(string outputDeviceId, int milliseconds = 900)
    {
        using var enumerator = new MMDeviceEnumerator();
        var device = enumerator
            .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .FirstOrDefault(d => d.ID == outputDeviceId)
            ?? throw new InvalidOperationException("Выбранное output устройство не активно или не найдено.");

        using var waveOut = new WasapiOut(device, AudioClientShareMode.Shared, true, 20);
        var signal = new SignalGenerator(48000, 2)
        {
            Gain = 0.08,
            Frequency = 880,
            Type = SignalGeneratorType.Sin,
        };
        var sample = new OffsetSampleProvider(signal)
        {
            TakeSamples = (int)(48000 * 2 * (milliseconds / 1000.0)),
        };
        var source = sample.ToWaveProvider();

        waveOut.Init(source);
        waveOut.Play();
        Thread.Sleep(milliseconds + 120);
        waveOut.Stop();
    }

    public void RunInteractive()
    {
        var captureDevices = _enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.All).ToList();
        var renderDevices = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.All).ToList();
        if (captureDevices.Count == 0 || renderDevices.Count == 0)
            throw new InvalidOperationException("Не найдены аудио устройства для захвата или воспроизведения.");

        Console.WriteLine("\nДоступные INPUT (микрофоны/recording endpoints, включая disabled/unplugged):");
        PrintDevices(captureDevices);

        Console.WriteLine("\nДоступные OUTPUT (динамики/playback endpoints, включая disabled/unplugged):");
        PrintDevices(renderDevices);

        Console.WriteLine("\nВыбор только по индексам (числа из списка выше).");
        Console.WriteLine("Важно: выбирать нужно устройства со статусом Active.\n");

        Console.WriteLine("Профиль первого чата:");
        var firstMic = SelectDeviceByIndex(captureDevices, "Выберите INPUT индекс для первого чата (микрофон): ");
        var firstOutput = SelectDeviceByIndex(renderDevices, "Выберите OUTPUT индекс для первого чата (куда он слушает): ");

        Console.WriteLine("\nПрофиль второго чата:");
        var secondMic = SelectDeviceByIndex(captureDevices, "Выберите INPUT индекс для второго чата (микрофон): ");
        var secondOutput = SelectDeviceByIndex(renderDevices, "Выберите OUTPUT индекс для второго чата (куда он слушает): ");

        Console.Write("\nВключить локальный мониторинг в наушники (A->YOU и B->YOU)? (y/n): ");
        var enableMonitoring = (Console.ReadLine() ?? string.Empty).Trim().ToLowerInvariant() is "y" or "yes" or "д" or "да";

        MMDevice? monitorRender = null;
        if (enableMonitoring)
        {
            Console.WriteLine("\nЛокальный мониторинг включен.");
            Console.WriteLine("Подсказка: выбери физические наушники/колонки, а не виртуальный кабель.");
            monitorRender = SelectDeviceByIndex(renderDevices, "Выберите OUTPUT индекс для твоего прослушивания: ");
        }

        // Сохраняем профиль, чтобы окно A/B в AudioChat можно было настроить автоматически без повторного выбора.
        NektoAudioRouteProfile.Save(
            firstMic.FriendlyName,
            firstOutput.FriendlyName,
            secondMic.FriendlyName,
            secondOutput.FriendlyName
        );

        // Кросс-маршрутизация: 1-й микрофон в вывод 2-го, 2-й микрофон в вывод 1-го.
        StartLinks(firstMic, secondOutput, secondMic, firstOutput, monitorRender);
        Console.WriteLine(enableMonitoring
            ? "\nГолосовой мост запущен: A->B, B->A, и оба потока в твой монитор.\n"
            : "\nГолосовой мост запущен: A->B и B->A (без локального мониторинга).\n");
    }

    private void StartLinks(
        MMDevice aCapture,
        MMDevice bRender,
        MMDevice bCapture,
        MMDevice aRender,
        MMDevice? monitorRender
    )
    {
        _linkAToB = new AudioBridgeLink(aCapture, bRender, "A->B");
        _linkBToA = new AudioBridgeLink(bCapture, aRender, "B->A");
        _monitorA = monitorRender is null ? null : new AudioBridgeLink(aCapture, monitorRender, "A->YOU");
        _monitorB = monitorRender is null ? null : new AudioBridgeLink(bCapture, monitorRender, "B->YOU");

        _linkAToB.Start();
        _linkBToA.Start();
        _monitorA?.Start();
        _monitorB?.Start();
    }

    private static MMDevice FindActiveById(IReadOnlyList<MMDevice> devices, string id)
    {
        var found = devices.FirstOrDefault(d => d.ID == id);
        if (found is null)
            throw new InvalidOperationException($"Устройство не найдено: {id}");
        if (found.State != DeviceState.Active)
            throw new InvalidOperationException($"Устройство не активно: {found.FriendlyName} ({found.State})");
        return found;
    }

    private bool TryAutoConfigure(
        IReadOnlyList<MMDevice> captures,
        IReadOnlyList<MMDevice> renders,
        out AutoConfig config
    )
    {
        config = default;

        var aCapture = FindByTokens(captures, "cable a output", "voicemeeter output", "line 1");
        var bCapture = FindByTokens(captures, "cable b output", "voicemeeter aux output", "line 2");
        var aRender = FindByTokens(renders, "cable a input", "voicemeeter input", "line 1");
        var bRender = FindByTokens(renders, "cable b input", "voicemeeter aux input", "line 2");
        var monitor = TryGetBestMonitorRender(renders);

        if (aCapture is null || bCapture is null || aRender is null || bRender is null || monitor is null)
            return false;

        config = new AutoConfig(aCapture, bRender, bCapture, aRender, monitor);
        return true;
    }

    private MMDevice? TryGetBestMonitorRender(IReadOnlyList<MMDevice> renders)
    {
        MMDevice? defaultRender = null;
        try
        {
            defaultRender = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }
        catch
        {
            // no-op
        }

        if (defaultRender != null && !IsVirtual(defaultRender.FriendlyName))
            return renders.FirstOrDefault(r => r.ID == defaultRender.ID) ?? defaultRender;

        return renders.FirstOrDefault(r => !IsVirtual(r.FriendlyName));
    }

    private static MMDevice? FindByTokens(IReadOnlyList<MMDevice> devices, params string[] tokens)
    {
        foreach (var token in tokens)
        {
            var match = devices.FirstOrDefault(d =>
                d.FriendlyName.Contains(token, StringComparison.OrdinalIgnoreCase)
            );

            if (match != null)
                return match;
        }

        return null;
    }

    private static BridgeDeviceInfo? FindByTokens(IReadOnlyList<BridgeDeviceInfo> devices, params string[] tokens)
    {
        foreach (var token in tokens)
        {
            var match = devices.FirstOrDefault(d =>
                d.IsActive && d.Name.Contains(token, StringComparison.OrdinalIgnoreCase)
            );

            if (!string.IsNullOrWhiteSpace(match.Id))
                return match;
        }

        return null;
    }

    private static bool IsVirtual(string name) =>
        name.Contains("cable", StringComparison.OrdinalIgnoreCase)
        || name.Contains("voicemeeter", StringComparison.OrdinalIgnoreCase)
        || name.Contains("vb-audio", StringComparison.OrdinalIgnoreCase);

    private static void PrintDevices(IReadOnlyList<MMDevice> devices)
    {
        for (var i = 0; i < devices.Count; i++)
        {
            Console.WriteLine($"[{i}] {devices[i].FriendlyName} (state: {devices[i].State})");
        }
    }

    private static MMDevice SelectDeviceByIndex(IReadOnlyList<MMDevice> devices, string prompt)
    {
        while (true)
        {
            Console.Write(prompt);
            var text = (Console.ReadLine() ?? string.Empty).Trim();
            if (int.TryParse(text, out var index) && index >= 0 && index < devices.Count)
            {
                var device = devices[index];
                if (device.State == DeviceState.Active)
                    return device;

                Console.WriteLine($"Устройство {index} не активно (state={device.State}). Выберите Active.");
                continue;
            }

            Console.WriteLine("Неверный индекс. Попробуйте снова.");
        }
    }

    public void Dispose()
    {
        _monitorA?.Dispose();
        _monitorB?.Dispose();
        _linkAToB?.Dispose();
        _linkBToA?.Dispose();
        _enumerator.Dispose();
    }

    private readonly record struct AutoConfig(
        MMDevice ACapture,
        MMDevice BRender,
        MMDevice BCapture,
        MMDevice ARender,
        MMDevice MonitorRender
    );

    public readonly record struct BridgeDeviceInfo(string Id, string Name, bool IsActive, string State);
    public readonly record struct BridgePreset(
        string Name,
        string FirstMicId,
        string FirstOutputId,
        string SecondMicId,
        string SecondOutputId,
        string? MonitorOutputId,
        bool EnableMonitoring,
        DateTimeOffset SavedAt
    );

    private sealed class AudioBridgeLink : IDisposable
    {
        private readonly WasapiCapture _capture;
        private readonly WasapiOut _playback;
        private readonly BufferedWaveProvider _buffer;
        private readonly string _name;

        public AudioBridgeLink(MMDevice captureDevice, MMDevice renderDevice, string name)
        {
            _name = name;

            _capture = new WasapiCapture(captureDevice);
            _buffer = new BufferedWaveProvider(_capture.WaveFormat)
            {
                DiscardOnBufferOverflow = true,
                BufferDuration = TimeSpan.FromMilliseconds(500),
            };

            _playback = new WasapiOut(renderDevice, AudioClientShareMode.Shared, true, 20);
            _playback.Init(_buffer);

            _capture.DataAvailable += (_, args) =>
            {
                _buffer.AddSamples(args.Buffer, 0, args.BytesRecorded);
            };

            _capture.RecordingStopped += (_, args) =>
            {
                if (args.Exception != null)
                    Console.WriteLine($"[{_name}] capture stopped with error: {args.Exception.Message}");
            };
        }

        public void Start()
        {
            Console.WriteLine($"[{_name}] start");
            _playback.Play();
            _capture.StartRecording();
        }

        public void Dispose()
        {
            _capture.StopRecording();
            _playback.Stop();
            _capture.Dispose();
            _playback.Dispose();
        }
    }
}