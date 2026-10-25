using System;

namespace SoundWin.Core.Models;

public class StreamPost
{
    public string Type { get; set; } = "track"; // "track" or "playlist"
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public User? User { get; set; }
    public Track? Track { get; set; }
    public Playlist? Playlist { get; set; }
    public bool IsRepost { get; set; }

    public string HeaderText
    {
        get
        {
            var author = User?.Username ?? Track?.Artist ?? "Artiste";
            return IsRepost ? $"{author} a reposté" : $"{author} a publié";
        }
    }
}
