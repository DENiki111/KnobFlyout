# KnobFlyout

**Per-app volume for Windows with a physical volume knob** – profiles, an on-screen display (OSD) and a media flyout.

Turn your knob and KnobFlyout changes the volume of exactly the apps you want (for example only the game, only Discord or only Spotify) instead of the whole system. A small OSD shows what happened, and a media flyout shows what is playing.

🇩🇪 [Deutsche Version](README.de.md)

---

## Contents

- [Features](#features)
- [Requirements](#requirements)
- [Installation](#installation)
- [Introduction](#introduction)
- [Controls](#controls)
- [Profiles](#profiles)
- [OSD (volume display)](#osd-volume-display)
- [Media flyout](#media-flyout)
- [Position](#position)
- [Designs](#designs)
- [Spotify (optional)](#spotify-optional)
- [YouTube in Firefox and Chrome (optional)](#youtube-in-firefox-and-chrome-optional)
- [Settings overview](#settings-overview)
- [Files](#files)
- [Building from source](#building-from-source)
- [Credits](#credits)

---

## Features

- **Volume per app instead of system volume** – the volume knob (or the Volume Up / Down / Mute keys) controls only the apps of the active profile.
- **Profiles** – up to 6 (default: Gaming, Comms, Music, Video), each with name, logo and color. Switch with one key.
- **New-app detection** – when an unknown app starts playing sound, a popup asks which profile it belongs to.
- **OSD** – shows volume and mute per app, profile switches, Caps/Num/Scroll Lock and position switches.
- **Media flyout** – title, artist, cover, progress bar (click or drag to seek), previous / play-pause / next, ±10 seconds, a “…” menu and a source bar when several apps play at once.
- **8 OSD designs and 8 media flyout designs** – from the ModernFlyouts and Windows 11 look to Dynamic Island, macOS, Material You, Gaming HUD and Hardware knob, plus fully customizable designs.
- **Free placement** – 9 places per monitor, multi-monitor support, drag with the mouse, saved positions you can cycle through by key.
- **Spotify Web API** (optional) – controls Spotify's own volume.
- **YouTube in Firefox and Chrome** (optional) – with a small browser extension the knob changes the YouTube player's own volume instead of the whole browser.
- **English and German**, light, dark or system color scheme.
- **Autostart** – start with Windows, switchable in the settings and in Task Manager (“Startup apps”).
- Runs in the notification area, **no administrator rights** needed.

## Requirements

- Windows 10 (version 2004 / build 19041 or newer) or Windows 11, 64-bit
- A knob or keyboard that sends the standard media keys **Volume Up / Volume Down / Mute** (most multimedia keyboards, volume knobs and macro pads do)

The setup brings everything it needs, no separate .NET installation is required.

## Installation

1. Download **`KnobFlyout-Setup.exe`** from the [Releases](../../releases) page and run it.
2. Choose the language (English / Deutsch).
3. Choose the destination folder – default `%LocalAppData%\Programs`. The folder **`KnobFlyout`** is created there automatically.
4. Choose the additional tasks: desktop shortcut, start menu entry (so Windows search finds KnobFlyout), start with Windows.
5. Click **Install**, then **Finish** – KnobFlyout starts and appears in the notification area (next to the clock).

Notes:

- **Windows SmartScreen** may warn because the setup is not signed: click “More info” → “Run anyway”.
- For protected folders such as `C:\Program Files`, the setup offers to restart itself as administrator.
- **Autostart** can be switched on and off later in the settings (page **General**) or in Task Manager under “Startup apps” – KnobFlyout is always listed there.
- **Update:** simply run the newer setup – it suggests the existing folder and keeps your settings.
- **Coming from ModernFlyout** (the old name): the setup takes over your settings and the Spotify connection and removes the old version.
- **Uninstall:** Windows Settings → Apps → Installed apps → KnobFlyout → Uninstall. This removes the program folder including the settings.

## Introduction

On the first start, a short introduction opens. It explains the knob, profiles and features and sets them up with you right away: try the knob and profile key, assign apps, create your own profile, pick a look and a place. Everything you change there applies immediately.

You can skip it at any time. It comes back when you open the settings until you tick **Don't show again**. To see it again: **Settings → General → Introduction**.

## Controls

### Keys

| Action | Default key | What happens |
|---|---|---|
| Turn the knob up / down | Volume Up / Volume Down | Changes the volume of the apps in the active profile (3 % per notch) |
| Press the mute key | Mute | Mutes / unmutes these apps |
| Switch profile | **Ctrl + Shift + P** (tap) | Next profile, shown in the OSD |
| Next saved position | **Hold** Ctrl + Shift + P (600 ms) | Moves OSD and flyout to the next saved position (needs saved positions and “Drag with the mouse”); keep holding to continue |
| Skip forward / back 10 s | not set | Jumps in the current track |
| Next track (gesture) | off | e.g. press Play/Pause twice quickly |
| Switch media source (gesture) | off | e.g. hold Play/Pause |
| Assign key per profile | not set | Only while the new-app popup is open: assigns the app to that profile |
| Caps / Num / Scroll Lock | – | Shows on / off in the OSD |

All keys can be changed in the settings (page **Shortcuts**): click the field, press the new key combination, Esc cancels. Gestures can use their own key or the Play/Pause key; with the Play/Pause key, normal Play/Pause keeps working.

Good to know:

- While KnobFlyout is running, the volume keys **no longer change the Windows master volume** – they only control the apps of the active profile.
- With “Only apps playing sound” (default), the knob only affects apps of the profile that are currently playing. If none is playing, nothing changes.
- Windows does not pass keys to normal programs while a window running as administrator is in the foreground. The knob therefore does not react during that time.

### Mouse

| Where | Action |
|---|---|
| Tray icon | Right-click → **Settings** or **Exit** |
| OSD / media flyout | Drag to move (option “Drag with the mouse”); snaps to saved positions |
| OSD / media flyout | Click to hide (option “Click to dismiss”) |
| Media flyout | Previous, Play/Pause, Next, −10 s / +10 s |
| Progress bar | Click or drag to jump |
| “…” button | Menu with shuffle, repeat and more (see below) |
| Source bar | Click a source to switch to it (the previous one pauses); the small pause dot pauses a source without switching |
| New-app popup | Profile button = assign, ⊘ = exclude the app |

## Profiles

A profile is a group of apps the knob controls together – for example *Gaming* (game), *Comms* (Discord, Teams), *Music* (Spotify), *Video* (browser, VLC).

- **Assign apps:** page **Volume & apps** → app list: each row has one button per profile and ⊘ to exclude the app. Or directly in the new-app popup.
- **New-app popup:** appears when an app plays sound that is not assigned yet. Click a profile button or press its assign key; ⊘ excludes the app. Without a click it disappears after 12 seconds and the app is suggested again later.
- **Edit profiles** (page **Profiles**): up to 6 profiles; name, logo (44 Windows symbols or your own image), color, delete, add. The order is the switching order. At least one profile remains.
- **Controlled apps:** “Only apps playing sound” (default) or “All apps in the profile”.
- **Skip empty profiles:** switching only goes to profiles with running apps.

## OSD (volume display)

The OSD appears when you turn the knob, switch profiles, press a lock key or switch positions. If several apps are changed, the first one is shown with “+N”.

- On / off, duration 0.5–10 s
- Design, corners (classic designs), colors (design “Custom”)
- Opacity separately for the background (0–100 %) and text/icons (20–100 %) – in every design, also for the new-app popup
- 👁 button next to the design: live preview of all views next to the settings window

## Media flyout

The media flyout appears when a track changes or playback starts or pauses – for every app that reports media to Windows (Spotify, browsers, media players, …).

- **Content:** app name and logo, cover, title and artist, progress bar with times, Previous / Play-Pause / Next, ±10-second buttons
- **Source bar:** when several apps play at the same time, one entry per app – click to switch
- **“…” menu** (items selectable): Shuffle, Repeat, Next source, ±10 s hotkeys on/off, Keep pinned, Save position, Next position, Reset position, Open settings, Hide
- **Options:** on / off, duration 2–30 s, progress bar, ±10 s buttons, cover as blurred background (blur and dimming adjustable), opacity separately for background and text/icons
- **Design “Custom”:** Appearance → **Design your own** opens an editor with a live preview at the top:
  - **Templates** as a starting point: Standard, Compact, Minimal, Large album art (your colors are kept)
  - **What should be shown?** – all sections in the order of the flyout: app row, title and album art, timeline, sources bar, buttons. The switch shows or hides a section, ↑ ↓ changes the order, clicking the row shows more
  - single elements as tiles on/off: title, artist, album art · previous, ±10 s, play/pause, next, “…”
  - plus album art position and size, title size, times, “…” at the top right, round or flat buttons, alignment, size and your own colors

## Position

Page **Position**:

- **Arrangement:** Separate · Media flyout above the OSD · Media flyout below the OSD. Together, both move as one block.
- **Place on screen:** a preview of each monitor with 9 tiles (top / middle / bottom × left / center / right) or “Custom position”. Monitor selectable separately for OSD and flyout.
- **Test position:** shows both briefly exactly where they will appear.
- **Saved positions:** add the current position or pick one by tile, test each one, cycle through them by holding the profile key.
- **Reset position** (Advanced) if something is off screen.

OSD and flyout always stay on screen, keep clear of the taskbar (also an auto-hidden one) and do not overlap. After a resolution or monitor change, own positions are converted automatically.

## Designs

| OSD | Media flyout |
|---|---|
| ModernFlyouts (default) | ModernFlyouts (default) |
| Windows 11 | Windows 11 |
| Custom (own colors) | Custom (own layout and colors) |
| Dynamic Island | Dynamic Island |
| macOS | macOS |
| Material You (Windows accent color) | Material You (color from the cover) |
| Gaming HUD | Gaming HUD |
| Hardware knob | Hardware knob |

OSD and media flyout designs can be chosen independently. All appearance settings are also collected on the page **Design**. ↺ next to a setting resets it to the default.

## Spotify (optional)

Without the API, KnobFlyout controls Spotify like any other app through the Windows mixer. With the Spotify Web API, it changes Spotify's own volume slider instead. This requires **Spotify Premium**.

Setup (page **Spotify**, explained step by step there):

1. Open the [Spotify Developer Dashboard](https://developer.spotify.com/dashboard) and click “Create app”.
2. Add the redirect URI **`http://127.0.0.1:5000/callback/`** exactly like this (the settings page has a copy button).
3. Tick “Web API” and save.
4. Copy the Client ID and Client Secret into KnobFlyout.
5. Click **Connect**, agree in the browser – then **Test** checks the connection.

The token is stored in `.spotify_cache` in the program folder. Spotify has its own step size (default 4 %).

## YouTube in Firefox and Chrome (optional)

Normally the knob changes the whole browser session in the Windows mixer. With the YT-Bridge it changes the volume slider of the **YouTube player** instead – only the video, not the rest of the browser. It works without focus, with YouTube in a background tab and with a minimized window, also during ads, on Shorts and in the miniplayer.

- Applies while a YouTube video is **playing** in that browser and the browser is in the active profile. Otherwise the knob controls the browser through the mixer as before.
- Firefox, Chrome and Opera / Opera GX (they use the Chrome extension) can be connected at the same time; each browser session in the mixer (`firefox.exe`, `chrome.exe`, `opera.exe`) controls YouTube in that browser.
- Several YouTube tabs playing: the one started last wins. Mute toggles YouTube's own mute. Step size = normal step size.
- The OSD then shows **YouTube** instead of the browser.

Setup (page **YouTube**, details in the [README in `Browser`](Browser/README.md)):

1. **Firefox:** on the [Releases](../../releases) page, click the signed `.xpi` and confirm the installation.
2. **Chrome, Opera, Opera GX:** get the folder `Browser/chrome` from this repo, then `chrome://extensions` or `opera://extensions` → Developer mode → **Load unpacked** → pick that folder. Reload open YouTube tabs afterwards.
3. Turn on **Control volume in the YouTube player** and click **Save**. The status shows for each browser when its extension is connected.

The extensions talk to KnobFlyout only locally (`ws://127.0.0.1:8975`, only connections from browser extensions are accepted).

## Settings overview

Open via tray icon → right-click → **Settings**. Changes apply when you click **Save** (language and color scheme apply immediately).

| Page | Contents |
|---|---|
| General | Language, light or dark, start with Windows, introduction, reload settings |
| Volume & apps | Step size, Spotify step size, app list with profile assignment, ignored apps |
| Profiles | Switch key, controlled apps, skip empty profiles, edit profiles, assign keys |
| OSD | On / off, duration, click to dismiss, design, opacity, colors |
| Media flyout | On / off, duration, content, “…” menu, design (with editor for “Custom”), opacity, cover background, skip keys, gestures |
| Position | Arrangement, place on screen, test, drag, saved positions, reset |
| Design | All appearance settings in one place |
| Shortcuts | All keys and hold / double-press times in one place |
| Spotify | Setup guide, connect, test, disconnect |
| YouTube | YouTube player in Firefox and Chrome on/off, connection status per browser, setup guide |
| Info | Paths of the configuration and log file |

## Files

All files are in the program folder (default `%LocalAppData%\Programs\KnobFlyout`):

| File | Contents |
|---|---|
| `KnobFlyout.exe` | The program |
| `config.json` | All settings |
| `.spotify_cache` | Spotify token (only with Spotify API) |
| `knob_service.log` | Log for troubleshooting |
| `uninstall.cmd` | Uninstaller (also used by “Installed apps”) |

To start over with default settings, exit KnobFlyout and delete `config.json`.

## Building from source

Requirements: Windows, [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
# Run (Debug)
dotnet run --project KnobFlyout

# Portable single exe → publish\KnobFlyout.exe
publish.bat

# Setup → dist\KnobFlyout-Setup.exe (publishes the app automatically)
dotnet build Installer\KnobFlyoutSetup.csproj -c Release
```

Project layout:

| Folder | Contents |
|---|---|
| `KnobFlyout/` | The app (WPF, .NET 8) – `Audio/` (sessions, Spotify), `Input/` (keyboard hook), `Osd/` (OSD, media flyout, designs), `Media/` (Windows media sessions), `Settings/`, `Tray/`, `Config/` |
| `Installer/` | Setup wizard (WinForms, .NET Framework 4.8) with the app embedded |

Built with [NAudio](https://github.com/naudio/NAudio) (audio sessions), [Hardcodet NotifyIcon](https://github.com/hardcodet/wpf-notifyicon) (tray icon) and the Windows media control API (GSMTC).

## Credits

- Look and media flyout inspired by [ModernFlyouts](https://github.com/ModernFlyouts-Community/ModernFlyouts).
- Started as a Python tool (*KnobEarTrumpet*) and was ported to C#/WPF.
