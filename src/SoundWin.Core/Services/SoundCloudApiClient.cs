using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SoundCloudExplode;
using SoundWin.Core.Models;

namespace SoundWin.Core.Services;

public class SoundCloudApiClient
{
    private static readonly HttpClient _http = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(15),
        AutomaticDecompression = System.Net.DecompressionMethods.All
    });

    private readonly SoundCloudClient _explodeClient;
    private string? _clientId;
    private string? _oauthToken;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    static SoundCloudApiClient()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/javascript, */*; q=0.01");
    }

    public SoundCloudApiClient()
    {
        _explodeClient = new SoundCloudClient();
    }

    public string? ClientId => _clientId;
    public string? OAuthToken
    {
        get => _oauthToken;
        set => _oauthToken = value;
    }

    public bool IsAuthenticated => !string.IsNullOrEmpty(_oauthToken);

    public async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(_clientId)) return;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrEmpty(_clientId)) return;

            // Strategy 1: Use SoundCloudExplode built-in scraper
            try
            {
                await _explodeClient.InitializeAsync(cancellationToken);
                _clientId = _explodeClient.ClientId;
            }
            catch { }

            // Strategy 2: Direct web asset scraping if Strategy 1 failed
            if (string.IsNullOrEmpty(_clientId))
            {
                _clientId = await ScrapeClientIdDirectAsync(cancellationToken);
            }

            if (string.IsNullOrEmpty(_clientId))
            {
                throw new InvalidOperationException("Failed to obtain a valid SoundCloud Client ID.");
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task<string?> ScrapeClientIdDirectAsync(CancellationToken ct)
    {
        try
        {
            var html = await _http.GetStringAsync("https://soundcloud.com", ct);
            var scriptMatches = Regex.Matches(html, @"<script crossorigin src=""(https:\/\/a-v2\.sndcdn\.com\/assets\/[^""]+\.js)""");
            
            foreach (Match match in scriptMatches)
            {
                var scriptUrl = match.Groups[1].Value;
                var js = await _http.GetStringAsync(scriptUrl, ct);
                var idMatch = Regex.Match(js, @"client_id[:=]""([a-zA-Z0-9]{32})""");
                if (idMatch.Success)
                {
                    return idMatch.Groups[1].Value;
                }
            }
        }
        catch { }

        return null;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url, bool attachAuth = true)
    {
        var request = new HttpRequestMessage(method, url);
        if (attachAuth && !string.IsNullOrEmpty(_oauthToken))
        {
            request.Headers.Add("Authorization", $"OAuth {_oauthToken}");
        }
        return request;
    }

    // ==========================================
    // 1. HOME / CURATED SELECTIONS (Mixed Selections)
    // ==========================================
    public async Task<List<Shelf>> GetHomeMixedSelectionsAsync(int limit = 10, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var url = $"https://api-v2.soundcloud.com/mixed-selections?client_id={_clientId}&limit={limit}";
        using var request = CreateRequest(HttpMethod.Get, url);
        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var shelves = new List<Shelf>();
        if (!doc.RootElement.TryGetProperty("collection", out var col)) return shelves;

        foreach (var shelfEl in col.EnumerateArray())
        {
            var shelf = new Shelf
            {
                Title = shelfEl.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                Description = shelfEl.TryGetProperty("description", out var d) ? d.GetString() : null,
                QueryUrn = shelfEl.TryGetProperty("query_urn", out var q) ? q.GetString() : null
            };

            if (shelfEl.TryGetProperty("items", out var items) && items.TryGetProperty("collection", out var itemsCol))
            {
                foreach (var item in itemsCol.EnumerateArray())
                {
                    var kind = item.TryGetProperty("kind", out var k) ? k.GetString() : "";
                    if (kind == "playlist" || kind == "system-playlist")
                    {
                        var playlist = ParsePlaylist(item);
                        shelf.Playlists.Add(playlist);
                    }
                    else if (kind == "track")
                    {
                        var track = ParseTrack(item);
                        shelf.Tracks.Add(track);
                    }
                }
            }

            if (shelf.Playlists.Count > 0 || shelf.Tracks.Count > 0)
            {
                shelves.Add(shelf);
            }
        }

        return shelves;
    }

    // ==========================================
    // 2. PLAYLIST DETAILS & SYSTEM PLAYLIST RESOLUTION
    // ==========================================
    public async Task<Playlist> GetPlaylistDetailsAsync(string playlistUrnOrId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        string url;
        if (playlistUrnOrId.StartsWith("soundcloud:system-playlists:") || playlistUrnOrId.StartsWith("soundcloud:playlists:"))
        {
            url = $"https://api-v2.soundcloud.com/system-playlists/{playlistUrnOrId}?client_id={_clientId}";
        }
        else
        {
            url = $"https://api-v2.soundcloud.com/playlists/{playlistUrnOrId}?client_id={_clientId}";
        }

        using var request = CreateRequest(HttpMethod.Get, url);
        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        return ParsePlaylist(doc.RootElement);
    }

    // ==========================================
    // 3. SEARCH (Tracks, Playlists, Users, Suggestions)
    // ==========================================
    public async Task<List<Track>> SearchTracksAsync(string query, int limit = 24, int offset = 0, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var encoded = UrlEncoder.Default.Encode(query);
        var url = $"https://api-v2.soundcloud.com/search/tracks?q={encoded}&client_id={_clientId}&limit={limit}&offset={offset}";

        using var request = CreateRequest(HttpMethod.Get, url);
        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var list = new List<Track>();
        if (!doc.RootElement.TryGetProperty("collection", out var col)) return list;

        foreach (var item in col.EnumerateArray())
        {
            list.Add(ParseTrack(item));
        }

        return list;
    }

    public async Task<List<Playlist>> SearchPlaylistsAsync(string query, int limit = 20, int offset = 0, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var encoded = UrlEncoder.Default.Encode(query);
        var url = $"https://api-v2.soundcloud.com/search/playlists?q={encoded}&client_id={_clientId}&limit={limit}&offset={offset}";

        using var request = CreateRequest(HttpMethod.Get, url);
        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var list = new List<Playlist>();
        if (!doc.RootElement.TryGetProperty("collection", out var col)) return list;

        foreach (var item in col.EnumerateArray())
        {
            list.Add(ParsePlaylist(item));
        }

        return list;
    }

    public async Task<List<User>> SearchUsersAsync(string query, int limit = 20, int offset = 0, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var encoded = UrlEncoder.Default.Encode(query);
        var url = $"https://api-v2.soundcloud.com/search/users?q={encoded}&client_id={_clientId}&limit={limit}&offset={offset}";

        using var request = CreateRequest(HttpMethod.Get, url);
        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var list = new List<User>();
        if (!doc.RootElement.TryGetProperty("collection", out var col)) return list;

        foreach (var item in col.EnumerateArray())
        {
            list.Add(ParseUser(item));
        }

        return list;
    }

    public async Task<List<string>> GetSearchSuggestionsAsync(string query, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var encoded = UrlEncoder.Default.Encode(query);
        var url = $"https://api-v2.soundcloud.com/search/queries?q={encoded}&client_id={_clientId}&limit=6";

        try
        {
            using var request = CreateRequest(HttpMethod.Get, url);
            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return new();

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            var list = new List<string>();
            if (doc.RootElement.TryGetProperty("collection", out var col))
            {
                foreach (var item in col.EnumerateArray())
                {
                    if (item.TryGetProperty("output", out var output))
                    {
                        var text = output.GetString();
                        if (!string.IsNullOrWhiteSpace(text)) list.Add(text);
                    }
                }
            }
            return list;
        }
        catch
        {
            return new();
        }
    }

    // ==========================================
    // 4. STREAM RESOLUTION (HLS or Progressive MP3)
    // ==========================================
    public async Task<string> ResolveStreamUrlAsync(Track track, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        if (track.Transcodings == null || track.Transcodings.Count == 0)
        {
            // Fallback: fetch track details directly
            var trackUrl = $"https://api-v2.soundcloud.com/tracks/{track.Id}?client_id={_clientId}";
            using var req = CreateRequest(HttpMethod.Get, trackUrl);
            using var resp = await _http.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode)
            {
                var j = await resp.Content.ReadAsStringAsync(ct);
                using var d = JsonDocument.Parse(j);
                var updated = ParseTrack(d.RootElement);
                track.Transcodings = updated.Transcodings;
                track.TrackAuthorization = updated.TrackAuthorization;
            }
        }

        if (track.Transcodings == null || track.Transcodings.Count == 0)
        {
            throw new InvalidOperationException($"No transcodings found for track {track.Id} ({track.Title}).");
        }

        // Transcoding priority order:
        // 1. Progressive MP3 128k (standard seeking, robust)
        // 2. HLS AAC 160k (standard SoundCloud stream)
        // 3. HLS MP3 / opus / any first available
        var ordered = track.Transcodings
            .OrderByDescending(t => t.Protocol == "progressive" ? 3 : (t.Protocol == "hls" ? 2 : 1))
            .ThenByDescending(t => t.MimeType.Contains("mp4a") ? 2 : (t.MimeType.Contains("mpeg") ? 1 : 0))
            .ToList();

        Exception? lastEx = null;
        foreach (var transcoding in ordered)
        {
            try
            {
                var resolveUrl = $"{transcoding.Url}?client_id={_clientId}";
                if (!string.IsNullOrEmpty(track.TrackAuthorization))
                {
                    resolveUrl += $"&track_authorization={track.TrackAuthorization}";
                }

                // CRITICAL: SoundCloud media CDN endpoints do NOT accept OAuth headers!
                using var request = CreateRequest(HttpMethod.Get, resolveUrl, attachAuth: false);
                using var response = await _http.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                var json = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("url", out var cdnUrlProp))
                {
                    var cdnUrl = cdnUrlProp.GetString();
                    if (!string.IsNullOrEmpty(cdnUrl))
                    {
                        return cdnUrl;
                    }
                }
            }
            catch (Exception ex)
            {
                lastEx = ex;
            }
        }

        throw new InvalidOperationException($"Failed to resolve stream URL for track {track.Id} ({track.Title}).", lastEx);
    }

    // ==========================================
    // 5. WAVEFORM SAMPLES (1800 raw audio amplitudes)
    // ==========================================
    public async Task<WaveformData?> GetWaveformDataAsync(string? waveformUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(waveformUrl)) return null;

        var jsonUrl = waveformUrl.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            ? waveformUrl.Substring(0, waveformUrl.Length - 4) + ".json"
            : waveformUrl;

        try
        {
            var json = await _http.GetStringAsync(jsonUrl, ct);
            using var doc = JsonDocument.Parse(json);

            var width = doc.RootElement.TryGetProperty("width", out var w) ? w.GetInt32() : 1800;
            var height = doc.RootElement.TryGetProperty("height", out var h) ? h.GetInt32() : 140;

            if (doc.RootElement.TryGetProperty("samples", out var samplesEl))
            {
                var count = samplesEl.GetArrayLength();
                var samples = new float[count];
                int idx = 0;
                float max = 1.0f;

                foreach (var s in samplesEl.EnumerateArray())
                {
                    var val = s.GetSingle();
                    samples[idx++] = val;
                    if (val > max) max = val;
                }

                // Normalize to 0.0f .. 1.0f range
                for (int i = 0; i < samples.Length; i++)
                {
                    samples[i] = Math.Clamp(samples[i] / max, 0.05f, 1.0f);
                }

                return new WaveformData
                {
                    Width = width,
                    Height = height,
                    Samples = samples
                };
            }
        }
        catch { }

        return null;
    }

    // ==========================================
    // 6. USER ACCOUNT / LIKES / LIBRARY
    // ==========================================
    public async Task<UserSession?> GetMeAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_oauthToken)) return null;

        await EnsureInitializedAsync(ct);

        var url = $"https://api-v2.soundcloud.com/me?client_id={_clientId}";
        using var request = CreateRequest(HttpMethod.Get, url);
        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        return new UserSession
        {
            UserId = root.GetProperty("id").GetInt64(),
            Username = root.GetProperty("username").GetString() ?? "User",
            AvatarUrl = root.TryGetProperty("avatar_url", out var av) ? av.GetString() : null,
            OAuthToken = _oauthToken
        };
    }

    public async Task<List<Track>> GetLikedTracksAsync(long userId, int limit = 30, int offset = 0, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var url = $"https://api-v2.soundcloud.com/users/{userId}/track_likes?client_id={_clientId}&limit={limit}&offset={offset}";
        using var request = CreateRequest(HttpMethod.Get, url);
        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var list = new List<Track>();
        if (!doc.RootElement.TryGetProperty("collection", out var col)) return list;

        foreach (var item in col.EnumerateArray())
        {
            // In track_likes, track object is nested inside "track"
            if (item.TryGetProperty("track", out var trackEl))
            {
                var track = ParseTrack(trackEl);
                track.IsLiked = true;
                list.Add(track);
            }
            else
            {
                var track = ParseTrack(item);
                track.IsLiked = true;
                list.Add(track);
            }
        }

        return list;
    }

    public async Task<bool> LikeTrackAsync(long userId, long trackId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_oauthToken)) return false;
        await EnsureInitializedAsync(ct);

        var url = $"https://api-v2.soundcloud.com/users/{userId}/track_likes/{trackId}?client_id={_clientId}";
        using var request = CreateRequest(HttpMethod.Put, url);
        using var response = await _http.SendAsync(request, ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> UnlikeTrackAsync(long userId, long trackId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_oauthToken)) return false;
        await EnsureInitializedAsync(ct);

        var url = $"https://api-v2.soundcloud.com/users/{userId}/track_likes/{trackId}?client_id={_clientId}";
        using var request = CreateRequest(HttpMethod.Delete, url);
        using var response = await _http.SendAsync(request, ct);
        return response.IsSuccessStatusCode;
    }

    // ==========================================
    // JSON PARSING UTILITIES
    // ==========================================
    public static Track ParseTrack(JsonElement el)
    {
        var track = new Track
        {
            Id = el.TryGetProperty("id", out var idProp) ? idProp.GetInt64() : 0,
            Title = el.TryGetProperty("title", out var titleProp) ? titleProp.GetString() ?? "" : "",
            PermalinkUrl = el.TryGetProperty("permalink_url", out var permProp) ? permProp.GetString() : null,
            ArtworkUrl = el.TryGetProperty("artwork_url", out var artProp) ? artProp.GetString() : null,
            WaveformUrl = el.TryGetProperty("waveform_url", out var waveProp) ? waveProp.GetString() : null,
            Genre = el.TryGetProperty("genre", out var genreProp) ? genreProp.GetString() : null,
            LikesCount = el.TryGetProperty("likes_count", out var likesProp) ? likesProp.GetInt64() : 0,
            PlaybackCount = el.TryGetProperty("playback_count", out var playProp) ? playProp.GetInt64() : 0,
            TrackAuthorization = el.TryGetProperty("track_authorization", out var authProp) ? authProp.GetString() : null
        };

        if (el.TryGetProperty("duration", out var durProp))
        {
            track.DurationMs = durProp.GetInt64();
            track.Duration = TimeSpan.FromMilliseconds(track.DurationMs);
        }

        if (el.TryGetProperty("user", out var userProp))
        {
            track.Artist = userProp.TryGetProperty("username", out var u) ? u.GetString() ?? "Unknown" : "Unknown";
            track.ArtistId = userProp.TryGetProperty("id", out var uid) ? uid.GetInt64() : 0;
            track.ArtistAvatarUrl = userProp.TryGetProperty("avatar_url", out var uav) ? uav.GetString() : null;
        }

        if (el.TryGetProperty("media", out var media) && media.TryGetProperty("transcodings", out var transList))
        {
            foreach (var t in transList.EnumerateArray())
            {
                var trans = new TrackTranscoding
                {
                    Url = t.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "",
                    Duration = t.TryGetProperty("duration", out var dur) ? dur.GetInt32() : 0,
                    Snipped = t.TryGetProperty("snipped", out var snip) && snip.GetBoolean()
                };

                if (t.TryGetProperty("format", out var fmt))
                {
                    trans.Protocol = fmt.TryGetProperty("protocol", out var proto) ? proto.GetString() ?? "" : "";
                    trans.MimeType = fmt.TryGetProperty("mime_type", out var mime) ? mime.GetString() ?? "" : "";
                }

                if (t.TryGetProperty("preset", out var pres))
                {
                    trans.Preset = pres.GetString();
                }

                track.Transcodings.Add(trans);
            }
        }

        return track;
    }

    public static Playlist ParsePlaylist(JsonElement el)
    {
        var playlist = new Playlist
        {
            Id = el.TryGetProperty("id", out var idProp)
                ? (idProp.ValueKind == JsonValueKind.Number ? idProp.GetInt64().ToString() : idProp.GetString() ?? "")
                : el.TryGetProperty("urn", out var urnProp) ? urnProp.GetString() ?? "" : "",
            Title = el.TryGetProperty("title", out var titleProp) ? titleProp.GetString() ?? "" : "",
            Description = el.TryGetProperty("description", out var descProp) ? descProp.GetString() : null,
            ArtworkUrl = el.TryGetProperty("artwork_url", out var artProp) ? artProp.GetString() : null,
            CalculatedArtworkUrl = el.TryGetProperty("calculated_artwork_url", out var cArtProp) ? cArtProp.GetString() : null,
            LikesCount = el.TryGetProperty("likes_count", out var likesProp) ? likesProp.GetInt64() : 0,
            TrackCount = el.TryGetProperty("track_count", out var countProp) ? countProp.GetInt32() : 0,
            PermalinkUrl = el.TryGetProperty("permalink_url", out var permProp) ? permProp.GetString() : null,
            IsSystemPlaylist = el.TryGetProperty("kind", out var kindProp) && kindProp.GetString() == "system-playlist"
        };

        if (el.TryGetProperty("user", out var userProp))
        {
            playlist.Author = userProp.TryGetProperty("username", out var u) ? u.GetString() ?? "" : "";
            playlist.AuthorId = userProp.TryGetProperty("id", out var uid) ? uid.GetInt64() : 0;
        }

        if (el.TryGetProperty("tracks", out var tracksProp) && tracksProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in tracksProp.EnumerateArray())
            {
                // In some playlists, tracks are stub objects with only { "id": ... }
                // or full track objects.
                if (t.TryGetProperty("title", out _))
                {
                    playlist.Tracks.Add(ParseTrack(t));
                }
            }
            if (playlist.TrackCount == 0) playlist.TrackCount = playlist.Tracks.Count;
        }

        return playlist;
    }

    public static User ParseUser(JsonElement el)
    {
        return new User
        {
            Id = el.TryGetProperty("id", out var idProp) ? idProp.GetInt64() : 0,
            Username = el.TryGetProperty("username", out var u) ? u.GetString() ?? "" : "",
            FullName = el.TryGetProperty("full_name", out var fn) ? fn.GetString() : null,
            AvatarUrl = el.TryGetProperty("avatar_url", out var av) ? av.GetString() : null,
            City = el.TryGetProperty("city", out var city) ? city.GetString() : null,
            Country = el.TryGetProperty("country_code", out var cc) ? cc.GetString() : null,
            FollowersCount = el.TryGetProperty("followers_count", out var fol) ? fol.GetInt32() : 0,
            FollowingsCount = el.TryGetProperty("followings_count", out var flw) ? flw.GetInt32() : 0,
            TrackCount = el.TryGetProperty("track_count", out var tc) ? tc.GetInt32() : 0,
            LikesCount = el.TryGetProperty("likes_count", out var lc) ? lc.GetInt32() : 0,
            PlaylistCount = el.TryGetProperty("playlist_count", out var pc) ? pc.GetInt32() : 0,
            Description = el.TryGetProperty("description", out var desc) ? desc.GetString() : null,
            PermalinkUrl = el.TryGetProperty("permalink_url", out var perm) ? perm.GetString() : null
        };
    }
}
