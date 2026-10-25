using System.Collections.Generic;

namespace SoundWin.Core.Models;

public sealed class SoundWinSettings
{
    public const string DefaultDiscordClientId = "463097721130188830";

    /// <summary>
    /// Active ou désactive globalement le Discord Rich Presence (RPC).
    /// </summary>
    public bool EnableDiscordRpc { get; set; } = true;

    /// <summary>
    /// Affiche la pochette d'album haute résolution sur Discord.
    /// </summary>
    public bool DiscordRpcShowCoverArt { get; set; } = true;

    /// <summary>
    /// Affiche un bouton d'action cliquable renvoyant vers le morceau sur SoundCloud.
    /// </summary>
    public bool DiscordRpcShowButtons { get; set; } = true;

    /// <summary>
    /// Affiche l'activité 'En pause' lorsque la lecture est interrompue.
    /// </summary>
    public bool DiscordRpcShowPaused { get; set; } = true;

    /// <summary>
    /// Identifiant de l'application Discord (Client ID).
    /// </summary>
    public string DiscordRpcClientId { get; set; } = DefaultDiscordClientId;

    /// <summary>
    /// Active le serveur Named Pipe pour l'écosystème Jay (Diswin).
    /// </summary>
    public bool EnableSoundWinBridge { get; set; } = true;

    /// <summary>
    /// Thème d'interface (Dark par défaut).
    /// </summary>
    public string ThemeMode { get; set; } = "Dark";

    /// <summary>
    /// Historique des requêtes de recherche récentes.
    /// </summary>
    public List<string> RecentSearchQueries { get; set; } = new();

    /// <summary>
    /// Identifiants des morceaux joués récemment depuis la recherche (style Spotify).
    /// </summary>
    public List<long> RecentSearchedTrackIds { get; set; } = new();

    /// <summary>
    /// Identifiant du dernier morceau joué lors de la fermeture de l'application.
    /// </summary>
    public long LastPlayedTrackId { get; set; }

    /// <summary>
    /// Position de lecture en secondes sauvegardée à la fermeture.
    /// </summary>
    public double LastPlayedPositionSeconds { get; set; }

    /// <summary>
    /// Minimiser dans la zone de notification (systray) au lieu de fermer l'application.
    /// </summary>
    public bool MinimizeToTrayOnClose { get; set; } = true;

    /// <summary>
    /// Canal de mise à jour actif ("Edge" ou "Stable").
    /// </summary>
    public string UpdateChannel { get; set; } = "Edge";

    /// <summary>
    /// Recherche automatique des mises à jour au lancement.
    /// </summary>
    public bool AutoCheckUpdatesOnStartup { get; set; } = true;
}
