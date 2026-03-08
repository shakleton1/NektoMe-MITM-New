using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NektoMe_MITM_text;

namespace NektoMe_MITM_UI;

public partial class MainWindow : Window
{
    private const string DefaultToken1 = "e17940de41201874ce088da85f23efd742d405450468508c296ba9080ed6dfa4";
    private const string DefaultToken2 = "4555ca935e223b3359a4b7585f5a973e39147c0767114beb1371bffbad5f378f";

    private NektoChatManager? _textManager;
    private Task? _textModeTask;
    private NektoAudioChatManager? _audioManager;
    private NektoVoiceBridge? _voiceBridge;
    private List<NektoVoiceBridge.BridgeDeviceInfo> _bridgeCaptureDevices = new();
    private List<NektoVoiceBridge.BridgeDeviceInfo> _bridgeRenderDevices = new();
    private TextWriter? _oldConsoleOut;
    private TextWriter? _oldConsoleErr;

    public MainWindow()
    {
        InitializeComponent();

        Token1Box.Text = DefaultToken1;
        Token2Box.Text = DefaultToken2;
        AppendLog("UI запущен. Выберите браузер и режим.");
        RefreshBridgeDevices();
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        RootContent.Opacity = 1;

        _oldConsoleOut = Console.Out;
        _oldConsoleErr = Console.Error;
        var writer = new UiLogTextWriter(AppendLog);
        Console.SetOut(writer);
        Console.SetError(writer);
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        try
        {
            _textManager?.Stop();
        }
        catch
        {
            // Ignore shutdown errors.
        }

        try
        {
            _audioManager?.Dispose();
        }
        catch
        {
            // Ignore shutdown errors.
        }

        try
        {
            _voiceBridge?.Dispose();
        }
        catch
        {
            // Ignore shutdown errors.
        }

        if (_oldConsoleOut is not null)
            Console.SetOut(_oldConsoleOut);
        if (_oldConsoleErr is not null)
            Console.SetError(_oldConsoleErr);
    }

    private async void OnStartTextClick(object? sender, RoutedEventArgs e)
    {
        if (_textModeTask is not null && !_textModeTask.IsCompleted)
        {
            AppendLog("Текстовый MITM уже запущен.");
            return;
        }

        var token1 = (Token1Box.Text ?? string.Empty).Trim();
        var token2 = (Token2Box.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(token1) || string.IsNullOrWhiteSpace(token2))
        {
            AppendLog("Запуск отменен: оба токена обязательны.");
            return;
        }

        var browser = GetSelectedBrowser();
        TextModeState.Text = "Состояние: запуск...";
        AppendLog($"Старт текстового MITM ({browser}).");

        if (OpenViewerCheckBox.IsChecked == true)
        {
            NektoCaptchaBrowser.OpenTextChatViewer(token1, "Client 1", browser);
            NektoCaptchaBrowser.OpenTextChatViewer(token2, "Client 2", browser);
        }

        _textManager = new NektoChatManager(browser, autoOpenCaptchaBrowser: false);
        _textManager.AddMember(
            token1,
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36",
            "M",
            "F",
            new[] { 0, 17 },
            new[] { 0, 17 }
        );

        _textManager.AddMember(
            token2,
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36",
            "F",
            "M",
            new[] { 0, 17 },
            new[] { 0, 17 }
        );

        _textModeTask = Task.Run(async () =>
        {
            try
            {
                await _textManager.StartAsync();
            }
            catch (Exception ex)
            {
                AppendLog($"Ошибка текстового режима: {ex.Message}");
            }
        });

        TextModeState.Text = "Состояние: запущен";
        await Task.Delay(1);
    }

    private void OnStopTextClick(object? sender, RoutedEventArgs e)
    {
        if (_textManager is null)
        {
            AppendLog("Текстовый MITM уже остановлен.");
            return;
        }

        _textManager.Stop();
        _textManager = null;
        TextModeState.Text = "Состояние: остановлен";
        AppendLog("Текстовый MITM остановлен.");
    }

    private void OnOpenAudioClick(object? sender, RoutedEventArgs e)
    {
        var browser = GetSelectedBrowser();

        try
        {
            _audioManager?.Dispose();
            _audioManager = new NektoAudioChatManager(browser);
            _audioManager.OpenWindowsOnly();

            AudioModeState.Text = "Состояние: 2 окна открыты";
            AppendLog($"AudioChat окна открыты в {browser}. Войдите в оба аккаунта.");
        }
        catch (Exception ex)
        {
            AppendLog($"Не удалось открыть audiochat окна: {ex.Message}");
        }
    }

    private void OnCloseAudioClick(object? sender, RoutedEventArgs e)
    {
        _audioManager?.Dispose();
        _audioManager = null;
        AudioModeState.Text = "Состояние: окна не открыты";
        AppendLog("AudioChat окна закрыты.");
    }

    private void OnRefreshBridgeDevicesClick(object? sender, RoutedEventArgs e)
    {
        RefreshBridgeDevices();
        AppendLog("Список аудио устройств моста обновлен.");
    }

    private void OnStartBridgeClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var firstMic = GetSelectedBridgeDeviceId(BridgeFirstMicComboBox);
            var firstOut = GetSelectedBridgeDeviceId(BridgeFirstOutputComboBox);
            var secondMic = GetSelectedBridgeDeviceId(BridgeSecondMicComboBox);
            var secondOut = GetSelectedBridgeDeviceId(BridgeSecondOutputComboBox);
            var monitoring = BridgeMonitoringCheckBox.IsChecked == true;
            var monitorOut = monitoring ? GetSelectedBridgeDeviceId(BridgeMonitorOutputComboBox) : null;

            _voiceBridge?.Dispose();
            _voiceBridge = new NektoVoiceBridge();
            _voiceBridge.StartManual(firstMic, firstOut, secondMic, secondOut, monitoring, monitorOut);

            BridgeModeState.Text = monitoring
                ? "Состояние: мост активен (с мониторингом)"
                : "Состояние: мост активен";
            AppendLog("Аудио мост запущен из UI.");
        }
        catch (Exception ex)
        {
            AppendLog($"Не удалось запустить мост: {ex.Message}");
        }
    }

    private void OnStopBridgeClick(object? sender, RoutedEventArgs e)
    {
        _voiceBridge?.Dispose();
        _voiceBridge = null;
        BridgeModeState.Text = "Состояние: мост остановлен";
        AppendLog("Аудио мост остановлен.");
    }

    private BrowserKind GetSelectedBrowser()
    {
        return BrowserComboBox.SelectedIndex == 1 ? BrowserKind.Brave : BrowserKind.Chrome;
    }

    private void RefreshBridgeDevices()
    {
        using var probe = new NektoVoiceBridge();

        _bridgeCaptureDevices = probe.GetCaptureDevices().ToList();
        _bridgeRenderDevices = probe.GetRenderDevices().ToList();

        BridgeFirstMicComboBox.ItemsSource = _bridgeCaptureDevices.Select(FormatBridgeDevice).ToList();
        BridgeSecondMicComboBox.ItemsSource = _bridgeCaptureDevices.Select(FormatBridgeDevice).ToList();
        BridgeFirstOutputComboBox.ItemsSource = _bridgeRenderDevices.Select(FormatBridgeDevice).ToList();
        BridgeSecondOutputComboBox.ItemsSource = _bridgeRenderDevices.Select(FormatBridgeDevice).ToList();
        BridgeMonitorOutputComboBox.ItemsSource = _bridgeRenderDevices.Select(FormatBridgeDevice).ToList();

        BridgeFirstMicComboBox.SelectedIndex = GetFirstActiveIndex(_bridgeCaptureDevices);
        BridgeSecondMicComboBox.SelectedIndex = GetFirstActiveIndex(_bridgeCaptureDevices, skipIndex: BridgeFirstMicComboBox.SelectedIndex);
        BridgeFirstOutputComboBox.SelectedIndex = GetFirstActiveIndex(_bridgeRenderDevices);
        BridgeSecondOutputComboBox.SelectedIndex = GetFirstActiveIndex(_bridgeRenderDevices, skipIndex: BridgeFirstOutputComboBox.SelectedIndex);
        BridgeMonitorOutputComboBox.SelectedIndex = GetFirstPhysicalRenderIndex(_bridgeRenderDevices);
    }

    private static string FormatBridgeDevice(NektoVoiceBridge.BridgeDeviceInfo d)
    {
        var active = d.IsActive ? "ACTIVE" : d.State.ToUpperInvariant();
        return $"{d.Name} [{active}]";
    }

    private static int GetFirstActiveIndex(IReadOnlyList<NektoVoiceBridge.BridgeDeviceInfo> devices, int skipIndex = -1)
    {
        for (var i = 0; i < devices.Count; i++)
        {
            if (i == skipIndex)
                continue;
            if (devices[i].IsActive)
                return i;
        }

        return devices.Count > 0 ? 0 : -1;
    }

    private static int GetFirstPhysicalRenderIndex(IReadOnlyList<NektoVoiceBridge.BridgeDeviceInfo> devices)
    {
        for (var i = 0; i < devices.Count; i++)
        {
            if (!devices[i].IsActive)
                continue;

            var name = devices[i].Name;
            var isVirtual = name.Contains("cable", StringComparison.OrdinalIgnoreCase)
                || name.Contains("voicemeeter", StringComparison.OrdinalIgnoreCase)
                || name.Contains("vb-audio", StringComparison.OrdinalIgnoreCase);

            if (!isVirtual)
                return i;
        }

        return GetFirstActiveIndex(devices);
    }

    private string GetSelectedBridgeDeviceId(ComboBox comboBox)
    {
        if (comboBox.SelectedIndex < 0)
            throw new InvalidOperationException("Нужно выбрать все устройства для моста.");

        var isCapture = comboBox == BridgeFirstMicComboBox || comboBox == BridgeSecondMicComboBox;
        var source = isCapture ? _bridgeCaptureDevices : _bridgeRenderDevices;
        if (comboBox.SelectedIndex >= source.Count)
            throw new InvalidOperationException("Выбранный индекс устройства вне диапазона.");

        return source[comboBox.SelectedIndex].Id;
    }

    private void AppendLog(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            LogBox.Text = string.IsNullOrEmpty(LogBox.Text)
                ? line
                : $"{LogBox.Text}{Environment.NewLine}{line}";

            LogBox.CaretIndex = LogBox.Text?.Length ?? 0;
        });
    }

    private sealed class UiLogTextWriter(Action<string> onLine) : TextWriter
    {
        private readonly StringBuilder _buffer = new();
        private readonly Action<string> _onLine = onLine;
        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            if (value == '\n')
            {
                FlushBuffer();
                return;
            }

            if (value != '\r')
                _buffer.Append(value);
        }

        public override void WriteLine(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                _onLine(value);
        }

        public override void Flush()
        {
            FlushBuffer();
        }

        private void FlushBuffer()
        {
            if (_buffer.Length == 0)
                return;

            var text = _buffer.ToString();
            _buffer.Clear();
            if (!string.IsNullOrWhiteSpace(text))
                _onLine(text);
        }
    }
}