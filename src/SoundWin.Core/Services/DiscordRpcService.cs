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
    private const string SoundWinDiscordClientId = "1296534887309971556"; // Dedicated Discord Client ID or generic music app ID
    private NamedPipeClientStream? _pipe;
    private readonly CancellationTokenSource _cts = new();
    private bool _isConnected;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public bool IsConnected => _isConnected;

    public Task StartAsync()
    {
        _ = Task.Run(async () =>
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                if (!_isConnected)
                {
                    await TryConnectAsync();
                }
                await Task.Delay(5000, _cts.Token).ConfigureAwait(false);
            }
        }, _cts.Token);

        return Task.CompletedTask;
    }

    private async Task TryConnectAsync()
    {
        for (int i = 0; i < 10; i++)
        {
            try
            {
                var pipeName = $"discord-ipc-{i}";
                var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(500).ConfigureAwait(false);

                _pipe = pipe;
                _isConnected = true;
                Debug.WriteLine($"[DiscordRpcService] Connected to Discord pipe: {pipeName}");

                // Send handshake
                var handshake = JsonSerializer.Serialize(new
                {
                    v = 1,
                    client_id = SoundWinDiscordClientId
                });

                await WriteFrameAsync(0, handshake);
                return;
            }
            catch
            {
                // Try next pipe index
            }
        }
        _isConnected = false;
    }

    public async Task UpdatePresenceAsync(Track? track, bool isPlaying, TimeSpan position, TimeSpan duration)
    {
        if (!_isConnected || _pipe == null || track == null)
        {
            if (!_isConnected) await TryConnectAsync();
            if (!_isConnected || _pipe == null) return;
        }

        try
        {
            object activity;
            if (isPlaying && track != null)
            {
                long startEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - (long)position.TotalSeconds;
                long endEpoch = startEpoch + (long)duration.TotalSeconds;

                activity = new
                {
                    details = track.Title.Length > 120 ? track.Title.Substring(0, 117) + "..." : track.Title,
                    state = $"by {track.Artist}".Length > 120 ? $"by {track.Artist}".Substring(0, 117) + "..." : $"by {track.Artist}",
                    timestamps = new
                    {
                        start = startEpoch,
                        end = endEpoch > startEpoch ? endEpoch : (long?)null
                    },
                    assets = new
                    {
                        large_image = !string.IsNullOrEmpty(track.ArtworkUrl) ? track.HighResArtworkUrl : "soundcloud_logo",
                        large_text = track.Title,
                        small_image = "soundcloud_icon",
                        small_text = "SoundWin"
                    },
                    buttons = !string.IsNullOrEmpty(track.PermalinkUrl) ? new[]
                    {
                        new { label = "Écouter sur SoundCloud", url = track.PermalinkUrl }
                    } : null
                };
            }
            else if (track != null)
            {
                activity = new
                {
                    details = track.Title,
                    state = $"En pause • {track.Artist}",
                    assets = new
                    {
                        large_image = !string.IsNullOrEmpty(track.ArtworkUrl) ? track.HighResArtworkUrl : "soundcloud_logo",
                        large_text = track.Title,
                        small_image = "pause",
                        small_text = "En pause"
                    }
                };
            }
            else
            {
                await ClearPresenceAsync();
                return;
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

            await WriteFrameAsync(1, payload);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DiscordRpcService] Update error: {ex.Message}");
            _isConnected = false;
        }
    }

    public async Task ClearPresenceAsync()
    {
        if (!_isConnected || _pipe == null) return;

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

            await WriteFrameAsync(1, payload);
        }
        catch
        {
            _isConnected = false;
        }
    }

    private async Task WriteFrameAsync(int opcode, string json)
    {
        if (_pipe == null || !_pipe.IsConnected) return;

        await _sendLock.WaitAsync();
        try
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            var header = new byte[8];
            BitConverter.GetBytes(opcode).CopyTo(header, 0);
            BitConverter.GetBytes(bytes.Length).CopyTo(header, 4);

            await _pipe.WriteAsync(header, 0, 8);
            await _pipe.WriteAsync(bytes, 0, bytes.Length);
            await _pipe.FlushAsync();
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _pipe?.Dispose();
        _sendLock.Dispose();
    }
}
