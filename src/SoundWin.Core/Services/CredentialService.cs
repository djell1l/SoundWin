using System;
using System.IO;
using System.Text.Json;
using SoundWin.Core.Models;
using Windows.Security.Credentials;

namespace SoundWin.Core.Services;

public class CredentialService
{
    private const string ResourceName = "SCwin.SoundCloudAuth";
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private static string GetSessionFilePath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(localAppData, "SoundWin");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "session.json");
    }

    public UserSession? LoadSession()
    {
        // 1. Try file storage in LocalAppData first (100% reliable for unpackaged apps)
        try
        {
            var path = GetSessionFilePath();
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var session = JsonSerializer.Deserialize<UserSession>(json, JsonOpts);
                if (session != null && !string.IsNullOrEmpty(session.OAuthToken))
                {
                    return session;
                }
            }
        }
        catch { }

        // 2. Fallback to Windows PasswordVault
        try
        {
            var vault = new PasswordVault();
            IReadOnlyList<PasswordCredential>? list = null;
            try { list = vault.FindAllByResource(ResourceName); } catch { }
            if (list == null || list.Count == 0)
            {
                try { list = vault.FindAllByResource("SoundWin.SoundCloudAuth"); } catch { }
            }

            if (list != null && list.Count > 0)
            {
                var cred = list[0];
                cred.RetrievePassword();

                var parts = cred.UserName.Split('|');
                var userId = parts.Length > 0 && long.TryParse(parts[0], out var uid) ? uid : 0;
                var username = parts.Length > 1 ? parts[1] : "User";
                var avatarUrl = parts.Length > 2 && !string.IsNullOrEmpty(parts[2]) ? parts[2] : null;

                var session = new UserSession
                {
                    UserId = userId,
                    Username = username,
                    AvatarUrl = avatarUrl,
                    OAuthToken = cred.Password
                };

                SaveSession(session);
                return session;
            }
        }
        catch { }

        return null;
    }

    public void SaveSession(UserSession session)
    {
        if (string.IsNullOrEmpty(session.OAuthToken)) return;

        // 1. Save to LocalAppData file
        try
        {
            var path = GetSessionFilePath();
            var json = JsonSerializer.Serialize(session, JsonOpts);
            File.WriteAllText(path, json);
        }
        catch { }

        // 2. Mirror into PasswordVault
        try
        {
            ClearVaultOnly();
            var vault = new PasswordVault();
            var userNameKey = $"{session.UserId}|{session.Username}|{session.AvatarUrl ?? ""}";
            var cred = new PasswordCredential(ResourceName, userNameKey, session.OAuthToken);
            vault.Add(cred);
        }
        catch { }
    }

    public void ClearSession()
    {
        try
        {
            var path = GetSessionFilePath();
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }

        ClearVaultOnly();
    }

    private void ClearVaultOnly()
    {
        try
        {
            var vault = new PasswordVault();
            foreach (var res in new[] { ResourceName, "SoundWin.SoundCloudAuth" })
            {
                try
                {
                    var list = vault.FindAllByResource(res);
                    if (list != null)
                    {
                        foreach (var c in list) vault.Remove(c);
                    }
                }
                catch { }
            }
        }
        catch { }
    }
}
