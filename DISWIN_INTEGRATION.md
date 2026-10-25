# Jay Ecosystem Integration: SoundWin <-> Diswin

Ce document documente l'intégration native bidirectionnelle entre **SoundWin** (client SoundCloud natif WinUI 3) et **Diswin** (client Discord natif WinUI 3), tous deux développés au sein de l'écosystème Jay.

---

## 1. Vue d'Ensemble

SoundWin et Diswin sont conçus pour fonctionner en parfaite synergie lorsqu'ils sont ouverts simultanément sur la machine de l'utilisateur :
- **SoundWin** héberge le serveur IPC Named Pipe : `\\.\pipe\Jay_SoundWin_Ecosystem`.
- **Diswin** se connecte en tant que client et affiche un mini-lecteur intégré et dynamique au-dessus de la barre de profil utilisateur.
- **Contrôles distants** : L'utilisateur peut mettre en pause, reprendre, passer au morceau suivant/précédent, et ramener SoundWin au premier plan directement depuis l'interface Discord.
- **Partage en 1 clic** : L'utilisateur peut partager instantanément le morceau SoundCloud écouté dans le salon ou MP actif sur Discord.

---

## 2. Architecture Technique (SoundWin)

### Fichiers Impliqués dans SoundWin
- **`src/SoundWin.Core/Services/SoundWinBridgeServer.cs`** :
  - Écoute sur le pipe nommé `\\.\pipe\Jay_SoundWin_Ecosystem`.
  - Abonne aux événements `AudioPlayerService.TrackChanged` et `AudioPlayerService.PlaybackStateChanged`.
  - Diffuse en temps réel l'état de lecture JSON (`state` et `progress`).
  - Reçoit et exécute les commandes envoyées par Diswin sur le thread UI via `AppDispatcher.Dispatch`.
- **`src/SoundWin.App/App.xaml.cs`** :
  - Enregistre `SoundWinBridgeServer` en singleton dans le conteneur d'injection de dépendances.
  - Démarre automatiquement le serveur en arrière-plan via `_ = bridge.StartAsync()` lors du lancement.
  - Renseigne `SoundWinBridgeServer.BringToFrontHandler` pour activer et focaliser la fenêtre principale de SoundWin.

---

## 3. Protocole IPC (JSON sur Named Pipe)

### Événements émis par SoundWin vers Diswin :

#### A. État global (`event: "state"`)
Envoyé lors de la connexion, d'un changement de piste, d'un changement d'état Play/Pause :
```json
{
  "event": "state",
  "isPlaying": true,
  "trackId": 182749281,
  "title": "Nom du morceau",
  "artist": "Nom de l'artiste",
  "artworkUrl": "https://i1.sndcdn.com/artworks-...-t500x500.jpg",
  "permalinkUrl": "https://soundcloud.com/artiste/morceau",
  "durationMs": 215000,
  "positionMs": 45000,
  "progress": 0.2093
}
```

#### B. Progression continue (`event: "progress"`)
Envoyé chaque seconde pendant la lecture :
```json
{
  "event": "progress",
  "positionMs": 46000,
  "progress": 0.2139
}
```

### Commandes acceptées par SoundWin depuis Diswin :

- `{"command": "toggle_play"}` : Bascule entre Play et Pause via `_player.TogglePlayPause()`.
- `{"command": "play"}` : Démarre la lecture via `_player.Play()`.
- `{"command": "pause"}` : Met en pause via `_player.Pause()`.
- `{"command": "next"}` : Passe au morceau suivant dans la file d'attente via `_player.NextAsync()`.
- `{"command": "prev"}` ou `{"command": "previous"}` : Morceau précédent via `_player.PreviousAsync()`.
- `{"command": "get_state"}` : Renvoie immédiatement l'état actuel (`state`).
- `{"command": "focus"}` ou `{"command": "bring_to_front"}` : Ramène la fenêtre SoundWin au premier plan.

---

## 4. Consignes pour l'Agent SoundWin

Si l'utilisateur demande des fonctionnalités supplémentaires liées à Diswin ou à l'écosystème Jay :
1. **Conserver la compatibilité du protocole** : Toute modification de format JSON ou de nom de commande doit rester rétrocompatible avec `SoundWinBridgeService.cs` dans Diswin.
2. **Pas de couplage fort** : SoundWin ne dépend d'aucun binaire Diswin. La communication se fait exclusivement via le Named Pipe `Jay_SoundWin_Ecosystem`.
3. **Persistance** : Le service démarre automatiquement dans `App.xaml.cs` et tourne de manière non-bloquante sans impacter les performances audio ou le framerate WinUI 3.
