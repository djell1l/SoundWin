using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using SoundCloudExplode;

var client = new SoundCloudClient();
await client.InitializeAsync();

using var http = new HttpClient();
http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

var url = $"https://api-v2.soundcloud.com/mixed-selections?client_id={client.ClientId}&limit=5";
var json = await http.GetStringAsync(url);
using var doc = JsonDocument.Parse(json);
var col = doc.RootElement.GetProperty("collection");
var shelf = col[2]; // Trending by genre
var firstItem = shelf.GetProperty("items").GetProperty("collection")[1]; // All Genres

Console.WriteLine("Item JSON keys:");
foreach (var prop in firstItem.EnumerateObject())
{
    Console.WriteLine($"  {prop.Name}: {prop.Value.ToString().Substring(0, Math.Min(60, prop.Value.ToString().Length))}");
}
