using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace EasyRPC;

public static class EasyRpcEvents
{
    public static event EventHandler<string>? OnReady;
    public static event EventHandler<Exception>? OnError;
    public static event EventHandler<Presence>? OnPresenceUpdate;

    internal static void RaiseOnReady(string userId) => OnReady?.Invoke(null, userId);
    internal static void RaiseOnError(Exception ex) => OnError?.Invoke(null, ex);
    internal static void RaiseOnPresenceUpdate(Presence presence) => OnPresenceUpdate?.Invoke(null, presence);
}

public static class EasyRpc
{
    private const int OpcodeHandshake = 0;
    private const int OpcodeFrame = 1;
    private const int OpcodeClose = 2;
    private const int OpcodePing = 3;
    private const int OpcodePong = 4;

    private static NamedPipeClientStream? _pipe;
    private static bool _isConnected;
    private static readonly SemaphoreSlim _lock = new(1, 1);
    private static System.Timers.Timer? _pingTimer;
    private static bool _isDisposing;
    private static string? _clientId;
    private static Presence? _currentPresence;
    private static DateTime _lastPong = DateTime.UtcNow;
    private static bool _reconnectPending;
    private static readonly SemaphoreSlim _readLock = new(1, 1); // prevent concurrent reads

    public static async Task InitializeAsync(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
            throw new ArgumentException("Client ID is required.", nameof(clientId));

        await _lock.WaitAsync();
        try
        {
            if (_isConnected) return;

            _clientId = clientId;
            _isDisposing = false;
            await ConnectAsync();

            // Ping timer: send a ping every 15 seconds
            _pingTimer = new System.Timers.Timer(15000);
            _pingTimer.Elapsed += async (_, _) => await PingAsync();
            _pingTimer.AutoReset = true;
            _pingTimer.Start();
        }
        finally
        {
            _lock.Release();
        }
    }

    private static async Task ConnectAsync()
    {
        if (_isDisposing) return;

        for (int i = 0; i < 10; i++)
        {
            string pipeName = $"discord-ipc-{i}";
            try
            {
                _pipe?.Dispose();
                _pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
                await _pipe.ConnectAsync(1000);

                var handshake = new { v = 1, client_id = _clientId };
                await SendFrameAsync(OpcodeHandshake, JsonSerializer.Serialize(handshake));

                var response = await ReadFrameAsync();
                if (response.Opcode != OpcodeFrame)
                {
                    await ClosePipeAsync();
                    continue;
                }

                string userId = "unknown";
                try
                {
                    using var doc = JsonDocument.Parse(response.Payload);
                    if (doc.RootElement.TryGetProperty("data", out var data) &&
                        data.TryGetProperty("user", out var user) &&
                        user.TryGetProperty("id", out var id))
                    {
                        userId = id.GetString() ?? "unknown";
                    }
                }
                catch { /* ignore */ }

                _isConnected = true;
                _lastPong = DateTime.UtcNow;
                _reconnectPending = false;
                EasyRpcEvents.RaiseOnReady(userId);
                Console.WriteLine($"EasyRPC connected via {pipeName} as user {userId}");

                if (_currentPresence != null)
                    await SetPresenceAsync(_currentPresence);
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"!!! Pipe {pipeName} failed: {ex.Message}");
            }
        }

        throw new Exception("Could not connect to Discord. Is Discord running?");
    }

    public static async Task SetPresenceAsync(Presence presence)
    {
        if (presence == null) throw new ArgumentNullException(nameof(presence));
        await EnsureConnectionAsync();

        var activity = new Dictionary<string, object>();

        if (!string.IsNullOrEmpty(presence.Details))
            activity["details"] = presence.Details;
        if (!string.IsNullOrEmpty(presence.State))
            activity["state"] = presence.State;

        var assets = new Dictionary<string, object>();
        if (!string.IsNullOrEmpty(presence.LargeImageKey))
            assets["large_image"] = presence.LargeImageKey;
        if (!string.IsNullOrEmpty(presence.LargeImageText))
            assets["large_text"] = presence.LargeImageText;
        if (!string.IsNullOrEmpty(presence.SmallImageKey))
            assets["small_image"] = presence.SmallImageKey;
        if (!string.IsNullOrEmpty(presence.SmallImageText))
            assets["small_text"] = presence.SmallImageText;
        if (assets.Count > 0)
            activity["assets"] = assets;

        var timestamps = new Dictionary<string, object>();
        if (presence.StartTimestamp.HasValue)
            timestamps["start"] = ToUnixSeconds(presence.StartTimestamp.Value);
        if (presence.EndTimestamp.HasValue)
            timestamps["end"] = ToUnixSeconds(presence.EndTimestamp.Value);
        if (timestamps.Count > 0)
            activity["timestamps"] = timestamps;

        if (!string.IsNullOrEmpty(presence.PartyId))
        {
            var party = new Dictionary<string, object> { ["id"] = presence.PartyId };
            if (presence.PartySize.HasValue && presence.PartyMax.HasValue)
                party["size"] = new[] { presence.PartySize.Value, presence.PartyMax.Value };
            activity["party"] = party;
        }

        if (presence.Buttons != null && presence.Buttons.Count > 0)
        {
            var buttonsList = presence.Buttons
                .Take(2)
                .Where(b => !string.IsNullOrEmpty(b.Label) && !string.IsNullOrEmpty(b.Url))
                .Select(b => new Dictionary<string, object> { ["label"] = b.Label!, ["url"] = b.Url! })
                .ToList();
            if (buttonsList.Count > 0)
                activity["buttons"] = buttonsList;
        }

        var payload = new Dictionary<string, object>
        {
            ["cmd"] = "SET_ACTIVITY",
            ["nonce"] = Guid.NewGuid().ToString(),
            ["args"] = new Dictionary<string, object>
            {
                ["pid"] = Environment.ProcessId,
                ["activity"] = activity
            }
        };

        string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        try
        {
            await SendFrameAsync(OpcodeFrame, json);
            // Read the response (this also helps keep the pipe alive)
            var response = await ReadFrameAsync();

            if (response.Opcode == OpcodeFrame)
            {
                try
                {
                    using var doc = JsonDocument.Parse(response.Payload);
                    if (doc.RootElement.TryGetProperty("evt", out var evt) && evt.GetString() == "ERROR")
                    {
                        if (doc.RootElement.TryGetProperty("data", out var data) &&
                            data.TryGetProperty("message", out var msg))
                        {
                            var errorMsg = msg.GetString() ?? "Unknown error";
                            Console.WriteLine($"!!! Discord error: {errorMsg}");
                            EasyRpcEvents.RaiseOnError(new Exception(errorMsg));
                            return;
                        }
                    }
                }
                catch (JsonException) { /* ignore */ }

                _currentPresence = presence;
                EasyRpcEvents.RaiseOnPresenceUpdate(presence);
                Console.WriteLine("Presence updated successfully.");
            }
            else if (response.Opcode == OpcodeClose)
            {
                Console.WriteLine("!!! Discord closed the connection.");
                await ReconnectAsync();
            }
            else
            {
                Console.WriteLine($"!!! Unexpected opcode {response.Opcode}: {response.Payload}");
            }
        }
        catch (IOException ex)
        {
            Console.WriteLine($"!!! I/O error while sending presence: {ex.Message}");
            await ReconnectAsync();
        }
    }

    public static async Task SetPresenceAsync(
        string? details = null,
        string? state = null,
        string? largeImageKey = null,
        string? largeImageText = null,
        string? smallImageKey = null,
        string? smallImageText = null,
        DateTime? startTimestamp = null,
        DateTime? endTimestamp = null,
        string? partyId = null,
        int? partySize = null,
        int? partyMax = null,
        List<Button>? buttons = null)
    {
        var presence = new Presence
        {
            Details = details,
            State = state,
            LargeImageKey = largeImageKey,
            LargeImageText = largeImageText,
            SmallImageKey = smallImageKey,
            SmallImageText = smallImageText,
            StartTimestamp = startTimestamp,
            EndTimestamp = endTimestamp,
            PartyId = partyId,
            PartySize = partySize,
            PartyMax = partyMax,
            Buttons = buttons
        };
        await SetPresenceAsync(presence);
    }

    public static async Task ClearPresenceAsync() => await SetPresenceAsync(new Presence());

    public static async Task ShutdownAsync()
    {
        _isDisposing = true;
        _pingTimer?.Stop();
        _pingTimer?.Dispose();
        _pingTimer = null;

        await _lock.WaitAsync();
        try
        {
            await ClosePipeAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    // ------------------ Private helpers ------------------

    private static async Task EnsureConnectionAsync()
    {
        if (_isConnected) return;
        await ReconnectAsync();
    }

    private static async Task ReconnectAsync()
    {
        if (_isDisposing) return;
        if (_reconnectPending) return;

        _reconnectPending = true;
        await ClosePipeAsync();
        _isConnected = false;

        int delay = 2000;
        while (!_isConnected && !_isDisposing)
        {
            try
            {
                Console.WriteLine($"!!! Attempting reconnect in {delay}ms...");
                await Task.Delay(delay);
                if (_isDisposing) break;

                await _lock.WaitAsync();
                try
                {
                    if (_isConnected) break;
                    await ConnectAsync();
                    if (_isConnected)
                    {
                        _reconnectPending = false;
                        break;
                    }
                }
                finally
                {
                    _lock.Release();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"!!! Reconnect failed: {ex.Message}");
            }

            if (delay < 30000)
                delay = Math.Min(delay * 2, 30000);
        }
    }

    private static async Task PingAsync()
    {
        try
        {
            if (!_isConnected || _pipe == null || !_pipe.IsConnected)
            {
                await ReconnectAsync();
                return;
            }

            // Check if we haven't received a pong in a while (30 seconds)
            if ((DateTime.UtcNow - _lastPong).TotalSeconds > 30)
            {
                Console.WriteLine("!!! Ping timeout – reconnecting...");
                await ReconnectAsync();
                return;
            }

            // Send ping and wait for pong
            await SendFrameAsync(OpcodePing, "{}");
            var response = await ReadFrameAsync();
            if (response.Opcode == OpcodePong)
            {
                _lastPong = DateTime.UtcNow;
                // Console.WriteLine("Pong received");
            }
            else if (response.Opcode == OpcodeClose)
            {
                Console.WriteLine("!!! Discord closed connection during ping.");
                await ReconnectAsync();
            }
        }
        catch (IOException)
        {
            Console.WriteLine("!!! Pipe broken during ping – reconnecting...");
            await ReconnectAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"!!! Ping failed: {ex.Message}");
            await ReconnectAsync();
        }
    }

    private static async Task SendFrameAsync(int opcode, string payload)
    {
        if (_pipe == null || !_pipe.IsConnected)
            throw new InvalidOperationException("Pipe not connected.");

        byte[] data = Encoding.UTF8.GetBytes(payload);
        using var ms = new MemoryStream();
        ms.Write(BitConverter.GetBytes(opcode), 0, 4);
        ms.Write(BitConverter.GetBytes(data.Length), 0, 4);
        ms.Write(data, 0, data.Length);

        await _pipe.WriteAsync(ms.ToArray(), 0, (int)ms.Length);
        await _pipe.FlushAsync();
    }

    private static async Task<(int Opcode, string Payload)> ReadFrameAsync()
    {
        if (_pipe == null || !_pipe.IsConnected)
            throw new InvalidOperationException("Pipe not connected.");

        // Prevent concurrent reads from different threads
        await _readLock.WaitAsync();
        try
        {
            byte[] header = new byte[8];
            int read = 0;
            while (read < 8)
                read += await _pipe.ReadAsync(header.AsMemory(read, 8 - read));

            int opcode = BitConverter.ToInt32(header, 0);
            int length = BitConverter.ToInt32(header, 4);

            byte[] payload = new byte[length];
            read = 0;
            while (read < length)
                read += await _pipe.ReadAsync(payload.AsMemory(read, length - read));

            return (opcode, Encoding.UTF8.GetString(payload));
        }
        finally
        {
            _readLock.Release();
        }
    }

    private static async Task ClosePipeAsync()
    {
        if (_pipe != null)
        {
            try
            {
                if (_pipe.IsConnected)
                {
                    await SendFrameAsync(OpcodeClose, "{}");
                    await Task.Delay(50);
                }
                _pipe.Close();
            }
            catch (IOException) { /* ignore */ }
            catch (ObjectDisposedException) { /* ignore */ }
            await _pipe.DisposeAsync();
            _pipe = null;
        }
        _isConnected = false;
    }

    private static long ToUnixSeconds(DateTime dt) =>
        (long)(dt.ToUniversalTime() - DateTime.UnixEpoch).TotalSeconds;
}
