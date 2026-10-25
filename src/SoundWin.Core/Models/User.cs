using CommunityToolkit.Mvvm.ComponentModel;

namespace SoundWin.Core.Models;

public partial class User : ObservableObject
{
    public long Id { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayFullName))]
    private string _username = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayFullName), nameof(HasFullName))]
    private string? _firstName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayFullName), nameof(HasFullName))]
    private string? _lastName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayFullName), nameof(HasFullName))]
    private string? _fullName;

    [ObservableProperty]
    private string? _permalink;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HighResAvatarUrl))]
    private string? _avatarUrl;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HighResBannerUrl), nameof(HasBannerImage))]
    private string? _bannerUrl;

    [ObservableProperty]
    private string? _city;

    [ObservableProperty]
    private string? _country;

    public int FollowersCount { get; set; }
    public int FollowingsCount { get; set; }
    public int TrackCount { get; set; }
    public int LikesCount { get; set; }
    public int PlaylistCount { get; set; }
    public int RepostsCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FollowButtonText))]
    private bool _isFollowed;

    public string FollowButtonText => IsFollowed ? "Abonné(e)" : "S'abonner";

    [ObservableProperty]
    private string? _description;

    [ObservableProperty]
    private string? _permalinkUrl;

    public string DisplayFullName
    {
        get
        {
            var combined = $"{FirstName} {LastName}".Trim();
            if (!string.IsNullOrEmpty(combined)) return combined;
            return FullName ?? string.Empty;
        }
    }

    public bool HasFullName => !string.IsNullOrWhiteSpace(DisplayFullName);

    public string HighResAvatarUrl
    {
        get
        {
            if (string.IsNullOrEmpty(AvatarUrl)) return "ms-appx:///Assets/AppIcon.png";
            return AvatarUrl.Replace("-large.", "-t500x500.");
        }
    }

    public string? HighResBannerUrl
    {
        get
        {
            if (string.IsNullOrEmpty(BannerUrl)) return null;
            return BannerUrl.Replace("-large.", "-t2480x520.").Replace("-original.", "-t2480x520.");
        }
    }

    public bool HasBannerImage => !string.IsNullOrEmpty(BannerUrl);
}

public class WebProfileLink
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Service { get; set; }
}
