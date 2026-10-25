using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SoundWin.Core.Models;

namespace SoundWin.Core.Services;

public class DiscordRpcService : IDisposable
{
    private const string SoundCloudFallbackLogo = "https://a-v2.sndcdn.com/assets/images/sc-icons/ios-a62dfc8f.png";

    private readonly SettingsService _settings;
    private NamedPipeClientStream? _pipe;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private bool _isConnected;
    private string _statusMessage = "Non initialisé";

    // Cache of the last reported state for replay on reconnect or setting toggle
    private Track? _lastTrack;
    private bool _lastIsPlaying;
    private TimeSpan _lastPosition;
    private TimeSpan _lastDuration;

    public bool IsConnected => _isConnected;
    public bool IsEnabled => _settings.Current.EnableDiscordRpc;
    public string StatusMessage => _statusMessage;

    public event Action<bool>? ConnectionStatusChanged;

    public DiscordRpcService(SettingsService settings)
    {
        _settings = settings;
        _settings.SettingsChanged += OnSettingsChanged;
    }

    public Task StartAsync()
    {
        if (!IsEnabled)
        {
            _statusMessage = "Désactivé dans les paramètres";
            ConnectionStatusChanged?.Invoke(false);
        }

        _ = Task.Run(async () =>
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                if (IsEnabled && !_isConnected)
                {
                    await TryConnectAsync().ConfigureAwait(false);
                }

                try
                {
                    await Task.Delay(4000, _cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, _cts.Token);

        return Task.CompletedTask;
    }

    private void OnSettingsChanged()
    {
        if (!IsEnabled)
        {
            _statusMessage = "Désactivé dans les paramètres";
            _ = ClearPresenceAndDisconnectAsync();
        }
        else if (!_isConnected)
        {
            _ = TryConnectAsync();
        }
        else
        {
            // Settings changed (e.g., cover art or buttons toggled): re-push presence
            if (_lastTrack != null)
            {
                _ = UpdatePresenceAsync(_lastTrack, _lastIsPlaying, _lastPosition, _lastDuration);
            }
        }
    }

    private async Task ClearPresenceAndDisconnectAsync()
    {
        try
        {
            if (_isConnected && _pipe != null && _pipe.IsConnected)
            {
                await ClearPresenceAsync().ConfigureAwait(false);
            }
        }
        catch { }

        DisconnectInternal();
    }

    private async Task TryConnectAsync()
    {
        if (!IsEnabled) return;

        DisconnectInternal();

        string clientId = !string.IsNullOrWhiteSpace(_settings.Current.DiscordRpcClientId)
            ? _settings.Current.DiscordRpcClientId.Trim()
            : SoundWinSettings.DefaultDiscordClientId;

        for (int i = 0; i < 10; i++)
        {
            NamedPipeClientStream? candidatePipe = null;
            try
            {
                var pipeName = $"discord-ipc-{i}";
                candidatePipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                
                using var timeoutCts = new CancellationTokenSource(600);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, timeoutCts.Token);
                await candidatePipe.ConnectAsync(linkedCts.Token).ConfigureAwait(false);

                _pipe = candidatePipe;
                _statusMessage = $"Connecté au pipe {pipeName}";
                Debug.WriteLine($"[DiscordRpcService] Connected to pipe: {pipeName}");

                // Send Handshake frame (opcode 0)
                var handshakePayload = JsonSerializer.Serialize(new
                {
                    v = 1,
                    client_id = clientId
                });

                await WriteFrameInternalAsync(candidatePipe, 0, handshakePayload).ConfigureAwait(false);

                // Start background reader for this pipe
                _ = Task.Run(() => ReadLoopAsync(candidatePipe, _cts.Token), _cts.Token);
                return;
            }
            catch
            {
                candidatePipe?.Dispose();
            }
        }

        _isConnected = false;
        _statusMessage = "En attente de Discord...";
        ConnectionStatusChanged?.Invoke(false);
    }

    private async Task ReadLoopAsync(NamedPipeClientStream pipe, CancellationToken ct)
    {
        byte[] header = new byte[8];
        try
        {
            while (pipe.IsConnected && !ct.IsCancellationRequested)
            {
                int headerRead = await ReadExactAsync(pipe, header, 8, ct).ConfigureAwait(false);
                if (headerRead < 8) break;

                int op = BitConverter.ToInt32(header, 0);
                int len = BitConverter.ToInt32(header, 4);

                if (len < 0 || len > 2 * 1024 * 1024) break;

                byte[] body = new byte[len];
                int bodyRead = await ReadExactAsync(pipe, body, len, ct).ConfigureAwait(false);
                if (bodyRead < len) break;

                string json = Encoding.UTF8.GetString(body);

                if (op == 1) // FRAME / DISPATCH
                {
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("evt", out var evtEl) && evtEl.GetString() == "READY")
                    {
                        _isConnected = true;
                        _statusMessage = "Connecté à Discord";
                        Debug.WriteLine("[DiscordRpcService] Discord RPC handshake READY received!");
                        ConnectionStatusChanged?.Invoke(true);

                        // If a track was already playing before connection, push it right away!
                        if (_lastTrack != null)
                        {
                            _ = UpdatePresenceAsync(_lastTrack, _lastIsPlaying, _lastPosition, _lastDuration);
                        }
                    }
                    else if (root.TryGetProperty("evt", out var errEvt) && errEvt.GetString() == "ERROR")
                    {
                        _statusMessage = "Erreur Discord RPC";
                        Debug.WriteLine($"[DiscordRpcService] Discord RPC error: {json}");
                    }
                }
                else if (op == 2) // CLOSE
                {
                    Debug.WriteLine("[DiscordRpcService] Discord closed RPC connection.");
                    break;
                }
                else if (op == 3) // PING
                {
                    // Respond with PONG (opcode 4)
                    await WriteFrameInternalAsync(pipe, 4, json).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DiscordRpcService] Read loop ended: {ex.Message}");
        }
        finally
        {
            if (ReferenceEquals(_pipe, pipe))
            {
                DisconnectInternal();
            }
        }
    }

    private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, int count, CancellationToken ct)
    {
        int total = 0;
        while (total < count)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(total, count - total), ct).ConfigureAwait(false);
            if (read == 0) return total;
            total += read;
        }
        return total;
    }

    private void DisconnectInternal()
    {
        var prevConnected = _isConnected;
        _isConnected = false;

        try
        {
            _pipe?.Dispose();
        }
        catch { }
        _pipe = null;

        if (prevConnected)
        {
            _statusMessage = IsEnabled ? "En attente de Discord..." : "Désactivé";
            ConnectionStatusChanged?.Invoke(false);
        }
    }

    public async Task UpdatePresenceAsync(Track? track, bool isPlaying, TimeSpan position, TimeSpan duration)
    {
        _lastTrack = track;
        _lastIsPlaying = isPlaying;
        _lastPosition = position;
        _lastDuration = duration;

        if (!IsEnabled) return;

        if (!_isConnected || _pipe == null)
        {
            await TryConnectAsync().ConfigureAwait(false);
            if (!_isConnected || _pipe == null) return;
        }

        if (track == null)
        {
            await ClearPresenceAsync().ConfigureAwait(false);
            return;
        }

        try
        {
            object? activity = null;

            if (isPlaying)
            {
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                long startEpoch = Math.Max(0, now - (long)position.TotalSeconds);
                long endEpoch = startEpoch + (long)duration.TotalSeconds;

                string? largeImageUrl = null;
                if (_settings.Current.DiscordRpcShowCoverArt)
                {
                    if (!string.IsNullOrEmpty(track.ArtworkUrl) && track.ArtworkUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        largeImageUrl = track.HighResArtworkUrl;
                    }
                    else
                    {
                        largeImageUrl = SoundCloudFallbackLogo;
                    }
                }

                activity = new
                {
                    type = 2, // 2 = Listening to
                    details = Truncate(track.Title, 120),
                    state = Truncate($"par {track.Artist}", 120),
                    timestamps = new
                    {
                        start = startEpoch,
                        end = endEpoch > startEpoch ? endEpoch : (long?)null
                    },
                    assets = new
                    {
                        large_image = largeImageUrl,
                        large_text = Truncate(track.Title, 120),
                        small_image = SoundCloudFallbackLogo,
                        small_text = "SoundWin"
                    },
                    buttons = _settings.Current.DiscordRpcShowButtons && !string.IsNullOrEmpty(track.PermalinkUrl) && track.PermalinkUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                        ? new[]
                        {
                            new { label = "Écouter sur SoundCloud", url = track.PermalinkUrl }
                        }
                        : null
                };
            }
            else if (_settings.Current.DiscordRpcShowPaused)
            {
                string? largeImageUrl = null;
                if (_settings.Current.DiscordRpcShowCoverArt)
                {
                    if (!string.IsNullOrEmpty(track.ArtworkUrl) && track.ArtworkUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        largeImageUrl = track.HighResArtworkUrl;
                    }
                    else
                    {
                        largeImageUrl = SoundCloudFallbackLogo;
                    }
                }

                activity = new
                {
                    type = 2,
                    details = Truncate(track.Title, 120),
                    state = Truncate($"{track.Artist} • En pause", 120),
                    assets = new
                    {
                        large_image = largeImageUrl,
                        large_text = Truncate(track.Title, 120),
                        small_image = SoundCloudFallbackLogo,
                        small_text = "En pause"
                    },
                    buttons = _settings.Current.DiscordRpcShowButtons && !string.IsNullOrEmpty(track.PermalinkUrl) && track.PermalinkUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                        ? new[]
                        {
                            new { label = "Écouter sur SoundCloud", url = track.PermalinkUrl }
                        }
                        : null
                };
            }

            var payload = JsonSerializer.Serialize(new
            {
                cmd = "SET_ACTIVITY",
                args = new
                {
                    pid = Process.GetCurrentProcess().Id,
                    activity
                },
                nonce = Guid.NewGuid().ToString()
            });

            await WriteFrameAsync(1, payload).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DiscordRpcService] Update error: {ex.Message}");
            DisconnectInternal();
        }
    }

    public async Task ClearPresenceAsync()
    {
        if (_pipe == null || !_pipe.IsConnected) return;

        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                cmd = "SET_ACTIVITY",
                args = new
                {
                    pid = Process.GetCurrentProcess().Id,
                    activity = (object?)null
                },
                nonce = Guid.NewGuid().ToString()
            });

            await WriteFrameAsync(1, payload).ConfigureAwait(false);
        }
        catch
        {
            DisconnectInternal();
        }
    }

    private async Task WriteFrameAsync(int opcode, string json)
    {
        if (_pipe == null || !_pipe.IsConnected) return;
        await WriteFrameInternalAsync(_pipe, opcode, json).ConfigureAwait(false);
    }

    private async Task WriteFrameInternalAsync(NamedPipeClientStream pipe, int opcode, string json)
    {
        if (!pipe.IsConnected) return;

        await _sendLock.WaitAsync().ConfigureAwait(false);
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            byte[] header = new byte[8];
            BitConverter.GetBytes(opcode).CopyTo(header, 0);
            BitConverter.GetBytes(bytes.Length).CopyTo(header, 4);

            await pipe.WriteAsync(header, 0, 8).ConfigureAwait(false);
            await pipe.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            await pipe.FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private static string Truncate(string value, int maxLen)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= maxLen ? value : value.Substring(0, maxLen - 3) + "...";
    }

    public void Dispose()
    {
        _settings.SettingsChanged -= OnSettingsChanged;
        _cts.Cancel();
        _cts.Dispose();
        DisconnectInternal();
        _sendLock.Dispose();
    }
}
