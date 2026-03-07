namespace NektoMe_MITM_text;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SocketIOClient;
using SocketIOClient.Transport;

public class NektoClient
{
    private readonly SocketIOClient.SocketIO _client;
    private readonly NektoChatManager _manager;
    private readonly CancellationTokenSource _cts = new();
    private readonly Dictionary<string, object> _searchParams;
    private readonly SemaphoreSlim _searchLock = new(1, 1);
    private readonly object _stateLock = new();
    private bool _hasRetriedSearchWithoutFilters;
    private bool _captchaRequired;
    private bool _sendSearchWithoutFilters;
    private bool _retryScheduled;
    private int _retryAttempt;
    private int? _lastErrorCode;
    private DateTimeOffset? _nextRetryAt;
    private string _status = "INIT";

    public string Token { get; }
    public string UserAgent { get; }
    public string Id { get; set; }
    public string DialogId { get; set; }
    public bool CaptchaRequired => _captchaRequired;

    public string StatusLine
    {
        get
        {
            lock (_stateLock)
            {
                var retryAt = _nextRetryAt.HasValue ? _nextRetryAt.Value.ToString("HH:mm:ss") : "-";
                var err = _lastErrorCode.HasValue ? _lastErrorCode.Value.ToString() : "-";
                return $"[{Token[..10]}] status={_status}, fallback={_sendSearchWithoutFilters}, error={err}, retryAt={retryAt}, dialog={(string.IsNullOrEmpty(DialogId) ? "-" : DialogId)}";
            }
        }
    }

    public NektoClient(
        string token,
        string userAgent,
        NektoChatManager manager,
        string sex,
        string wishSex,
        int[] age,
        int[] wishAge,
        bool? role,
        bool? adult,
        string wishRole
    )
    {
        Token = token;
        UserAgent = userAgent;
        _manager = manager;

        _searchParams = new Dictionary<string, object>
        {
            ["wishAge"] = wishAge,
            ["myAge"] = age,
            ["mySex"] = sex,
            ["wishSex"] = wishSex,
            ["adult"] = adult,
            ["role"] = role,
        };

        if (role == true)
        {
            _searchParams["myAge"] = wishRole == "suggest" ? new[] { 30, 40 } : new[] { 10, 20 };
        }

        _client = new SocketIOClient.SocketIO(
            "wss://im.nekto.me",
            new SocketIOOptions
            {
                Transport = TransportProtocol.WebSocket,
                ExtraHeaders = new Dictionary<string, string> { ["User-Agent"] = UserAgent },
            }
        );

        _client.OnConnected += async (s, e) => await OnConnected();
        _client.OnDisconnected += async (s, r) => await OnDisconnected(r);
        _client.On("notice", OnNotice);
    }

    private async Task OnConnected()
    {
        UpdateStatus("CONNECTED");
        Console.WriteLine($"[{Token[..10]}] Connected!");
        await _client.EmitAsync(
            "action",
            new
            {
                action = "auth.sendToken",
                token = Token,
                locale = "ru",
                t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                timeZone = "Europe/Kiev",
                version = 12,
            }
        );
    }

    private async Task OnDisconnected(string reason)
    {
        Console.WriteLine($"[{Token[..10]}] Disconnected: {reason}");
        Id = null;
        DialogId = null;
        UpdateStatus("DISCONNECTED", reason);
    }

    private async void OnNotice(SocketIOResponse response)
    {
        try
        {
            JsonElement data;
            try
            {
                data = response.GetValue<JsonElement[]>().FirstOrDefault();
            }
            catch
            {
                data = response.GetValue<JsonElement>();
            }

            var notice = data.TryGetProperty("notice", out var n) ? GetStringValue(n) : null;
            var hasData = data.TryGetProperty("data", out var dataElement);

            switch (notice)
            {
                case "auth.successToken" when hasData:
                    await HandleAuthSuccess(dataElement);
                    break;
                case "messages.new" when hasData:
                    await _manager.OnMessageAsync(dataElement, this);
                    break;
                case "dialog.opened" when hasData:
                    DialogId = dataElement.TryGetProperty("id", out var i)
                        ? GetStringValue(i)
                        : null;
                    ResetRetryState();
                    UpdateStatus("DIALOG_OPENED");
                    await _manager.OnDialogOpenedAsync(dataElement, this);
                    break;
                case "dialog.closed" when hasData:
                    DialogId = null;
                    UpdateStatus("DIALOG_CLOSED");
                    await _manager.OnDialogClosedAsync(dataElement, this);
                    break;
                case "dialog.typing" when hasData:
                    await _manager.OnTypingAsync(dataElement, this);
                    break;
                case "search.out":
                    UpdateStatus("SEARCH_OUT");
                    Console.WriteLine($"[{Token[..10]}] Search completed");
                    break;
                case "error.code":
                    await HandleServerErrorAsync(dataElement);
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{Token[..10]}] Error: {ex.Message}");
        }
    }

    private async Task HandleAuthSuccess(JsonElement data)
    {
        UpdateStatus("AUTH_OK");
        Id = data.TryGetProperty("id", out var i) ? GetStringValue(i) : null;

        if (
            data.TryGetProperty("statusInfo", out var si)
            && si.TryGetProperty("anonDialogId", out var di)
        )
        {
            DialogId = GetStringValue(di);
        }

        await _client.EmitAsync(
            "action",
            new
            {
                type = "web-agent",
                data = Convert.ToBase64String(
                    System.Security.Cryptography.SHA256.HashData(
                        Encoding.UTF8.GetBytes(
                            $"{Token}{Id}{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
                        )
                    )
                )[..16],
            }
        );

        await _manager.OnAuthAsync(data, this);
    }

    public async Task SearchAsync()
    {
        if (_captchaRequired)
        {
            UpdateStatus("CAPTCHA_REQUIRED");
            Console.WriteLine($"[{Token[..10]}] Поиск остановлен: требуется капча.");
            return;
        }

        await _searchLock.WaitAsync(_cts.Token);
        try
        {
            var includeFilters = !_sendSearchWithoutFilters;
            UpdateStatus(includeFilters ? "SEARCHING" : "SEARCHING_FALLBACK");
            await _client.EmitAsync("action", BuildSearchPayload(includeFilters));
        }
        finally
        {
            _searchLock.Release();
        }
    }

    private Dictionary<string, object> BuildSearchPayload(bool includeFilters)
    {
        var payload = new Dictionary<string, object> { ["action"] = "search.run" };
        if (includeFilters)
        {
            foreach (var p in _searchParams.Where(p => p.Value != null))
                payload[p.Key] = p.Value;
        }

        return payload;
    }

    private async Task HandleServerErrorAsync(JsonElement data)
    {
        Console.WriteLine($"[{Token[..10]}] Error: {data}");

        if (!data.TryGetProperty("id", out var idElement))
        {
            _ = ScheduleRetryAsync("unknown error");
            return;
        }

        var id = idElement.ValueKind == JsonValueKind.Number ? idElement.GetInt32() : -1;
        lock (_stateLock)
        {
            _lastErrorCode = id;
        }

        if (id == 400 && !_hasRetriedSearchWithoutFilters)
        {
            _hasRetriedSearchWithoutFilters = true;
            _sendSearchWithoutFilters = true;
            UpdateStatus("WRONG_DATA_FALLBACK");
            Console.WriteLine($"[{Token[..10]}] Сервер отклонил фильтры, пробую поиск без фильтров...");
            await _client.EmitAsync("action", BuildSearchPayload(includeFilters: false));
            return;
        }

        if (id == 400)
        {
            _ = ScheduleRetryAsync("wrong data (fallback mode)");
            return;
        }

        if (id == 600)
        {
            _captchaRequired = true;
            UpdateStatus("CAPTCHA_REQUIRED");
            Console.WriteLine($"[{Token[..10]}] Требуется решить капчу на сайте для этого токена.");
            string publicKey = null;
            if (
                data.TryGetProperty("additional", out var additional)
                && additional.TryGetProperty("publicKey", out var publicKeyElement)
            )
            {
                publicKey = GetStringValue(publicKeyElement);
            }

            await _manager.OnCaptchaRequiredAsync(this, publicKey);
            return;
        }

        _ = ScheduleRetryAsync($"server error {id}");
    }

    private async Task ScheduleRetryAsync(string reason)
    {
        if (_captchaRequired || _cts.IsCancellationRequested)
            return;

        lock (_stateLock)
        {
            if (_retryScheduled)
                return;
            _retryScheduled = true;
        }

        try
        {
            var attempt = Interlocked.Increment(ref _retryAttempt);
            var delaySeconds = Math.Min(60, 3 * (int)Math.Pow(2, Math.Min(attempt - 1, 5)));
            var delay = TimeSpan.FromSeconds(delaySeconds);

            lock (_stateLock)
            {
                _nextRetryAt = DateTimeOffset.Now.Add(delay);
            }

            UpdateStatus("RETRY_WAIT", $"{reason}, {delaySeconds}s");
            await Task.Delay(delay, _cts.Token);
            lock (_stateLock)
            {
                _nextRetryAt = null;
            }

            await SearchAsync();
        }
        catch (OperationCanceledException)
        {
            UpdateStatus("STOPPED");
        }
        finally
        {
            lock (_stateLock)
            {
                _retryScheduled = false;
            }
        }
    }

    private void ResetRetryState()
    {
        lock (_stateLock)
        {
            _retryAttempt = 0;
            _nextRetryAt = null;
            _lastErrorCode = null;
            _retryScheduled = false;
        }
    }

    private void UpdateStatus(string status, string details = null)
    {
        lock (_stateLock)
        {
            _status = status;
        }

        if (!string.IsNullOrEmpty(details))
            Console.WriteLine($"[{Token[..10]}] status={status}: {details}");
    }

    public async Task EmitAsync(string eventName, object data) =>
        await _client.EmitAsync(eventName, data);

    public async Task ConnectAsync() => await _client.ConnectAsync();

    public async Task WaitAsync()
    {
        try
        {
            await Task.Delay(-1, _cts.Token);
        }
        catch (OperationCanceledException) { }
    }

    public void Disconnect()
    {
        _cts.Cancel();
        UpdateStatus("STOPPED");
        _client.DisconnectAsync();
    }

    private static string GetStringValue(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => element.ToString(),
        };
}
