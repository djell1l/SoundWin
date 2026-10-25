using System.Collections.Generic;

namespace SoundWin.Core.Models;

public class Shelf
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? QueryUrn { get; set; }
    public List<Playlist> Playlists { get; set; } = new();
    public List<Track> Tracks { get; set; } = new();
    public bool HasPlaylists => Playlists.Count > 0;
    public bool HasTracks => Tracks.Count > 0;
}

public class WaveformData
{
    public int Width { get; set; }
    public int Height { get; set; }
    public float[] Samples { get; set; } = System.Array.Empty<float>();
}

public class UserSession
{
    public long UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string? OAuthToken { get; set; }
    public bool IsAuthenticated => !string.IsNullOrEmpty(OAuthToken);
}
