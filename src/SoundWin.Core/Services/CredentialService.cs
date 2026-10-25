using System;
using SoundWin.Core.Models;
using Windows.Security.Credentials;

namespace SoundWin.Core.Services;

public class CredentialService
{
    private const string ResourceName = "SoundWin.SoundCloudAuth";

    public UserSession? LoadSession()
    {
        try
        {
            var vault = new PasswordVault();
            var list = vault.FindAllByResource(ResourceName);
            if (list != null && list.Count > 0)
            {
                var cred = list[0];
                cred.RetrievePassword();

                var parts = cred.UserName.Split('|');
                var userId = parts.Length > 0 && long.TryParse(parts[0], out var uid) ? uid : 0;
                var username = parts.Length > 1 ? parts[1] : "User";

                return new UserSession
                {
                    UserId = userId,
                    Username = username,
                    OAuthToken = cred.Password
                };
            }
        }
        catch
        {
            // Vault empty or not found
        }

        return null;
    }

    public void SaveSession(UserSession session)
    {
        if (string.IsNullOrEmpty(session.OAuthToken)) return;

        try
        {
            ClearSession();
            var vault = new PasswordVault();
            var userNameKey = $"{session.UserId}|{session.Username}";
            var cred = new PasswordCredential(ResourceName, userNameKey, session.OAuthToken);
            vault.Add(cred);
        }
        catch { }
    }

    public void ClearSession()
    {
        try
        {
            var vault = new PasswordVault();
            var list = vault.FindAllByResource(ResourceName);
            if (list != null)
            {
                foreach (var c in list)
                {
                    vault.Remove(c);
                }
            }
        }
        catch { }
    }
}
