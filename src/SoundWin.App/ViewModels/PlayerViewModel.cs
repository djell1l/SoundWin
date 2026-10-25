using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoundWin.Core.Models;
using SoundWin.Core.Services;

namespace SoundWin.App.ViewModels;

public partial class PlayerViewModel : ObservableObject
{
    private readonly AudioPlayerService _player;
    private readonly SoundCloudApiClient _apiClient;
    private readonly DiscordRpcService _rpc;
    private readonly CredentialService _credentials;

    [ObservableProperty]
    private Track? _currentTrack;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _isBuffering;

    [ObservableProperty]
    private TimeSpan _position;

    [ObservableProperty]
    private TimeSpan _duration;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private double _volume = 1.0;

    [ObservableProperty]
    private bool _isMuted;

    [ObservableProperty]
    private bool _isShuffle;

    [ObservableProperty]
    private RepeatMode _repeatMode = RepeatMode.Off;

    [ObservableProperty]
    private WaveformData? _currentWaveform;

    public ObservableCollection<Track> Queue => _player.Queue;

    public string FormattedPosition => Position.TotalHours >= 1
        ? Position.ToString(@"h\:mm\:ss")
        : Position.ToString(@"m\:ss");

    public string FormattedDuration => Duration.TotalHours >= 1
        ? Duration.ToString(@"h\:mm\:ss")
        : Duration.ToString(@"m\:ss");

    public PlayerViewModel(AudioPlayerService player, SoundCloudApiClient apiClient, DiscordRpcService rpc, CredentialService credentials)
    {
        _player = player;
        _apiClient = apiClient;
        _rpc = rpc;
        _credentials = credentials;

        _player.TrackChanged += (s, track) =>
        {
            CurrentTrack = track;
            Duration = track?.Duration ?? TimeSpan.Zero;
            _ = _rpc.UpdatePresenceAsync(CurrentTrack, IsPlaying, Position, Duration);
        };

        _player.PlaybackStateChanged += (s, playing) =>
        {
            IsPlaying = playing;
            _ = _rpc.UpdatePresenceAsync(CurrentTrack, IsPlaying, Position, Duration);
        };

        _player.PropertyChanged += (s, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(AudioPlayerService.Position):
                    Position = _player.Position;
                    OnPropertyChanged(nameof(FormattedPosition));
                    break;
                case nameof(AudioPlayerService.Duration):
                    Duration = _player.Duration;
                    OnPropertyChanged(nameof(FormattedDuration));
                    break;
                case nameof(AudioPlayerService.Progress):
                    Progress = _player.Progress;
                    break;
                case nameof(AudioPlayerService.IsBuffering):
                    IsBuffering = _player.IsBuffering;
                    break;
                case nameof(AudioPlayerService.CurrentWaveform):
                    CurrentWaveform = _player.CurrentWaveform;
                    break;
            }
        };
    }

    [RelayCommand]
    public void TogglePlayPause()
    {
        _player.TogglePlayPause();
    }

    [RelayCommand]
    public async Task Next()
    {
        await _player.NextAsync();
    }

    [RelayCommand]
    public async Task Previous()
    {
        await _player.PreviousAsync();
    }

    [RelayCommand]
    public void SeekProgress(double ratio)
    {
        _player.SeekProgress(ratio);
    }

    [RelayCommand]
    public void ToggleShuffle()
    {
        IsShuffle = !IsShuffle;
        _player.IsShuffle = IsShuffle;
    }

    [RelayCommand]
    public void ToggleRepeat()
    {
        RepeatMode = RepeatMode switch
        {
            RepeatMode.Off => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            _ => RepeatMode.Off
        };
        _player.RepeatMode = RepeatMode;
    }

    [RelayCommand]
    public async Task ToggleLike()
    {
        if (CurrentTrack == null) return;

        var session = _credentials.LoadSession();
        if (session == null || !session.IsAuthenticated) return;

        CurrentTrack.IsLiked = !CurrentTrack.IsLiked;
        if (CurrentTrack.IsLiked)
        {
            await _apiClient.LikeTrackAsync(session.UserId, CurrentTrack.Id);
        }
        else
        {
            await _apiClient.UnlikeTrackAsync(session.UserId, CurrentTrack.Id);
        }
    }

    partial void OnVolumeChanged(double value)
    {
        _player.Volume = value;
    }

    partial void OnIsMutedChanged(bool value)
    {
        _player.IsMuted = value;
    }
}
