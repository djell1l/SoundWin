using System;
using System.Collections.Concurrent;
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

    // Global synchronized caches for Liked Tracks, Reposts, and User Playlists
    private static readonly ConcurrentDictionary<long, bool> _likedTrackIds = new();
    private static readonly ConcurrentDictionary<long, bool> _repostedTrackIds = new();
    private static readonly ConcurrentDictionary<long, WeakReference<Track>> _trackRegistry = new();

    public event Action<long, bool>? TrackLikeChanged;
    public event Action<long, bool>? TrackRepostChanged;
    public event Action? UserPlaylistsChanged;

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

            try
            {
                await _explodeClient.InitializeAsync(cancellationToken);
                _clientId = _explodeClient.ClientId;
            }
            catch { }

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
    public async Task<List<Shelf>> GetHomeMixedSelectionsAsync(int limit = 12, CancellationToken ct = default)
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
            // Filter out any promoted or ad-related shelves
            if (shelfEl.TryGetProperty("tracking_feature_name", out var tf) &&
                (tf.GetString()?.Contains("promoted", StringComparison.OrdinalIgnoreCase) == true ||
                 tf.GetString()?.Contains("ad", StringComparison.OrdinalIgnoreCase) == true))
            {
                continue;
            }

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
                    if (IsAdOrPromotedItem(item)) continue;

                    var kind = item.TryGetProperty("kind", out var k) ? k.GetString() : "";
                    if (kind == "playlist" || kind == "system-playlist")
                    {
                        var playlist = ParsePlaylist(item);
                        shelf.Playlists.Add(playlist);
                    }
                    else if (kind == "track")
                    {
                        var track = ParseTrack(item);
                        if (track.Id > 0) shelf.Tracks.Add(track);
                    }
                    else if (item.TryGetProperty("track", out var nestedTrk))
                    {
                        var track = ParseTrack(nestedTrk);
                        if (track.Id > 0) shelf.Tracks.Add(track);
                    }
                    else if (item.TryGetProperty("playlist", out var nestedPl))
                    {
                        var playlist = ParsePlaylist(nestedPl);
                        shelf.Playlists.Add(playlist);
                    }
                }
            }

            if (shelf.Playlists.Count > 0 || shelf.Tracks.Count > 0)
            {
                shelves.Add(shelf);
            }
        }

        // Also append a Trending Tracks shelf so users always have selectable tracks on Home!
        try
        {
            var trendingTracks = await SearchTracksAsync("trending 2026", 16, 0, ct);
            if (trendingTracks.Count > 0)
            {
                shelves.Insert(0, new Shelf
                {
                    Title = "Titres Tendances & Découvertes",
                    Description = "Sélectionnez directement un titre pour lancer l'écoute sans aucune coupure ni publicité.",
                    Tracks = trendingTracks
                });
            }
        }
        catch { }

        return shelves;
    }

    private static bool IsAdOrPromotedItem(JsonElement el)
    {
        if (el.TryGetProperty("kind", out var k))
        {
            var kindStr = k.GetString() ?? "";
            if (kindStr.Contains("ad", StringComparison.OrdinalIgnoreCase) ||
                kindStr.Contains("promoted", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        if (el.TryGetProperty("promoted_by", out var pb) && pb.ValueKind != JsonValueKind.Null)
        {
            return true;
        }
        if (el.TryGetProperty("monetization_model", out var mm) && mm.GetString() == "AD_SUPPORTED_ONLY")
        {
            // Still playable via direct CDN, we never request ad endpoints
        }
        return false;
    }

    // ==========================================
    // 2. PLAYLIST DETAILS & FULL TRACK RESOLUTION
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

        var playlist = ParsePlaylist(doc.RootElement);
        await HydratePlaylistTracksAsync(playlist, ct);
        return playlist;
    }

    /// <summary>
    /// Resolves any stub track IDs inside a playlist that SoundCloud returned without titles/transcodings.
    /// </summary>
    public async Task HydratePlaylistTracksAsync(Playlist playlist, CancellationToken ct = default)
    {
        if (playlist.TrackIds.Count == 0) return;

        var loadedIds = new HashSet<long>(playlist.Tracks.Select(t => t.Id));
        var missingIds = playlist.TrackIds.Where(id => !loadedIds.Contains(id)).Distinct().Take(50).ToList();

        if (missingIds.Count == 0) return;

        var fetched = await GetTracksByIdsAsync(missingIds, ct);
        var fetchedDict = fetched.ToDictionary(t => t.Id, t => t);

        var completeList = new List<Track>();
        var existingDict = playlist.Tracks.ToDictionary(t => t.Id, t => t);

        foreach (var id in playlist.TrackIds)
        {
            if (existingDict.TryGetValue(id, out var existing))
            {
                completeList.Add(existing);
            }
            else if (fetchedDict.TryGetValue(id, out var hydrated))
            {
                completeList.Add(hydrated);
            }
        }

        if (completeList.Count > 0)
        {
            playlist.Tracks = completeList;
            playlist.TrackCount = completeList.Count;
        }
    }

    public async Task<List<Track>> GetTracksByIdsAsync(IEnumerable<long> ids, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var idList = ids.Distinct().Take(50).ToList();
        if (idList.Count == 0) return new List<Track>();

        var joined = string.Join(",", idList);
        var url = $"https://api-v2.soundcloud.com/tracks?ids={joined}&client_id={_clientId}";

        try
        {
            using var request = CreateRequest(HttpMethod.Get, url);
            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return new List<Track>();

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var list = new List<Track>();

            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    if (!IsAdOrPromotedItem(el))
                    {
                        list.Add(ParseTrack(el));
                    }
                }
            }
            return list;
        }
        catch
        {
            return new List<Track>();
        }
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
            if (IsAdOrPromotedItem(item)) continue;
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
            if (IsAdOrPromotedItem(item)) continue;
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
    // 4. STREAM RESOLUTION (Direct Ad-Free Audio)
    // ==========================================
    public async Task<string> ResolveStreamUrlAsync(Track track, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        if (track.Transcodings == null || track.Transcodings.Count == 0)
        {
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

        // Filter out snipped previews if full stream is available, and prioritize Progressive MP3 then HLS AAC
        var ordered = track.Transcodings
            .OrderBy(t => t.Snipped ? 1 : 0)
            .ThenByDescending(t => t.Protocol == "progressive" ? 3 : (t.Protocol == "hls" ? 2 : 1))
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

                // Direct CDN audio resolution without ad-injection headers
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
    // 6. USER ACCOUNT / LIKES / PLAYLISTS / REAL SYNC
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

        var session = new UserSession
        {
            UserId = root.GetProperty("id").GetInt64(),
            Username = root.GetProperty("username").GetString() ?? "User",
            AvatarUrl = root.TryGetProperty("avatar_url", out var av) ? av.GetString() : null,
            OAuthToken = _oauthToken
        };

        // Pre-populate liked track IDs in background
        _ = RefreshLikedTrackIdsAsync(session.UserId, ct);

        return session;
    }

    public async Task RefreshLikedTrackIdsAsync(long userId, CancellationToken ct = default)
    {
        if (userId <= 0) return;
        try
        {
            await EnsureInitializedAsync(ct);
            var url = $"https://api-v2.soundcloud.com/users/{userId}/track_likes/ids?client_id={_clientId}&limit=500";
            using var request = CreateRequest(HttpMethod.Get, url);
            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return;

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("collection", out var col))
            {
                _likedTrackIds.Clear();
                foreach (var idEl in col.EnumerateArray())
                {
                    if (idEl.ValueKind == JsonValueKind.Number)
                    {
                        long tid = idEl.GetInt64();
                        _likedTrackIds[tid] = true;
                        if (_trackRegistry.TryGetValue(tid, out var wr) && wr.TryGetTarget(out var trk))
                        {
                            SoundWin.Core.Utils.AppDispatcher.Dispatch(() => trk.IsLiked = true);
                        }
                    }
                }
            }
        }
        catch { }
    }

    public async Task<List<Track>> GetLikedTracksAsync(long userId, int limit = 50, int offset = 0, CancellationToken ct = default)
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
            if (item.TryGetProperty("track", out var trackEl))
            {
                var track = ParseTrack(trackEl);
                track.IsLiked = true;
                _likedTrackIds[track.Id] = true;
                list.Add(track);
            }
            else if (item.TryGetProperty("title", out _))
            {
                var track = ParseTrack(item);
                track.IsLiked = true;
                _likedTrackIds[track.Id] = true;
                list.Add(track);
            }
        }

        return list;
    }

    public async Task<bool> LikeTrackAsync(long userId, long trackId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_oauthToken) || userId <= 0) return false;
        await EnsureInitializedAsync(ct);

        var url = $"https://api-v2.soundcloud.com/users/{userId}/track_likes/{trackId}?client_id={_clientId}";
        using var request = CreateRequest(HttpMethod.Put, url);
        using var response = await _http.SendAsync(request, ct);
        if (response.IsSuccessStatusCode)
        {
            SetTrackLikeStateEverywhere(trackId, true);
            return true;
        }
        return false;
    }

    public async Task<bool> UnlikeTrackAsync(long userId, long trackId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_oauthToken) || userId <= 0) return false;
        await EnsureInitializedAsync(ct);

        var url = $"https://api-v2.soundcloud.com/users/{userId}/track_likes/{trackId}?client_id={_clientId}";
        using var request = CreateRequest(HttpMethod.Delete, url);
        using var response = await _http.SendAsync(request, ct);
        if (response.IsSuccessStatusCode)
        {
            SetTrackLikeStateEverywhere(trackId, false);
            return true;
        }
        return false;
    }

    private void SetTrackLikeStateEverywhere(long trackId, bool isLiked)
    {
        if (isLiked) _likedTrackIds[trackId] = true;
        else _likedTrackIds.TryRemove(trackId, out _);

        if (_trackRegistry.TryGetValue(trackId, out var wr) && wr.TryGetTarget(out var trk))
        {
            SoundWin.Core.Utils.AppDispatcher.Dispatch(() => trk.IsLiked = isLiked);
        }

        TrackLikeChanged?.Invoke(trackId, isLiked);
    }

    // ==========================================
    // 7. PLAYLIST MANAGEMENT (Create, Edit, Delete, Add Track)
    // ==========================================
    public async Task<List<Playlist>> GetUserPlaylistsAsync(long userId, int limit = 50, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var url = $"https://api-v2.soundcloud.com/users/{userId}/playlists?client_id={_clientId}&limit={limit}";

        try
        {
            using var request = CreateRequest(HttpMethod.Get, url);
            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return new List<Playlist>();

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var list = new List<Playlist>();

            if (doc.RootElement.TryGetProperty("collection", out var col))
            {
                foreach (var item in col.EnumerateArray())
                {
                    list.Add(ParsePlaylist(item));
                }
            }

            // Hydrate first 5 tracks for playlists that only returned stub TrackIds (gives cover art + preview tracks)
            var stubIds = list
                .Where(p => p.Tracks.Count == 0 && p.TrackIds.Count > 0)
                .SelectMany(p => p.TrackIds.Take(5))
                .Distinct()
                .Take(50)
                .ToList();

            if (stubIds.Count > 0)
            {
                try
                {
                    var hydrated = await GetTracksByIdsAsync(stubIds, ct);
                    var map = hydrated.ToDictionary(t => t.Id, t => t);
                    foreach (var pl in list.Where(p => p.Tracks.Count == 0 && p.TrackIds.Count > 0))
                    {
                        foreach (var tid in pl.TrackIds.Take(5))
                        {
                            if (map.TryGetValue(tid, out var trk))
                            {
                                pl.Tracks.Add(trk);
                            }
                        }
                        if (string.IsNullOrEmpty(pl.ArtworkUrl) && string.IsNullOrEmpty(pl.CalculatedArtworkUrl))
                        {
                            pl.CalculatedArtworkUrl = pl.Tracks.FirstOrDefault(t => !string.IsNullOrEmpty(t.ArtworkUrl))?.ArtworkUrl;
                        }
                    }
                }
                catch { }
            }

            return list;
        }
        catch
        {
            return new List<Playlist>();
        }
    }

    public async Task<Playlist?> CreatePlaylistAsync(string title, string? description = null, string sharing = "public", IEnumerable<long>? initialTrackIds = null, CancellationToken ct = default)
    {
        if (!IsAuthenticated || string.IsNullOrWhiteSpace(title)) return null;
        await EnsureInitializedAsync(ct);

        var url = $"https://api-v2.soundcloud.com/playlists?client_id={_clientId}";
        var trackList = initialTrackIds?.Distinct().Select(id => new { id }).ToArray() ?? Array.Empty<object>();

        var payload = new
        {
            playlist = new
            {
                title = title.Trim(),
                description = description?.Trim() ?? "",
                sharing = string.IsNullOrEmpty(sharing) ? "public" : sharing,
                tracks = trackList
            }
        };

        try
        {
            using var request = CreateRequest(HttpMethod.Post, url);
            request.Content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");

            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var created = ParsePlaylist(doc.RootElement);
            UserPlaylistsChanged?.Invoke();
            return created;
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> AddTrackToPlaylistAsync(string playlistId, long trackId, CancellationToken ct = default)
    {
        if (!IsAuthenticated || string.IsNullOrWhiteSpace(playlistId)) return false;
        await EnsureInitializedAsync(ct);

        try
        {
            var current = await GetPlaylistDetailsAsync(playlistId, ct);
            var trackIds = new List<long>(current.TrackIds);
            if (trackIds.Count == 0 && current.Tracks.Count > 0)
            {
                trackIds.AddRange(current.Tracks.Select(t => t.Id));
            }

            if (!trackIds.Contains(trackId))
            {
                trackIds.Add(trackId);
            }

            return await SetPlaylistTracksAsync(playlistId, trackIds, ct);
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> RemoveTrackFromPlaylistAsync(string playlistId, long trackId, CancellationToken ct = default)
    {
        if (!IsAuthenticated || string.IsNullOrWhiteSpace(playlistId)) return false;
        await EnsureInitializedAsync(ct);

        try
        {
            var current = await GetPlaylistDetailsAsync(playlistId, ct);
            var trackIds = new List<long>(current.TrackIds);
            if (trackIds.Count == 0 && current.Tracks.Count > 0)
            {
                trackIds.AddRange(current.Tracks.Select(t => t.Id));
            }

            trackIds.RemoveAll(id => id == trackId);
            return await SetPlaylistTracksAsync(playlistId, trackIds, ct);
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> SetPlaylistTracksAsync(string playlistId, IEnumerable<long> trackIds, CancellationToken ct = default)
    {
        if (!IsAuthenticated || string.IsNullOrWhiteSpace(playlistId)) return false;
        await EnsureInitializedAsync(ct);

        var url = $"https://api-v2.soundcloud.com/playlists/{playlistId}?client_id={_clientId}";
        var payload = new
        {
            playlist = new
            {
                tracks = trackIds.ToArray()
            }
        };

        try
        {
            using var request = CreateRequest(HttpMethod.Put, url);
            request.Content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
            using var response = await _http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                UserPlaylistsChanged?.Invoke();
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> DeletePlaylistAsync(string playlistId, CancellationToken ct = default)
    {
        if (!IsAuthenticated || string.IsNullOrWhiteSpace(playlistId)) return false;
        await EnsureInitializedAsync(ct);

        var url = $"https://api-v2.soundcloud.com/playlists/{playlistId}?client_id={_clientId}";
        try
        {
            using var request = CreateRequest(HttpMethod.Delete, url);
            using var response = await _http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                UserPlaylistsChanged?.Invoke();
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    // ==========================================
    // JSON PARSING UTILITIES
    // ==========================================
    public static Track ParseTrack(JsonElement el)
    {
        long id = el.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.Number ? idProp.GetInt64() : 0;

        var track = new Track
        {
            Id = id,
            Title = el.TryGetProperty("title", out var titleProp) ? titleProp.GetString() ?? "" : "",
            PermalinkUrl = el.TryGetProperty("permalink_url", out var permProp) ? permProp.GetString() : null,
            ArtworkUrl = el.TryGetProperty("artwork_url", out var artProp) ? artProp.GetString() : null,
            WaveformUrl = el.TryGetProperty("waveform_url", out var waveProp) ? waveProp.GetString() : null,
            Genre = el.TryGetProperty("genre", out var genreProp) ? genreProp.GetString() : null,
            Description = el.TryGetProperty("description", out var descProp) ? descProp.GetString() : null,
            LikesCount = el.TryGetProperty("likes_count", out var likesProp) && likesProp.ValueKind == JsonValueKind.Number ? likesProp.GetInt64() : 0,
            PlaybackCount = el.TryGetProperty("playback_count", out var playProp) && playProp.ValueKind == JsonValueKind.Number ? playProp.GetInt64() : 0,
            TrackAuthorization = el.TryGetProperty("track_authorization", out var authProp) ? authProp.GetString() : null,
            IsLiked = _likedTrackIds.ContainsKey(id),
            IsReposted = _repostedTrackIds.ContainsKey(id)
        };

        if (el.TryGetProperty("duration", out var durProp) && durProp.ValueKind == JsonValueKind.Number)
        {
            track.DurationMs = durProp.GetInt64();
            track.Duration = TimeSpan.FromMilliseconds(track.DurationMs);
        }

        if (el.TryGetProperty("user", out var userProp))
        {
            track.Artist = userProp.TryGetProperty("username", out var u) ? u.GetString() ?? "Unknown" : "Unknown";
            track.ArtistId = userProp.TryGetProperty("id", out var uid) && uid.ValueKind == JsonValueKind.Number ? uid.GetInt64() : 0;
            track.ArtistAvatarUrl = userProp.TryGetProperty("avatar_url", out var uav) ? uav.GetString() : null;
        }

        if (el.TryGetProperty("media", out var media) && media.TryGetProperty("transcodings", out var transList))
        {
            foreach (var t in transList.EnumerateArray())
            {
                var trans = new TrackTranscoding
                {
                    Url = t.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "",
                    Duration = t.TryGetProperty("duration", out var dur) && dur.ValueKind == JsonValueKind.Number ? dur.GetInt32() : 0,
                    Snipped = t.TryGetProperty("snipped", out var snip) && snip.ValueKind == JsonValueKind.True
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

        if (id > 0)
        {
            _trackRegistry[id] = new WeakReference<Track>(track);
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
            LikesCount = el.TryGetProperty("likes_count", out var likesProp) && likesProp.ValueKind == JsonValueKind.Number ? likesProp.GetInt64() : 0,
            TrackCount = el.TryGetProperty("track_count", out var countProp) && countProp.ValueKind == JsonValueKind.Number ? countProp.GetInt32() : 0,
            PermalinkUrl = el.TryGetProperty("permalink_url", out var permProp) ? permProp.GetString() : null,
            IsSystemPlaylist = el.TryGetProperty("kind", out var kindProp) && kindProp.GetString() == "system-playlist"
        };

        if (el.TryGetProperty("user", out var userProp))
        {
            playlist.Author = userProp.TryGetProperty("username", out var u) ? u.GetString() ?? "" : "";
            playlist.AuthorId = userProp.TryGetProperty("id", out var uid) && uid.ValueKind == JsonValueKind.Number ? uid.GetInt64() : 0;
        }

        if (el.TryGetProperty("tracks", out var tracksProp) && tracksProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in tracksProp.EnumerateArray())
            {
                if (t.TryGetProperty("id", out var tidEl) && tidEl.ValueKind == JsonValueKind.Number)
                {
                    playlist.TrackIds.Add(tidEl.GetInt64());
                }

                if (t.TryGetProperty("title", out _))
                {
                    playlist.Tracks.Add(ParseTrack(t));
                }
            }
            if (playlist.TrackCount == 0) playlist.TrackCount = Math.Max(playlist.Tracks.Count, playlist.TrackIds.Count);
        }

        return playlist;
    }

    public static User ParseUser(JsonElement el)
    {
        string? bannerUrl = null;
        if (el.TryGetProperty("visuals", out var visEl) &&
            visEl.ValueKind == JsonValueKind.Object &&
            visEl.TryGetProperty("visuals", out var visArray) &&
            visArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var v in visArray.EnumerateArray())
            {
                if (v.TryGetProperty("visual_url", out var vUrl) && vUrl.ValueKind == JsonValueKind.String)
                {
                    bannerUrl = vUrl.GetString();
                    break;
                }
            }
        }

        return new User
        {
            Id = el.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.Number ? idProp.GetInt64() : 0,
            Username = el.TryGetProperty("username", out var u) ? u.GetString() ?? "" : "",
            FirstName = el.TryGetProperty("first_name", out var fn1) ? fn1.GetString() : null,
            LastName = el.TryGetProperty("last_name", out var ln1) ? ln1.GetString() : null,
            FullName = el.TryGetProperty("full_name", out var fn) ? fn.GetString() : null,
            Permalink = el.TryGetProperty("permalink", out var plink) ? plink.GetString() : null,
            AvatarUrl = el.TryGetProperty("avatar_url", out var av) ? av.GetString() : null,
            BannerUrl = bannerUrl,
            City = el.TryGetProperty("city", out var city) ? city.GetString() : null,
            Country = el.TryGetProperty("country_code", out var cc) ? cc.GetString() : null,
            FollowersCount = el.TryGetProperty("followers_count", out var fol) && fol.ValueKind == JsonValueKind.Number ? fol.GetInt32() : 0,
            FollowingsCount = el.TryGetProperty("followings_count", out var flw) && flw.ValueKind == JsonValueKind.Number ? flw.GetInt32() : 0,
            TrackCount = el.TryGetProperty("track_count", out var tc) && tc.ValueKind == JsonValueKind.Number ? tc.GetInt32() : 0,
            LikesCount = el.TryGetProperty("likes_count", out var lc) && lc.ValueKind == JsonValueKind.Number ? lc.GetInt32() : 0,
            PlaylistCount = el.TryGetProperty("playlist_count", out var pc) && pc.ValueKind == JsonValueKind.Number ? pc.GetInt32() : 0,
            RepostsCount = el.TryGetProperty("reposts_count", out var rc) && rc.ValueKind == JsonValueKind.Number ? rc.GetInt32() : 0,
            Description = el.TryGetProperty("description", out var desc) ? desc.GetString() : null,
            PermalinkUrl = el.TryGetProperty("permalink_url", out var perm) ? perm.GetString() : null
        };
    }

    // ==========================================
    // TRACK COMMENTS & REACTIONS
    // ==========================================
    public async Task<List<TrackComment>> GetTrackCommentsAsync(long trackId, int limit = 100, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var url = $"https://api-v2.soundcloud.com/tracks/{trackId}/comments?threaded=1&filter_replies=0&client_id={_clientId}&limit={limit}";

        try
        {
            using var request = CreateRequest(HttpMethod.Get, url);
            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return new List<TrackComment>();

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var list = new List<TrackComment>();

            if (doc.RootElement.TryGetProperty("collection", out var col))
            {
                foreach (var item in col.EnumerateArray())
                {
                    list.Add(ParseComment(item));
                }
            }
            return list;
        }
        catch
        {
            return new List<TrackComment>();
        }
    }

    public async Task<TrackComment?> PostTrackCommentAsync(long trackId, string body, long timestampMs, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        if (!IsAuthenticated) return null;

        var url = $"https://api-v2.soundcloud.com/tracks/{trackId}/comments?client_id={_clientId}";

        var payload = new
        {
            comment = new
            {
                body,
                timestamp = timestampMs
            }
        };

        try
        {
            using var request = CreateRequest(HttpMethod.Post, url);
            request.Content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");

            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            return ParseComment(doc.RootElement);
        }
        catch
        {
            return null;
        }
    }

    // ==========================================
    // USER PROFILE & DETAILS
    // ==========================================
    public async Task<User?> GetUserDetailsAsync(long userId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var url = $"https://api-v2.soundcloud.com/users/{userId}?client_id={_clientId}";

        try
        {
            using var request = CreateRequest(HttpMethod.Get, url);
            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            return ParseUser(doc.RootElement);
        }
        catch
        {
            return null;
        }
    }

    public async Task<List<Track>> GetUserTracksAsync(long userId, int limit = 30, int offset = 0, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var url = $"https://api-v2.soundcloud.com/users/{userId}/tracks?client_id={_clientId}&limit={limit}&offset={offset}";

        try
        {
            using var request = CreateRequest(HttpMethod.Get, url);
            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return new List<Track>();

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var list = new List<Track>();

            if (doc.RootElement.TryGetProperty("collection", out var col))
            {
                foreach (var item in col.EnumerateArray())
                {
                    list.Add(ParseTrack(item));
                }
            }
            return list;
        }
        catch
        {
            return new List<Track>();
        }
    }

    public async Task<bool> UpdateProfileAsync(
        string? username,
        string? description,
        string? city,
        string? countryCode,
        string? firstName = null,
        string? lastName = null,
        string? permalink = null,
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        if (!IsAuthenticated) return false;

        var url = $"https://api-v2.soundcloud.com/me?client_id={_clientId}";

        var payload = new Dictionary<string, object>();
        var userDict = new Dictionary<string, object?>();
        if (!string.IsNullOrEmpty(username)) userDict["username"] = username;
        if (firstName != null) userDict["first_name"] = firstName;
        if (lastName != null) userDict["last_name"] = lastName;
        if (!string.IsNullOrEmpty(permalink)) userDict["permalink"] = permalink;
        if (description != null) userDict["description"] = description;
        if (city != null) userDict["city"] = city;
        if (countryCode != null) userDict["country_code"] = countryCode;
        payload["user"] = userDict;

        try
        {
            using var request = CreateRequest(HttpMethod.Put, url);
            request.Content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");

            using var response = await _http.SendAsync(request, ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string?> UploadAvatarAsync(byte[] imageBytes, string fileName = "avatar.jpg", CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        if (!IsAuthenticated || imageBytes == null || imageBytes.Length == 0) return null;

        try
        {
            var base64Data = Convert.ToBase64String(imageBytes);
            var url = $"https://api-v2.soundcloud.com/me?client_id={_clientId}";

            // Try JSON avatar_data first (standard SoundCloud v2 format)
            var payload = new
            {
                user = new
                {
                    avatar_data = base64Data
                }
            };

            using (var request = CreateRequest(HttpMethod.Put, url))
            {
                request.Content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
                using var response = await _http.SendAsync(request, ct);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(ct);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("avatar_url", out var av))
                    {
                        return av.GetString();
                    }
                    return "updated";
                }
            }

            // Fallback: multipart form-data
            using (var request = CreateRequest(HttpMethod.Put, url))
            using (var form = new MultipartFormDataContent())
            {
                var imgContent = new ByteArrayContent(imageBytes);
                imgContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
                form.Add(imgContent, "user[avatar_data]", fileName);
                request.Content = form;

                using var response = await _http.SendAsync(request, ct);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(ct);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("avatar_url", out var av))
                    {
                        return av.GetString();
                    }
                    return "updated";
                }
            }
        }
        catch { }
        return null;
    }

    public async Task<string?> UploadHeaderImageAsync(byte[] imageBytes, string fileName = "header.jpg", CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        if (!IsAuthenticated || imageBytes == null || imageBytes.Length == 0) return null;

        try
        {
            var base64Data = Convert.ToBase64String(imageBytes);

            // 1. Try /me/visuals endpoint (SoundCloud v2 profile banner endpoint)
            var visualsUrl = $"https://api-v2.soundcloud.com/me/visuals?client_id={_clientId}";
            var visualPayload = new
            {
                visual = new
                {
                    image_data = base64Data,
                    entry_time = 0
                }
            };

            using (var req = CreateRequest(HttpMethod.Post, visualsUrl))
            {
                req.Content = new StringContent(JsonSerializer.Serialize(visualPayload), System.Text.Encoding.UTF8, "application/json");
                using var resp = await _http.SendAsync(req, ct);
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync(ct);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("visual_url", out var vUrl))
                    {
                        return vUrl.GetString();
                    }
                    return "updated";
                }
            }

            // 2. Fallback: PUT /me with visual_data
            var meUrl = $"https://api-v2.soundcloud.com/me?client_id={_clientId}";
            var mePayload = new
            {
                user = new
                {
                    visual_data = base64Data
                }
            };

            using (var req = CreateRequest(HttpMethod.Put, meUrl))
            {
                req.Content = new StringContent(JsonSerializer.Serialize(mePayload), System.Text.Encoding.UTF8, "application/json");
                using var resp = await _http.SendAsync(req, ct);
                if (resp.IsSuccessStatusCode)
                {
                    return "updated";
                }
            }

            // 3. Fallback: Multipart POST /me/visuals
            using (var req = CreateRequest(HttpMethod.Post, visualsUrl))
            using (var form = new MultipartFormDataContent())
            {
                var imgContent = new ByteArrayContent(imageBytes);
                imgContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
                form.Add(imgContent, "visual[image_data]", fileName);
                req.Content = form;

                using var resp = await _http.SendAsync(req, ct);
                if (resp.IsSuccessStatusCode)
                {
                    return "updated";
                }
            }
        }
        catch { }
        return null;
    }

    public async Task<List<User>> GetUserFollowingsAsync(long userId, int limit = 20, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var url = $"https://api-v2.soundcloud.com/users/{userId}/followings?client_id={_clientId}&limit={limit}";

        try
        {
            using var request = CreateRequest(HttpMethod.Get, url);
            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return new List<User>();

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var list = new List<User>();

            if (doc.RootElement.TryGetProperty("collection", out var col))
            {
                foreach (var item in col.EnumerateArray())
                {
                    list.Add(ParseUser(item));
                }
            }
            return list;
        }
        catch
        {
            return new List<User>();
        }
    }

    public async Task<List<Track>> GetUserRepostsAsync(long userId, int limit = 30, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var url = $"https://api-v2.soundcloud.com/stream/users/{userId}/reposts?client_id={_clientId}&limit={limit}";

        try
        {
            using var request = CreateRequest(HttpMethod.Get, url);
            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return new List<Track>();

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var list = new List<Track>();

            if (doc.RootElement.TryGetProperty("collection", out var col))
            {
                foreach (var item in col.EnumerateArray())
                {
                    if (item.TryGetProperty("track", out var trkEl))
                    {
                        var trk = ParseTrack(trkEl);
                        trk.IsReposted = true;
                        list.Add(trk);
                    }
                }
            }
            return list;
        }
        catch
        {
            return new List<Track>();
        }
    }

    public async Task<List<WebProfileLink>> GetUserWebProfilesAsync(long userId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var urn = Uri.EscapeDataString($"soundcloud:users:{userId}");
        var url = $"https://api-v2.soundcloud.com/users/{urn}/web-profiles?client_id={_clientId}";

        try
        {
            using var request = CreateRequest(HttpMethod.Get, url);
            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return new List<WebProfileLink>();

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var list = new List<WebProfileLink>();

            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    list.Add(new WebProfileLink
                    {
                        Id = item.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number ? idEl.GetInt64() : 0,
                        Title = item.TryGetProperty("title", out var tEl) ? tEl.GetString() ?? "" : "",
                        Url = item.TryGetProperty("url", out var uEl) ? uEl.GetString() ?? "" : "",
                        Service = item.TryGetProperty("service", out var sEl) ? sEl.GetString() : null
                    });
                }
            }
            return list;
        }
        catch
        {
            return new List<WebProfileLink>();
        }
    }

    public async Task<bool> UpdateUserWebProfilesAsync(long userId, IEnumerable<WebProfileLink> links, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        if (!IsAuthenticated) return false;

        var urn = Uri.EscapeDataString($"soundcloud:users:{userId}");
        var url = $"https://api-v2.soundcloud.com/users/{urn}/web-profiles?client_id={_clientId}";

        try
        {
            var payload = links.Select(l => new
            {
                title = l.Title,
                url = l.Url
            }).ToArray();

            using var request = CreateRequest(HttpMethod.Put, url);
            request.Content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
            using var response = await _http.SendAsync(request, ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public static TrackComment ParseComment(JsonElement el)
    {
        long ts = el.TryGetProperty("timestamp", out var tProp) && tProp.ValueKind == JsonValueKind.Number ? tProp.GetInt64() : 0;
        var comment = new TrackComment
        {
            Id = el.TryGetProperty("id", out var idProp) ? idProp.GetInt64() : 0,
            Body = el.TryGetProperty("body", out var bProp) ? bProp.GetString() ?? "" : "",
            CreatedAt = el.TryGetProperty("created_at", out var cProp) && cProp.TryGetDateTime(out var dt) ? dt : DateTime.UtcNow,
            TimestampMs = ts,
            Timestamp = TimeSpan.FromMilliseconds(ts),
            TrackId = el.TryGetProperty("track_id", out var trProp) ? trProp.GetInt64() : 0
        };

        if (el.TryGetProperty("user", out var uProp))
        {
            comment.UserId = uProp.TryGetProperty("id", out var uid) ? uid.GetInt64() : 0;
            comment.Username = uProp.TryGetProperty("username", out var un) ? un.GetString() ?? "Anonyme" : "Anonyme";
            comment.AvatarUrl = uProp.TryGetProperty("avatar_url", out var av) ? av.GetString() : null;
        }

        return comment;
    }

    // ==========================================
    // NUAGE-INSPIRED DISCOVERY & SOCIAL FEATURES
    // ==========================================

    public async Task<List<StreamPost>> GetStreamAsync(int limit = 30, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var url = $"https://api-v2.soundcloud.com/stream?client_id={_clientId}&limit={limit}";

        try
        {
            using var request = CreateRequest(HttpMethod.Get, url);
            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return new List<StreamPost>();

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var list = new List<StreamPost>();

            if (doc.RootElement.TryGetProperty("collection", out var col))
            {
                foreach (var item in col.EnumerateArray())
                {
                    if (IsAdOrPromotedItem(item)) continue;

                    var post = new StreamPost();
                    if (item.TryGetProperty("type", out var typeEl))
                    {
                        var tStr = typeEl.GetString() ?? "";
                        if (tStr.Contains("promoted", StringComparison.OrdinalIgnoreCase)) continue;
                        post.IsRepost = tStr.Contains("repost", StringComparison.OrdinalIgnoreCase);
                        post.Type = tStr.Contains("playlist") ? "playlist" : "track";
                    }

                    if (item.TryGetProperty("created_at", out var catEl) && catEl.TryGetDateTime(out var dt))
                    {
                        post.CreatedAt = dt;
                    }

                    if (item.TryGetProperty("user", out var userEl))
                    {
                        post.User = ParseUser(userEl);
                    }

                    if (item.TryGetProperty("track", out var trkEl))
                    {
                        if (IsAdOrPromotedItem(trkEl)) continue;
                        post.Track = ParseTrack(trkEl);
                    }
                    else if (item.TryGetProperty("playlist", out var plEl))
                    {
                        if (IsAdOrPromotedItem(plEl)) continue;
                        post.Playlist = ParsePlaylist(plEl);
                    }

                    if (post.Track != null || post.Playlist != null)
                    {
                        list.Add(post);
                    }
                }
            }
            return list;
        }
        catch
        {
            return new List<StreamPost>();
        }
    }

    public async Task<List<Track>> GetTrackStationAsync(long trackId, int limit = 25, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var url = $"https://api-v2.soundcloud.com/system-playlists/soundcloud:system-playlists:track-stations:{trackId}?client_id={_clientId}";

        try
        {
            using var request = CreateRequest(HttpMethod.Get, url);
            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return new List<Track>();

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var pl = ParsePlaylist(doc.RootElement);
            await HydratePlaylistTracksAsync(pl, ct);
            return pl.Tracks;
        }
        catch
        {
            return new List<Track>();
        }
    }

    public async Task<List<Track>> GetArtistStationAsync(long artistId, int limit = 25, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var url = $"https://api-v2.soundcloud.com/system-playlists/soundcloud:system-playlists:artist-stations:{artistId}?client_id={_clientId}";

        try
        {
            using var request = CreateRequest(HttpMethod.Get, url);
            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return new List<Track>();

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var pl = ParsePlaylist(doc.RootElement);
            await HydratePlaylistTracksAsync(pl, ct);
            return pl.Tracks;
        }
        catch
        {
            return new List<Track>();
        }
    }

    public async Task<bool> RepostTrackAsync(long trackId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        if (!IsAuthenticated) return false;

        var url = $"https://api-v2.soundcloud.com/me/track_reposts/{trackId}?client_id={_clientId}";
        try
        {
            using var request = CreateRequest(HttpMethod.Put, url);
            using var response = await _http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                _repostedTrackIds[trackId] = true;
                if (_trackRegistry.TryGetValue(trackId, out var wr) && wr.TryGetTarget(out var trk))
                {
                    SoundWin.Core.Utils.AppDispatcher.Dispatch(() => trk.IsReposted = true);
                }
                TrackRepostChanged?.Invoke(trackId, true);
                return true;
            }
            return false;
        }
        catch { return false; }
    }

    public async Task<bool> UnrepostTrackAsync(long trackId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        if (!IsAuthenticated) return false;

        var url = $"https://api-v2.soundcloud.com/me/track_reposts/{trackId}?client_id={_clientId}";
        try
        {
            using var request = CreateRequest(HttpMethod.Delete, url);
            using var response = await _http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                _repostedTrackIds.TryRemove(trackId, out _);
                if (_trackRegistry.TryGetValue(trackId, out var wr) && wr.TryGetTarget(out var trk))
                {
                    SoundWin.Core.Utils.AppDispatcher.Dispatch(() => trk.IsReposted = false);
                }
                TrackRepostChanged?.Invoke(trackId, false);
                return true;
            }
            return false;
        }
        catch { return false; }
    }

    public async Task<List<Track>> GetPlayHistoryAsync(int limit = 16, CancellationToken ct = default)
    {
        if (!IsAuthenticated) return new List<Track>();
        await EnsureInitializedAsync(ct);

        var url = $"https://api-v2.soundcloud.com/me/play-history/tracks?client_id={_clientId}&limit={limit}";
        try
        {
            using var request = CreateRequest(HttpMethod.Get, url);
            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return new List<Track>();

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var list = new List<Track>();

            if (doc.RootElement.TryGetProperty("collection", out var col))
            {
                foreach (var item in col.EnumerateArray())
                {
                    if (item.TryGetProperty("track", out var trkEl))
                    {
                        if (!IsAdOrPromotedItem(trkEl)) list.Add(ParseTrack(trkEl));
                    }
                    else if (item.TryGetProperty("title", out _))
                    {
                        if (!IsAdOrPromotedItem(item)) list.Add(ParseTrack(item));
                    }
                }
            }
            return list;
        }
        catch
        {
            return new List<Track>();
        }
    }

    public async Task<bool> FollowUserAsync(long currentUserId, long targetUserId, CancellationToken ct = default)
    {
        if (!IsAuthenticated || currentUserId <= 0 || targetUserId <= 0) return false;
        await EnsureInitializedAsync(ct);

        var url = $"https://api-v2.soundcloud.com/users/{currentUserId}/followings/{targetUserId}?client_id={_clientId}";
        try
        {
            using var request = CreateRequest(HttpMethod.Put, url);
            using var response = await _http.SendAsync(request, ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> UnfollowUserAsync(long currentUserId, long targetUserId, CancellationToken ct = default)
    {
        if (!IsAuthenticated || currentUserId <= 0 || targetUserId <= 0) return false;
        await EnsureInitializedAsync(ct);

        var url = $"https://api-v2.soundcloud.com/users/{currentUserId}/followings/{targetUserId}?client_id={_clientId}";
        try
        {
            using var request = CreateRequest(HttpMethod.Delete, url);
            using var response = await _http.SendAsync(request, ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<User>> GetWhoToFollowAsync(int limit = 12, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var url = $"https://api-v2.soundcloud.com/me/suggested/users/who_to_follow?client_id={_clientId}&limit={limit}";

        try
        {
            using var request = CreateRequest(HttpMethod.Get, url);
            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return new List<User>();

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var list = new List<User>();

            if (doc.RootElement.TryGetProperty("collection", out var col))
            {
                foreach (var item in col.EnumerateArray())
                {
                    if (item.TryGetProperty("user", out var uEl))
                    {
                        list.Add(ParseUser(uEl));
                    }
                }
            }
            return list;
        }
        catch
        {
            return new List<User>();
        }
    }
}
