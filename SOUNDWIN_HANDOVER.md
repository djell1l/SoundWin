# 🚀 SoundWin — Dossier de Reprise de Contexte & Handover

> **Archivage & Continuité de Projet**  
> **Auteur / Lead :** djell1l  
> **Emplacement local :** `C:\Users\djell\Projects\SoundWin\`  
> **Philosophie :** 100% C# .NET 8 + WinUI 3 (Windows App SDK), 0% Electron, 0% Chromium, Nativité Maximale (standard Diswin & Files).

---

## 1. Vue d'Ensemble & Stack Technique

- **Solution** : `C:\Users\djell\Projects\SoundWin\SoundWin.sln`
- **Langage & Framework** : C# (.NET 8.0 Windows 10.0.19041.0+ / Windows 11), WinUI 3 via `Microsoft.WindowsAppSDK 1.7.250606001`.
- **Mode de Déploiement** : Unpackaged (`<WindowsPackageType>None</WindowsPackageType>`), Self-Contained.
- **Moteur Réseau SoundCloud** (`SoundWin.Core.Services.SoundCloudApiClient`) :
  - Extraction dynamique et automatique du `client_id` public à partir des scripts bundles de SoundCloud.
  - Décodage des endpoints internes v2 :
    - `mixed-selections` (Sélections curatées, Tendances par genre, Artistes émergents).
    - `search/tracks`, `search/playlists`, `search/users`, `search/queries` (suggestions instantanées).
    - Résolution des flux audio CDN (HLS AAC 160k & Progressive MP3).
    - Téléchargement et normalisation des 1800 échantillons de forme d'onde (`wave.sndcdn.com/...json`).
    - Synchronisation des Likes et Playlists (`track_likes`).
- **Moteur Audio & Système** (`SoundWin.Core.Services.AudioPlayerService`) :
  - `Windows.Media.Playback.MediaPlayer` natif.
  - Intégration totale avec `SystemMediaTransportControls` (touches média clavier, écran de verrouillage, widget Windows 11).
  - File d'attente (Queue) dynamique avec mode Shuffle et Repeat (Off, All, One).
- **Discord Rich Presence** (`SoundWin.Core.Services.DiscordRpcService`) :
  - Connexion IPC Named Pipes (`\\.\pipe\discord-ipc-0`) sans DLL externe.
- **Waveform Vectorielle Interactive** (`SoundWin.App.Controls.WaveformControl`) :
  - Rendu en temps réel des barres d'onde sonore avec coloration orange SoundCloud et scrubbing fluide au clic/glisser.

---

## 2. Commandes Utiles

```powershell
cd C:\Users\djell\Projects\SoundWin

# Compiler et lancer
.\build.ps1

# Compiler en Release
.\build.ps1 -Configuration Release

# Tester les endpoints API via le CLI
dotnet run --project .\src\SoundWin.Cli\SoundWin.Cli.csproj
```
