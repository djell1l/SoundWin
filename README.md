# SoundWin (maintenant SCwin)

Running an entire Chromium browser in the background just to stream a few audio tracks on Windows is an insult to your hardware. If Apple gets to flex its sleek Swift and SwiftUI native ecosystem with apps like Nuage, why on earth shouldn't Windows get the exact same first-class treatment? Respect your ecosystem.

**SCwin** (anciennement **SoundWin**) is the world's first 100% native SoundCloud client engineered from scratch in **C# (.NET 8)** and **WinUI 3**. No Electron. No CEF. No disguised web browser quietly devouring half a gigabyte of your RAM just to play an MP3. Either way, you're welcome.

---

## Highlights

- **Pure WinUI 3 & Fluent Design**: Real Mica Alt backdrop, dark and light theme synchronization, squircle geometry, integrated title bar search, and acrylic bottom player bar inspired by Files, Nuage, and Diswin. No fake web CSS tricks.
- **Interactive Vector Waveform**: Real SoundCloud waveform scrubbing powered by the official 1,800-amplitude JSON telemetry. Smooth hardware-rendered bars with responsive dragging, pointer hover tracking, dynamic progress fill, and timestamped user comments overlay.
- **Hardware-Accelerated Audio**: Windows Media Foundation and `Windows.Media.Playback.MediaPlayer` handling Progressive MP3 and HLS streams natively with sub-millisecond seek times and zero background bloat.
- **Nuage-Inspired Discovery & Social Features**:
  - **Live Stream (Flux d'actualités)**: Direct feed of newly posted tracks, albums, and reposts from creators you follow.
  - **Track & Artist Radio Stations**: One-click radio generation discovering similar music and related creators (`soundcloud:system-playlists:track-stations` & `artist-stations`).
  - **Context Actions Everywhere**: Right-click or 3-dots menu on any track: "Lire maintenant", "Lire ensuite", "Ajouter à la file d'attente", "Lancer la station radio", "Ajouter aux coups de cœur", "Copier le lien".
  - **SoundCloud Reposts & Likes**: Synchronized interaction across the entire app.
- **Windows 11 Integration & SMTC**: Full System Media Transport Controls integration. Control playback from your keyboard media keys, the Windows 11 Action Center sound widget, or your lock screen with high-resolution album artwork.
- **Discord Rich Presence (RPC)**: Zero-dependency direct named-pipe IPC client (`\\.\pipe\discord-ipc-0`) broadcasting your current track, artist, album art, and live progress timestamps straight to your Discord profile using the verified registered SoundCloud Discord application ID (`463097721130188830`).
- **Frictionless Authentication**: Guest mode with instant public `client_id` resolution (listen immediately without an account), plus a one-time ephemeral login window capturing your `oauth_token` securely into Windows `PasswordVault`. Once logged in, your likes, library, profile bio editing, and stream are synced natively forever.
- **Instant Launch & Native Footprint**: Launches immediately, sips negligible RAM, and leaves your CPU and battery in peace.

---

## Architecture

```
SoundWin.sln
│ src/
│   │ SoundWin.Core/          Standalone .NET class library (SoundCloud v2 API, AudioPlayer, SMTC, RPC, Auth)
│   │ SoundWin.App/           WinUI 3 Modern Windows Application (MVVM, Mica, Waveform, Controls, Stream)
│   │ SoundWin.Installer/     Pure WinUI 3 setup wizard
│   │ SoundWin.Cli/           Diagnostic CLI runner for API, stream resolution, and audio testing
```

---

## Installation & Distribution

### 1. Single-File WinUI 3 Setup (`SCwin-Setup-v0.1.0-win-x64.exe`)
Download and run the single executable installer. It is a genuine Windows 11 setup wizard written in pure WinUI 3 that unpacks natively, creates a Desktop shortcut, registers in your Start Menu, and gets out of your way.

### 2. Standalone Portable (`SCwin-v0.1.0-win-x64-portable.zip`)
For minimalists and portable USB drives. Unzip anywhere, run `SoundWin.App.exe`, and you're good to go.

### 3. Local Native Install Script (`install_local.ps1`)
To compile and install SCwin natively from source on your local machine:
```powershell
.\install_local.ps1
```

---

## Building from Source

### Prerequisites
- Windows 10 (version 1809+) or Windows 11
- .NET 8 SDK
- Visual Studio 2022 (v17.10+) with **.NET Desktop Development** and **Windows App SDK** workloads

### Build & Run
```powershell
# Clone the repository
git clone https://github.com/djell1l/SoundWin.git
cd SoundWin

# Switch to the active development branch
git checkout edge

# Compile and launch in Debug
.\build.ps1

# Compile and launch in Release
.\build.ps1 -Configuration Release
```

---

## Contributors & Credits

AI is the future - why whisper about it on GitHub like it's a dirty secret? This project is 100% vibe-coded and we fully own it: pairing human ambition with cutting-edge AI pair programming to build what multi-billion-dollar companies were too lazy to ship.

<p align="left">
  <a href="https://github.com/djell1l">
    <img src="https://github.com/djell1l.png" width="60" height="60" style="border-radius: 50%;" alt="djell1l" />
  </a>
</p>

- **[djell1l](https://github.com/djell1l)** - Lead developer & creator
- **[Gemini](https://github.com/google-gemini)** ([@google-gemini](https://github.com/google-gemini)) via **[Antigravity](https://github.com/google-deepmind)** ([@google-deepmind](https://github.com/google-deepmind)) - AI pair programming collaborator and architecture co-design
- **[Nuage (macOS)](https://github.com/lerboe/nuage-macos)** by Laurin Brandner ([@lerboe](https://github.com/lerboe)) - Outstanding native macOS SoundCloud client inspiration, UX concepts, and API routing architecture
- **[Single-File SFX Setup Architecture](https://github.com/djell1l/Diswin)** - C# pure WinUI 3 self-extracting bootstrap wrapper and deduplicated installer packaging
- **[SoundCloudExplode](https://github.com/Tyrrrz/SoundCloudExplode)** by Oleksii Holub (Tyrrrz) & Jerro - Lightweight C# SoundCloud API client and stream parser
- **[sctui](https://github.com/Illogicalll/sctui)** by Illogicalll - Outstanding terminal SoundCloud client inspiration and reverse-engineering insights
- **[Soundnode](https://github.com/Soundnode/soundnode-app)** - The early pioneer desktop SoundCloud client concept
- **[soundcloud.py](https://github.com/7x11x13/soundcloud.py)** by 7x11x13 - Python library referencing internal SoundCloud v2 endpoints
- **[Diswin](https://github.com/djell1l/Diswin)** - Sibling native Windows 11 client establishing this ecosystem standard
- **[Files](https://github.com/files-community/Files)** - UI/UX inspiration for WinUI 3 modern controls and Fluent design patterns

---

## License

This project is licensed under the **GNU General Public License v3.0 (GPL-3.0)**. See the [LICENSE](LICENSE) file for complete details.

Under this strong copyleft license:
- Any fork, derivative work, or redistribution **must remain completely free and open-source**.
- It is strictly prohibited to take this code, make it proprietary, or re-publish it in closed-source format without giving full attribution and releasing the modified source code under the same GPL-3.0 license.

---

## Disclaimer

SCwin is an unofficial third-party client built for experimental and educational purposes. SoundCloud is a registered trademark of SoundCloud Global Limited & Co. KG. This project is not affiliated with, endorsed by, or associated with SoundCloud. Use at your own discretion.
