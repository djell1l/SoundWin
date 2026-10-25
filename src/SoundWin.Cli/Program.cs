using System;
using System.Net.Http;
using System.Threading.Tasks;
using SoundWin.Core.Services;

Console.WriteLine("========================================");
Console.WriteLine(" SoundWin Diagnostic CLI Runner");
Console.WriteLine("========================================");

var apiClient = new SoundCloudApiClient();
Console.WriteLine("Initializing SoundCloud API client...");
await apiClient.EnsureInitializedAsync();
Console.WriteLine($"Public Client ID acquired: {apiClient.ClientId}");

Console.WriteLine("\nSearching for tracks ('synthwave')...");
var tracks = await apiClient.SearchTracksAsync("synthwave", limit: 3);
Console.WriteLine($"Found {tracks.Count} tracks.");

foreach (var track in tracks)
{
    Console.WriteLine($"\n[Track {track.Id}] {track.Artist} - {track.Title} ({track.Duration:m\\:ss})");
    Console.WriteLine($"  Artwork: {track.ArtworkUrl}");
    Console.WriteLine($"  Transcodings: {track.Transcodings.Count}");
    
    try
    {
        var streamUrl = await apiClient.ResolveStreamUrlAsync(track);
        Console.WriteLine($"  Resolved Stream URL: {streamUrl.Substring(0, Math.Min(80, streamUrl.Length))}...");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  Failed to resolve stream: {ex.Message}");
    }
}

Console.WriteLine("\nDiagnostic completed successfully.");
