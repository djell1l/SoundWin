using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using SoundWin.Core.Models;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace SoundWin.Core.Services;

public enum RepeatMode
{
    Off,
    All,
    One
}

public partial class AudioPlayerService : ObservableObject
{
    private readonly MediaPlayer _player;
    private readonly SystemMediaTransportControls _smtc;
    private readonly SoundCloudApiClient _apiClient;
    private readonly Timer _positionTimer;

    [ObservableProperty]
    private Track? _currentTrack;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _isBuffering;

    [ObservableProperty]
    private TimeSpan _position = TimeSpan.Zero;

    [ObservableProperty]
    private TimeSpan _duration = TimeSpan.Zero;

    [ObservableProperty]
    private double _progress; // 0.0 to 1.0

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

    public ObservableCollection<Track> Queue { get; } = new();
    public int CurrentIndex { get; private set; } = -1;

    public event EventHandler<Track?>? TrackChanged;
    public event EventHandler<bool>? PlaybackStateChanged;

    public AudioPlayerService(SoundCloudApiClient apiClient)
    {
        _apiClient = apiClient;

        _player = new MediaPlayer
        {
            AudioCategory = MediaPlayerAudioCategory.Media
        };

        _smtc = _player.SystemMediaTransportControls;
        _smtc.IsEnabled = true;
        _smtc.IsPlayEnabled = true;
        _smtc.IsPauseEnabled = true;
        _smtc.IsNextEnabled = true;
        _smtc.IsPreviousEnabled = true;
        _smtc.IsStopEnabled = true;

        _smtc.ButtonPressed += OnSmtcButtonPressed;

        _player.PlaybackSession.PlaybackStateChanged += OnPlaybackStateChanged;
        _player.MediaEnded += OnMediaEnded;
        _player.MediaFailed += OnMediaFailed;

        // Position update timer (updates progress every 100ms)
        _positionTimer = new Timer(OnTimerTick, null, Timeout.Infinite, Timeout.Infinite);
    }

    public async Task PlayTrackAsync(Track track, ObservableCollection<Track>? newQueue = null, int queueIndex = -1)
    {
        try
        {
            if (newQueue != null && newQueue != Queue)
            {
                Queue.Clear();
                foreach (var t in newQueue) Queue.Add(t);
                CurrentIndex = queueIndex >= 0 ? queueIndex : Queue.IndexOf(track);
            }
            else if (track != null)
            {
                var idx = Queue.IndexOf(track);
                if (idx >= 0)
                {
                    CurrentIndex = idx;
                }
                else
                {
                    Queue.Add(track);
                    CurrentIndex = Queue.Count - 1;
                }
            }

            if (CurrentTrack != null)
            {
                CurrentTrack.IsPlaying = false;
            }

            CurrentTrack = track;
            if (CurrentTrack != null)
            {
                CurrentTrack.IsPlaying = true;
            }

            IsBuffering = true;
            Position = TimeSpan.Zero;
            Progress = 0;
            Duration = track?.Duration ?? TimeSpan.Zero;

            TrackChanged?.Invoke(this, CurrentTrack);
            UpdateSmtcDisplay(track);

            // Fetch stream CDN URL
            var streamUrl = await _apiClient.ResolveStreamUrlAsync(track!);
            Debug.WriteLine($"[AudioPlayerService] Playing stream URL: {streamUrl}");

            var source = MediaSource.CreateFromUri(new Uri(streamUrl));
            _player.Source = source;
            _player.Play();

            _positionTimer.Change(0, 100);

            // Fetch waveform asynchronously
            _ = Task.Run(async () =>
            {
                try
                {
                    var wf = await _apiClient.GetWaveformDataAsync(track?.WaveformUrl);
                    if (wf != null && CurrentTrack?.Id == track?.Id)
                    {
                        CurrentWaveform = wf;
                    }
                }
                catch { }
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AudioPlayerService] Error playing track: {ex.Message}");
            IsBuffering = false;
        }
    }

    public void Play()
    {
        _player.Play();
        _positionTimer.Change(0, 100);
    }

    public void Pause()
    {
        _player.Pause();
        _positionTimer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    public void TogglePlayPause()
    {
        if (IsPlaying) Pause();
        else Play();
    }

    public void Seek(TimeSpan target)
    {
        if (_player.PlaybackSession.CanSeek)
        {
            _player.PlaybackSession.Position = target;
            Position = target;
            if (Duration.TotalSeconds > 0)
            {
                Progress = Math.Clamp(target.TotalSeconds / Duration.TotalSeconds, 0, 1);
            }
            UpdateSmtcTimeline();
        }
    }

    public void SeekProgress(double ratio)
    {
        ratio = Math.Clamp(ratio, 0.0, 1.0);
        if (Duration.TotalSeconds > 0)
        {
            var target = TimeSpan.FromSeconds(Duration.TotalSeconds * ratio);
            Seek(target);
        }
    }

    public async Task NextAsync()
    {
        if (Queue.Count == 0) return;

        int nextIndex;
        if (IsShuffle)
        {
            var rnd = new Random();
            nextIndex = rnd.Next(0, Queue.Count);
        }
        else
        {
            nextIndex = CurrentIndex + 1;
            if (nextIndex >= Queue.Count)
            {
                if (RepeatMode == RepeatMode.All) nextIndex = 0;
                else return;
            }
        }

        if (nextIndex >= 0 && nextIndex < Queue.Count)
        {
            CurrentIndex = nextIndex;
            await PlayTrackAsync(Queue[CurrentIndex]);
        }
    }

    public async Task PreviousAsync()
    {
        if (Position.TotalSeconds > 3)
        {
            Seek(TimeSpan.Zero);
            return;
        }

        if (Queue.Count == 0) return;

        var prevIndex = CurrentIndex - 1;
        if (prevIndex < 0)
        {
            if (RepeatMode == RepeatMode.All) prevIndex = Queue.Count - 1;
            else prevIndex = 0;
        }

        if (prevIndex >= 0 && prevIndex < Queue.Count)
        {
            CurrentIndex = prevIndex;
            await PlayTrackAsync(Queue[CurrentIndex]);
        }
    }

    public void AddToQueue(Track track)
    {
        Queue.Add(track);
    }

    public void PlayNext(Track track)
    {
        var targetIndex = CurrentIndex + 1;
        if (targetIndex >= Queue.Count)
        {
            Queue.Add(track);
        }
        else
        {
            Queue.Insert(targetIndex, track);
        }
    }

    partial void OnVolumeChanged(double value)
    {
        _player.Volume = Math.Clamp(value, 0.0, 1.0);
    }

    partial void OnIsMutedChanged(bool value)
    {
        _player.IsMuted = value;
    }

    private void OnTimerTick(object? state)
    {
        var session = _player.PlaybackSession;
        if (session == null) return;

        var pos = session.Position;
        var dur = session.NaturalDuration;

        Position = pos;
        if (dur > TimeSpan.Zero)
        {
            Duration = dur;
            Progress = Math.Clamp(pos.TotalSeconds / dur.TotalSeconds, 0, 1);
        }
    }

    private void OnPlaybackStateChanged(MediaPlaybackSession sender, object args)
    {
        switch (sender.PlaybackState)
        {
            case MediaPlaybackState.Playing:
                IsPlaying = true;
                IsBuffering = false;
                _smtc.PlaybackStatus = MediaPlaybackStatus.Playing;
                break;
            case MediaPlaybackState.Paused:
                IsPlaying = false;
                IsBuffering = false;
                _smtc.PlaybackStatus = MediaPlaybackStatus.Paused;
                break;
            case MediaPlaybackState.Buffering:
            case MediaPlaybackState.Opening:
                IsBuffering = true;
                break;
            case MediaPlaybackState.None:
                IsPlaying = false;
                IsBuffering = false;
                _smtc.PlaybackStatus = MediaPlaybackStatus.Stopped;
                break;
        }

        PlaybackStateChanged?.Invoke(this, IsPlaying);
        UpdateSmtcTimeline();
    }

    private async void OnMediaEnded(MediaPlayer sender, object args)
    {
        if (RepeatMode == RepeatMode.One && CurrentTrack != null)
        {
            await PlayTrackAsync(CurrentTrack);
        }
        else
        {
            await NextAsync();
        }
    }

    private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        Debug.WriteLine($"[AudioPlayerService] Media Failed: {args.Error} - {args.ErrorMessage}");
        IsBuffering = false;
        IsPlaying = false;
    }

    private async void OnSmtcButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        switch (args.Button)
        {
            case SystemMediaTransportControlsButton.Play:
                Play();
                break;
            case SystemMediaTransportControlsButton.Pause:
                Pause();
                break;
            case SystemMediaTransportControlsButton.Next:
                await NextAsync();
                break;
            case SystemMediaTransportControlsButton.Previous:
                await PreviousAsync();
                break;
            case SystemMediaTransportControlsButton.Stop:
                Pause();
                Seek(TimeSpan.Zero);
                break;
        }
    }

    private void UpdateSmtcDisplay(Track? track)
    {
        if (track == null) return;

        var updater = _smtc.DisplayUpdater;
        updater.Type = MediaPlaybackType.Music;
        updater.MusicProperties.Title = track.Title;
        updater.MusicProperties.Artist = track.Artist;
        updater.MusicProperties.AlbumArtist = track.Artist;

        if (!string.IsNullOrEmpty(track.ArtworkUrl))
        {
            try
            {
                updater.Thumbnail = RandomAccessStreamReference.CreateFromUri(new Uri(track.HighResArtworkUrl));
            }
            catch { }
        }

        updater.Update();
        UpdateSmtcTimeline();
    }

    private void UpdateSmtcTimeline()
    {
        try
        {
            var timeline = new SystemMediaTransportControlsTimelineProperties
            {
                StartTime = TimeSpan.Zero,
                MinSeekTime = TimeSpan.Zero,
                Position = Position,
                MaxSeekTime = Duration,
                EndTime = Duration
            };
            _smtc.UpdateTimelineProperties(timeline);
        }
        catch { }
    }
}
