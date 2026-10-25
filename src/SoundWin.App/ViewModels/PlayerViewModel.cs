using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoundWin.Core.Models;
using SoundWin.Core.Services;
using SoundWin.Core.Utils;

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

    [ObservableProperty]
    private ObservableCollection<TrackComment> _currentComments = new();

    public ObservableCollection<Track> Queue => _player.Queue;

    public double DurationMs => Duration.TotalMilliseconds;

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
            AppDispatcher.Dispatch(() =>
            {
                CurrentTrack = track;
                Duration = track?.Duration ?? TimeSpan.Zero;
                OnPropertyChanged(nameof(DurationMs));
                _ = _rpc.UpdatePresenceAsync(CurrentTrack, IsPlaying, Position, Duration);
                _ = LoadCommentsForCurrentTrackAsync();
            });
        };

        _player.PlaybackStateChanged += (s, playing) =>
        {
            AppDispatcher.Dispatch(() =>
            {
                IsPlaying = playing;
                _ = _rpc.UpdatePresenceAsync(CurrentTrack, IsPlaying, Position, Duration);
            });
        };

        _player.PropertyChanged += (s, e) =>
        {
            AppDispatcher.Dispatch(() =>
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
                        OnPropertyChanged(nameof(DurationMs));
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
            });
        };
    }

    public async Task LoadCommentsForCurrentTrackAsync()
    {
        if (CurrentTrack == null)
        {
            CurrentComments.Clear();
            return;
        }

        try
        {
            var comments = await _apiClient.GetTrackCommentsAsync(CurrentTrack.Id, 80);
            AppDispatcher.Dispatch(() =>
            {
                CurrentComments.Clear();
                foreach (var c in comments)
                {
                    CurrentComments.Add(c);
                }
            });
        }
        catch { }
    }

    [RelayCommand]
    public async Task PostComment(string body)
    {
        if (string.IsNullOrWhiteSpace(body) || CurrentTrack == null) return;

        long ts = (long)Position.TotalMilliseconds;
        var comment = await _apiClient.PostTrackCommentAsync(CurrentTrack.Id, body.Trim(), ts);
        if (comment != null)
        {
            CurrentComments.Add(comment);
        }
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
        _ = _rpc.UpdatePresenceAsync(CurrentTrack, IsPlaying, Position, Duration);
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

    [RelayCommand]
    public void AddToQueue(Track? track)
    {
        var target = track ?? CurrentTrack;
        if (target != null)
        {
            _player.AddToQueue(target);
        }
    }

    [RelayCommand]
    public void PlayNext(Track? track)
    {
        var target = track ?? CurrentTrack;
        if (target != null)
        {
            _player.PlayNext(target);
        }
    }

    [RelayCommand]
    public async Task StartTrackStation(Track? track)
    {
        var target = track ?? CurrentTrack;
        if (target == null) return;

        try
        {
            var stationTracks = await _apiClient.GetTrackStationAsync(target.Id);
            if (stationTracks.Count > 0)
            {
                var q = new ObservableCollection<Track>(stationTracks);
                await _player.PlayTrackAsync(stationTracks[0], q, 0);
            }
        }
        catch { }
    }

    [RelayCommand]
    public void CopyTrackLink(Track? track)
    {
        var target = track ?? CurrentTrack;
        if (target != null && !string.IsNullOrEmpty(target.PermalinkUrl))
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(target.PermalinkUrl);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        }
    }
}