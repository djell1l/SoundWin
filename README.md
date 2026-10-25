# 🎵 SoundWin — Client SoundCloud Natif WinUI 3 & C# (.NET 8)

> **Le premier client SoundCloud de bureau 100% natif Windows 11 / WinUI 3 (Windows App SDK), sans aucun wrapper web (0% Electron, 0% Chromium).**  
> Conçu selon la même philosophie de haute performance, de fluidité et d'intégration Windows que **Diswin** et **Files App**.

---

## 🌟 Points Forts & Architecture

- **Rendu Visuel Windows 11 Fluent** :
  - Arrière-plan **Mica Alt** translucide avec intégration moderne des coins arrondis (squircles).
  - Barre de titre personnalisée avec champ de recherche rapide intégré et avatar utilisateur.
  - Barre de lecture persistante en bas avec effet **Acrylic**, slider de volume et accès rapide à la file d'attente (*Queue*).
- **Véritable Waveform SoundCloud Interactive** :
  - Téléchargement et analyse des 1800 amplitudes sonores fournies par l'API SoundCloud (`.json`).
  - Rendu vectoriel dynamique en temps réel avec coloration orange SoundCloud et recherche fluide (*scrubbing* au clic/glisser).
- **Moteur Audio 100% Natif** :
  - `Windows.Media.Playback.MediaPlayer` pour une lecture matérielle accélérée HLS (AAC 160k) et Progressive MP3.
  - Support natif des **SMTC** (*System Media Transport Controls*) : touches multimédia du clavier, widget son de Windows 11, commandes sur l'écran de verrouillage avec jaquette HD.
- **Discord Rich Presence (RPC)** :
  - Tube IPC direct (`\\.\pipe\discord-ipc-0`) diffusant le morceau en direct avec titre, artiste, pochette et défilement temporel.
- **Accès Immédiat & Authentification Sûre** :
  - **Mode Invité** : Extraction automatique du `client_id` public de SoundCloud, zéro configuration requise.
  - **Connexion SoundCloud** : Fenêtre WebView2 éphémère pour la capture unique du cookie `oauth_token`, stocké de façon chiffrée dans le `PasswordVault` de Windows. Zéro navigateur en tâche de fond par la suite !

---

## 🚀 Compilation & Lancement

```powershell
# Compiler et lancer en mode Debug
.\build.ps1

# Compiler en mode Release
.\build.ps1 -Configuration Release

# Compiler sans lancer
.\build.ps1 -NoRun
```

---

## 📁 Structure du Projet

```
SoundWin/
├── SoundWin.sln
├── build.ps1
├── src/
│   ├── SoundWin.Core/          # Moteur C# autonome (API SoundCloud v2, Player, SMTC, RPC, Auth)
│   ├── SoundWin.App/           # Application WinUI 3 (Windows App SDK, Vues, Contrôles, Waveform)
│   └── SoundWin.Cli/           # Outil CLI pour tests rapides d'API et streaming
```
