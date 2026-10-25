using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using SoundWin.Core.Models;
using SoundWin.Core.Utils;

namespace SoundWin.Core.Services;

/// <summary>
/// Serveur IPC Named Pipe pour l'écosystème Jay.
/// Permet à Diswin de détecter la lecture SoundWin en direct, d'afficher le mini-lecteur,
/// de piloter la lecture (play/pause/next/prev) et de partager le morceau dans les salons Discord.
/// Pipe: \\.\pipe\Jay_SoundWin_Ecosystem
/// </summary>
public sealed class SoundWinBridgeServer : IDisposable
{
    public const string PipeName = "Jay_SoundWin_Ecosystem";

    private readonly AudioPlayerService _player;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly Timer _progressTimer;

    private NamedPipeServerStream? _serverPipe;
    private StreamWriter? _writer;
    private bool _isRunning;

    public static Action? BringToFrontHandler { get; set; }

    public SoundWinBridgeServer(AudioPlayerService player)
    {
        _player = player;
        _player.TrackChanged += OnPlayerTrackChanged;
        _player.PlaybackStateChanged += OnPlayerPlaybackStateChanged;

        _progressTimer = new Timer(OnProgressTimerTick, null, Timeout.Infinite, Timeout.Infinite);
    }

    public Task StartAsync()
    {
        if (_isRunning) return Task.CompletedTask;
        _isRunning = true;

        _ = Task.Run(async () =>
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    _serverPipe = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    Debug.WriteLine("[SoundWinBridgeServer] En attente de connexion de Diswin...");
                    await _serverPipe.WaitForConnectionAsync(_cts.Token).ConfigureAwait(false);
                    Debug.WriteLine("[SoundWinBridgeServer] Diswin connecté avec succès !");

                    _writer = new StreamWriter(_serverPipe, new UTF8Encoding(false), leaveOpen: true)
                    {
                        AutoFlush = true
                    };

                    // Envoi immédiat de l'état actuel au client Diswin connecté
                    await BroadcastCurrentStateAsync().ConfigureAwait(false);

                    // Active le ticker de progression si la lecture est en cours
                    if (_player.IsPlaying)
                    {
                        _progressTimer.Change(1000, 1000);
                    }

                    // Boucle de lecture des commandes reçues de Diswin
                    using var reader = new StreamReader(_serverPipe, Encoding.UTF8, leaveOpen: true);
                    while (!_cts.Token.IsCancellationRequested && _serverPipe.IsConnected)
                    {
                        var line = await reader.ReadLineAsync(_cts.Token).ConfigureAwait(false);
                        if (line == null) break;

                        await HandleCommandAsync(line).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SoundWinBridgeServer] Erreur pipe : {ex.Message}");
                }
                finally
                {
                    _progressTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    CleanupClient();
                }

                await Task.Delay(500, _cts.Token).ConfigureAwait(false);
            }
        }, _cts.Token);

        return Task.CompletedTask;
    }

    private void CleanupClient()
    {
        try
        {
            _writer?.Dispose();
            _writer = null;

            if (_serverPipe != null)
            {
                if (_serverPipe.IsConnected) _serverPipe.Disconnect();
                _serverPipe.Dispose();
                _serverPipe = null;
            }
        }
        catch { }
    }

    private void OnPlayerTrackChanged(object? sender, Track? track)
    {
        _ = BroadcastCurrentStateAsync();
    }

    private void OnPlayerPlaybackStateChanged(object? sender, bool isPlaying)
    {
        if (isPlaying)
        {
            _progressTimer.Change(1000, 1000);
        }
        else
        {
            _progressTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        _ = BroadcastCurrentStateAsync();
    }

    private void OnProgressTimerTick(object? state)
    {
        if (_player.IsPlaying && _serverPipe != null && _serverPipe.IsConnected)
        {
            _ = BroadcastProgressAsync();
        }
    }

    public async Task BroadcastCurrentStateAsync()
    {
        if (_serverPipe == null || !_serverPipe.IsConnected || _writer == null) return;

        try
        {
            var track = _player.CurrentTrack;
            var payload = new BridgeStateMessage
            {
                Event = "state",
                IsPlaying = _player.IsPlaying,
                TrackId = track?.Id ?? 0,
                Title = track?.Title ?? string.Empty,
                Artist = track?.Artist ?? string.Empty,
                ArtworkUrl = track?.HighResArtworkUrl ?? track?.ArtworkUrl ?? string.Empty,
                PermalinkUrl = track?.PermalinkUrl ?? string.Empty,
                DurationMs = track?.DurationMs ?? (long)_player.Duration.TotalMilliseconds,
                PositionMs = (long)_player.Position.TotalMilliseconds,
                Progress = _player.Progress
            };

            var json = JsonSerializer.Serialize(payload);
            await SendLineAsync(json).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SoundWinBridgeServer] Erreur broadcast state : {ex.Message}");
        }
    }

    private async Task BroadcastProgressAsync()
    {
        if (_serverPipe == null || !_serverPipe.IsConnected || _writer == null) return;

        try
        {
            var payload = new BridgeProgressMessage
            {
                Event = "progress",
                PositionMs = (long)_player.Position.TotalMilliseconds,
                Progress = _player.Progress
            };

            var json = JsonSerializer.Serialize(payload);
            await SendLineAsync(json).ConfigureAwait(false);
        }
        catch { }
    }

    private async Task HandleCommandAsync(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.TryGetProperty("command", out var cmdEl))
            {
                var command = cmdEl.GetString()?.ToLowerInvariant();
                switch (command)
                {
                    case "toggle_play":
                        AppDispatcher.Dispatch(() => _player.TogglePlayPause());
                        break;
                    case "play":
                        AppDispatcher.Dispatch(() => _player.Play());
                        break;
                    case "pause":
                        AppDispatcher.Dispatch(() => _player.Pause());
                        break;
                    case "next":
                        _ = _player.NextAsync();
                        break;
                    case "prev":
                    case "previous":
                        _ = _player.PreviousAsync();
                        break;
                    case "get_state":
                        await BroadcastCurrentStateAsync().ConfigureAwait(false);
                        break;
                    case "focus":
                    case "bring_to_front":
                        AppDispatcher.Dispatch(() => BringToFrontHandler?.Invoke());
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SoundWinBridgeServer] Erreur parsing commande : {ex.Message}");
        }
    }

    private async Task SendLineAsync(string json)
    {
        await _sendLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_writer != null && _serverPipe != null && _serverPipe.IsConnected)
            {
                await _writer.WriteLineAsync(json).ConfigureAwait(false);
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public void Dispose()
    {
        _progressTimer.Dispose();
        _cts.Cancel();
        CleanupClient();
        _sendLock.Dispose();
    }
}

public sealed class BridgeStateMessage
{
    [JsonPropertyName("event")]
    public string Event { get; set; } = "state";

    [JsonPropertyName("isPlaying")]
    public bool IsPlaying { get; set; }

    [JsonPropertyName("trackId")]
    public long TrackId { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("artist")]
    public string Artist { get; set; } = string.Empty;

    [JsonPropertyName("artworkUrl")]
    public string ArtworkUrl { get; set; } = string.Empty;

    [JsonPropertyName("permalinkUrl")]
    public string PermalinkUrl { get; set; } = string.Empty;

    [JsonPropertyName("durationMs")]
    public long DurationMs { get; set; }

    [JsonPropertyName("positionMs")]
    public long PositionMs { get; set; }

    [JsonPropertyName("progress")]
    public double Progress { get; set; }
}

public sealed class BridgeProgressMessage
{
    [JsonPropertyName("event")]
    public string Event { get; set; } = "progress";

    [JsonPropertyName("positionMs")]
    public long PositionMs { get; set; }

    [JsonPropertyName("progress")]
    public double Progress { get; set; }
}
