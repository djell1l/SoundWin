using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SoundWin.Core.Models;

public partial class Playlist : ObservableObject
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ArtworkUrl { get; set; }
    public string? CalculatedArtworkUrl { get; set; }
    public int TrackCount { get; set; }
    public long LikesCount { get; set; }
    public string Author { get; set; } = string.Empty;
    public long AuthorId { get; set; }
    public bool IsSystemPlaylist { get; set; }
    public string? PermalinkUrl { get; set; }
    public List<Track> Tracks { get; set; } = new();
    public List<long> TrackIds { get; set; } = new();

    [ObservableProperty]
    private bool _isLiked;

    public string DisplayArtworkUrl
    {
        get
        {
            var url = !string.IsNullOrEmpty(ArtworkUrl) ? ArtworkUrl : CalculatedArtworkUrl;
            if (string.IsNullOrEmpty(url)) return "ms-appx:///Assets/DefaultCover.png";
            return url.Replace("-large.", "-t500x500.");
        }
    }
}
