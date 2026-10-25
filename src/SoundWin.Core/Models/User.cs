using CommunityToolkit.Mvvm.ComponentModel;

namespace SoundWin.Core.Models;

public partial class User : ObservableObject
{
    public long Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string? AvatarUrl { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public int FollowersCount { get; set; }
    public int FollowingsCount { get; set; }
    public int TrackCount { get; set; }
    public int LikesCount { get; set; }
    public int PlaylistCount { get; set; }
    public string? Description { get; set; }
    public string? PermalinkUrl { get; set; }

    public string HighResAvatarUrl
    {
        get
        {
            if (string.IsNullOrEmpty(AvatarUrl)) return "ms-appx:///Assets/DefaultAvatar.png";
            return AvatarUrl.Replace("-large.", "-t500x500.");
        }
    }
}
