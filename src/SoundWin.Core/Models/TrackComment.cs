using System;

namespace SoundWin.Core.Models;

public class TrackComment
{
    public long Id { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public TimeSpan Timestamp { get; set; }
    public double TimestampMs { get; set; }
    public long TrackId { get; set; }
    public long UserId { get; set; }
    public string Username { get; set; } = "Anonyme";
    public string? AvatarUrl { get; set; }

    public string FormattedTimestamp => Timestamp.TotalHours >= 1
        ? Timestamp.ToString(@"h\:mm\:ss")
        : Timestamp.ToString(@"m\:ss");

    public string HighResAvatarUrl
    {
        get
        {
            if (string.IsNullOrEmpty(AvatarUrl)) return "https://a-v2.sndcdn.com/assets/images/sc-icons/ios-a62dfc8f.png";
            return AvatarUrl.Replace("-large.", "-t500x500.");
        }
    }
}