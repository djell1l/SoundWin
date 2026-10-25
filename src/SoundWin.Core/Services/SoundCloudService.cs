using System;
using System.Threading.Tasks;
using SoundCloudExplode;

namespace SoundWin.Core.Services;

public class SoundCloudService
{
    private readonly SoundCloudClient _client;

    public SoundCloudService()
    {
        _client = new SoundCloudClient();
    }

    public async Task InitializeAsync()
    {
        // Test initialization
        await _client.InitializeAsync();
    }
}
