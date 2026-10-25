using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SoundWin.Core.Models;

public partial class Track : ObservableObject
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public long ArtistId { get; set; }
    public string? ArtistAvatarUrl { get; set; }
    public TimeSpan Duration { get; set; }
    public long DurationMs { get; set; }
    public string? ArtworkUrl { get; set; }
    public string? WaveformUrl { get; set; }
    public long LikesCount { get; set; }
    public long PlaybackCount { get; set; }
    public string? Genre { get; set; }
    public string? PermalinkUrl { get; set; }
    public string? TrackAuthorization { get; set; }
    public List<TrackTranscoding> Transcodings { get; set; } = new();

    [ObservableProperty]
    private bool _isLiked;

    [ObservableProperty]
    private bool _isPlaying;

    public string HighResArtworkUrl
    {
        get
        {
            if (string.IsNullOrEmpty(ArtworkUrl))
                return "ms-appx:///Assets/DefaultCover.png";

            // SoundCloud provides -large.jpg (100x100) by default.
            // Replacing with -t500x500.jpg gives high-resolution crisp cover art!
            return ArtworkUrl.Replace("-large.", "-t500x500.");
        }
    }

    public string WaveformJsonUrl
    {
        get
        {
            if (string.IsNullOrEmpty(WaveformUrl)) return string.Empty;
            return WaveformUrl.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                ? WaveformUrl.Substring(0, WaveformUrl.Length - 4) + ".json"
                : WaveformUrl;
        }
    }

    public string FormattedDuration => Duration.TotalHours >= 1
        ? Duration.ToString(@"h\:mm\:ss")
        : Duration.ToString(@"m\:ss");
}

public class TrackTranscoding
{
    public string Url { get; set; } = string.Empty;
    public string Protocol { get; set; } = string.Empty; // "hls" or "progressive"
    public string MimeType { get; set; } = string.Empty; // "audio/mp4; codecs=\"mp4a.40.2\"" or "audio/mpeg"
    public string? Preset { get; set; }
    public int Duration { get; set; }
    public bool Snipped { get; set; }
}
