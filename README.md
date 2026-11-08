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
- **New-app detection** – when an unknown app starts playing sound, a popup asks which profile it belongs to. Can be turned off.
- **Automatic sorting** (on by default) – games, browsers, music and chat apps go to the right profile by themselves; anything unknown still gets the popup.
- **OSD** – shows volume and mute per app, profile switches, Caps/Num/Scroll Lock and position switches.
- **Media flyout** – title, artist, cover, progress bar (click or drag to seek), previous / play-pause / next, skip ± (10 s, adjustable), a “…” menu and a source bar when several apps play at once. Can stay on screen for programs you choose, e.g. while Spotify is playing.
- **9 OSD designs and 9 media flyout designs** – from the ModernFlyouts and Windows 11 look to Dynamic Island, macOS, Material You, Gaming HUD, Hardware knob and Knob, plus fully customizable designs. Every flyout design can be customized and saved under its own name.
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
- **Update:** simply run the newer setup – it detects the installed version, and one click on **Update** is enough. Folder, settings, Spotify sign-in, autostart and shortcuts stay as they are; KnobFlyout is closed for a moment and can be restarted at the end. “Change folder or options …” is still there if you want something else.
- **Coming from ModernFlyout** (the old name): the setup takes over your settings and the Spotify connection and removes the old version.
- **Uninstall:** Windows Settings → Apps → Installed apps → KnobFlyout → Uninstall. This removes the program folder including the settings.

## Introduction

On the first start, a short introduction opens (the new-app popup waits until it is closed). It explains the knob, profiles and features and sets them up with you right away. The previews are live: they react to your knob (or the − / + buttons below them) and show what's really playing, and their buttons really work. Everything you change there applies immediately.

In the step “Sort by hand” you try out the popup; next to it you see which logo belongs to which profile. You can go on without clicking.

The steps: turn and press · the profile key · profiles · your own profiles (the ready-made ones can be changed and deleted too) · sort automatically · sort by hand · media flyout · pick a look (customize it right in the introduction: template, layout, colors, opacity) · place on screen. Snap points are under “Show all features”.

At the end you choose: **Go to the program**, **Tour of the settings** or **Show all features**. The tour (also under **Settings → General → Tour of the settings**) guides you through the real settings window, all the way to Spotify and YouTube: step by step it highlights a setting and explains it in a speech bubble (Back, Next, ✕ or Esc ends it). Show all features is an overview of everything KnobFlyout can do, where each feature can be tried and set up right away.

You can skip it at any time. It comes back when you open the settings until you tick **Don't show again**. To see it again: **Settings → General → Introduction**.

## Controls

### Keys

| Action | Default key | What happens |
|---|---|---|
| Turn the knob up / down | Volume Up / Volume Down | Changes the volume of the apps in the active profile (2 % per notch) |
| Press the mute key | Mute | Mutes / unmutes these apps |
| Volume up / down, mute on keys of your own | not set | Works exactly like the knob (holding repeats); set in **Volume** or **Shortcuts**. If the profile lets Windows control the volume, Windows gets the real volume key |
| Switch profile | **Ctrl + Shift + P** (tap) | Next profile, shown in the OSD |
| Next saved position | **Hold** Ctrl + Shift + P (600 ms) | Moves OSD and flyout to the next saved position (needs saved positions and “Drag with the mouse”); keep holding to continue |
| Skip forward / back | not set | Jumps in the current track by the **skip distance** (5–60 s, default 10 s; also for the ± buttons in the flyout). Works while something plays and up to 10 s after pausing (each jump restarts the 10 s). Otherwise the key goes to the app in the foreground, e.g. a game. **Skip only while playing** off (Media flyout or “…” menu): the keys always skip. Videos in the browser skip through the browser extension when it is connected (also when the browser reports no timeline to Windows), otherwise through Windows |
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
| Tray icon | Left-click → **Settings**, right-click → **Settings** or **Exit** |
| Start KnobFlyout.exe | Opens the settings, also while KnobFlyout is already running (started with Windows it stays in the background) |
| OSD / media flyout | Drag to move (option “Drag with the mouse”); snaps to saved positions |
| OSD / media flyout | Click to hide (option “Click to dismiss”) |
| Media flyout | Previous, Play/Pause, Next, −10 s / +10 s |
| Progress bar | Click or drag to jump |
| “…” button | Menu with shuffle, repeat and more (see below) |
| Source bar | Click a source to switch to it (the previous one pauses); the small pause dot pauses a source without switching (bar and dot can be switched off) |
| New-app popup | Profile button = assign, ⊘ = exclude the app |

## Profiles

A profile is a group of apps the knob controls together – for example *Gaming* (game), *Comms* (Discord, Teams), *Music* (Spotify), *Video* (browser, VLC).

- **Assign apps:** page **Profiles & apps** → app list: each row has one button per profile and ⊘ to exclude the app. Or directly in the new-app popup.
- **Sort automatically** (page **Profiles & apps**, or in the introduction): new games (Steam, Epic, GOG, Xbox, the Windows game list), browsers and video players, music apps and chat apps go to the profile you chose for that kind of app, without asking. Anything KnobFlyout doesn't recognize, or a kind set to “Ask”, goes to the popup as usual. Apps that are already assigned stay where they are.
- **New-app popup:** appears when an app plays sound that is not assigned yet. Click a profile button or press its assign key; ⊘ excludes the app. Without a click it disappears after 12 seconds and the app is suggested again later. To turn it off: **General** or **Profiles & apps** page → **Show assignment OSD**. Sorting automatically keeps running, all other new apps stay without a profile until you assign them in the app list.
- **Edit profiles** (page **Profiles & apps**): up to 6 profiles; name, logo (44 Windows symbols or your own image), color, delete, add. The order is the switching order. At least one profile remains. Per profile you choose what the knob controls: **the apps in the profile**, **the Windows volume with the KnobFlyout OSD** (KnobFlyout controls the master volume) or **the Windows volume with the Windows OSD** (the volume keys pass through to Windows). Also when creating a profile in the introduction.
- **Lite mode** (General → Mode, or on the first page of the introduction): the knob only controls the Windows volume, plus the media flyout; no profiles. **Show OSD** (General) turns the KnobFlyout OSD off, then turning the knob shows the normal Windows volume indicator (also for profiles with "Windows volume with the KnobFlyout OSD"). The settings then only show what still applies (no Profiles & apps, Spotify, browser add-on), and the introduction has only 5 steps (Welcome, media flyout, look, position, all set).
- **Controlled apps:** “Only apps playing sound” (default) or “All apps in the profile”.
- **Skip empty profiles:** switching only goes to profiles with running apps.

## OSD (volume display)

The OSD appears when you turn the knob, switch profiles, press a lock key or switch positions. You can choose the text for the lock keys (Caps Lock, Num Lock, Scroll Lock) under **General → Show lock keys** (expand). If several apps are changed, the first one is shown with “+N”.

- On / off, duration 0.5–10 s
- **Design:** below it **Your own designs** (save design, layout, colors, opacity and corners under a name) and **Color profile**
- **Customize the design** opens an editor with a preview of all four cards at the top and the tabs **Template · Layout · Colors · Opacity**. As with the media flyout, everything is a draft and is kept with **Save as your own design**:
  - **Templates:** Standard, Compact (without the app name), Minimal (only the essentials), Mirrored
  - **Layout** per card (volume, profile, lock key, position): every part such as icon, name, bar, number or on/off. The arrows change the order (top of the list = left on the card), the switch hides a part. **Alignment** for all cards: as designed, left, center or right
  - **Colors** of the chosen design: background, text, secondary text, accent, button fill, bar and border one by one; design “Custom” has its detailed colors including the position card
  - **Opacity** of the background and of text/icons (corners too for the classic designs), saved with your own design
- Corners for the classic designs
- **Color profiles** are the same for OSD and media flyout and work in every design. The **Color profile** row picks one (↺ = the design's colors, the bin deletes the selected one after asking); with **Color profiles for OSD and flyout together** a selected profile applies to both at once. New color profiles are made in **Customize the design** → Colors or **Customize the colors of both** with **Save as color profile …**; there, picking a profile applies it right away
- Opacity separately for the background (0–100 %) and text/icons (20–100 %) – in every design, also for the new-app popup; set in **Customize the design** → Opacity
- 👁 button next to the design: live preview of all views next to the settings window

## Media flyout

The media flyout appears when a track changes or playback starts or pauses – for every app that reports media to Windows (Spotify, browsers, media players, …).

- **Content:** app name and logo, cover, title and artist, progress bar with times, Previous / Play-Pause / Next, skip ± buttons
- **Source bar:** when several apps play at the same time, one entry per app – click to switch. It can be switched off, and so can the pause symbol next to playing sources (**Customize the design** → Layout)
- **“…” menu** (items selectable): Shuffle, Repeat, Next source, Keep on screen on/off (for the app that is playing), Browser extension on/off, skip keys on/off, Skip only while playing on/off, Keep pinned, Save position, Next position, Reset position, Open settings, Hide Optionally a **right click on the flyout** opens the menu too (Media flyout → “…” menu).
- **Keep on screen** (page **Media flyout** → **Keep on screen**): click the programs it should stay on screen for, for example Spotify. Choose whether it stays **while playing** (hides when paused) or **while open** (also when paused). If another program starts playing, the flyout shows it briefly and disappears; it comes back once that program stops. With **Own** position it has its own spot, chosen by tile or by dragging the flyout; it is then not attached to the OSD, and the OSD moves out of its way. Works for every design.
- **Options:** on / off, duration 2–30 s, progress bar, skip ± buttons, opacity separately for background and text/icons (in **Customize the design** → Opacity)
- **Customize any design:** Appearance → **Customize the design** opens an editor with a preview at the top (the sources bar is always shown there; **Live flyout** switches it to what's really playing, with working buttons; for the OSD, **Live OSD** makes the volume card follow the real knob; **Keep on screen** shows OSD and flyout with the draft at their real place until you switch it off or leave the page) and the tabs **Template · Layout · Colors · Opacity · Background**, for every design, for example Dynamic Island or Gaming HUD:
  - **Everything here is a draft:** neither the preset nor a template is changed. You save with **Save as your own design**; if you leave with unsaved changes, KnobFlyout asks whether to save or leave without saving
  - **Templates** as a starting point: Standard, Compact, Minimal, Large album art (your colors are kept). ↺ next to a value returns it to the chosen template
  - **Your own designs** hold the layout, opacity, corners, colors and the cover background. Choose and delete them: **Media flyout** page → **Your own designs**. You keep editing a chosen design here, the same name overwrites
  - **What should be shown?** – all sections in the order of the flyout: app row, title and album art, timeline, sources bar, buttons. The switch shows or hides a section, ↑ ↓ changes the order, clicking the row shows more
  - single elements as tiles on/off: title, artist, album art · previous, skip ±, play/pause, next, “…”
  - plus album art position and size, title size, times, “…” at the top right, button size and alignment; round or flat buttons with ModernFlyouts, Windows 11 and “Custom”
  - **Colors** for every design: background, text, secondary text, accent, buttons, bar and border, one by one, each either fixed or taken from the album art (picture button); ↺ returns a color to the design. The Hardware knob also has its display colors. Dynamic Island's waves and Material You's base color follow the album art until you pick a fixed color. Color profiles work across all flyout designs
  - **Background:** album art as a blurred background, blur and dimming adjustable. It belongs to the design: a preset shows it off
  - Some parts are built into a design: they can be switched off but not moved, such as the timeline in the Hardware knob's display

## Position

Page **Position**:

- **Arrangement:** Separate · Media flyout above the OSD · Media flyout below the OSD. Together, both move as one block.
- **Place on screen:** a preview of each monitor with 9 tiles (top / middle / bottom × left / center / right) or “Custom position”. With “Separate”, one choice for both: **left click sets the flyout, right click the OSD** (also on “Own position”); the tiles show what is where. The dropdown (each monitor once, the main one marked “(main)”) picks the monitor shown, a click puts the part on that monitor; if one is on another monitor, that is noted below the preview. Clicking a tile briefly shows the OSD and flyout at that spot; in the tiles the OSD is blue, the flyout yellow.
- **Test position:** shows both briefly exactly where they will appear.
- **Saved positions:** tab **By tile** (with “Separate” one choice: left click flyout, right click OSD) and **Own position** (saves the spot the OSD and flyout were dragged to with the mouse). The tiles show every saved position with its number (“1 OSD”, “1 Flyout” …), above them the numbers to test and remove. Older positions without a tile are listed as “own position”. Cycle through them by holding the profile key.
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
| Knob (plain knob with level arc, no display) | Knob |

OSD and media flyout designs can be chosen independently. All appearance settings are also collected on the page **Design**. There, **Customize the colors of both** opens a page with all four OSD cards and the flyout at the top (with Live OSD, Live flyout and Keep on screen) and the colors of both: **Same colors for OSD and flyout** sets both with one row of colors at once (switching it on gives the OSD the flyout's colors); switched off, the colors of both are listed separately. The colors apply with “Save”; you can keep them as a color profile (one choice each for OSD and flyout). ↺ next to a setting resets it to the default. Your own designs and color profiles are only deleted after asking. At the bottom right, the **↶** button (undo) discards all settings not saved yet (a draft in “Customize the design” stays); it is greyed out while there is nothing to discard.

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
- **Skip** (hotkeys and ± buttons) jumps right in the video (extension 1.2 or newer), even when the browser reports no timeline to Windows. Live streams, Netflix and older extensions skip through Windows as before.

Setup (page **YouTube**, details in the [README in `Browser`](Browser/README.md)):

1. **Firefox:** on the [Releases](../../releases) page, click the signed `.xpi` and confirm the installation.
2. **Chrome, Opera, Opera GX:** get the folder `Browser/chrome` from this repo, then `chrome://extensions` or `opera://extensions` → Developer mode → **Load unpacked** → pick that folder. Reload open YouTube tabs afterwards.
3. Turn on **Control volume in the YouTube player** and click **Save**. The status shows for each browser when its extension is connected.

The extensions talk to KnobFlyout only locally (`ws://127.0.0.1:8975`, only connections from browser extensions are accepted).

## Settings overview

Open by left-clicking the tray icon, via right-click → **Settings**, or by starting KnobFlyout again. Changes apply when you click **Save**, which stays grayed out until something has changed (language and color scheme apply immediately).

**Search** at the top next to the three lines: finds every setting by title, section, page or a word from its description, also with typos or related terms (e.g. "darkmode" → light or dark). Arrow keys + Enter or a click opens the page, expands the card and briefly highlights it.

| Page | Contents |
|---|---|
| General | Language, light or dark, settings design (Classic, Midnight with a gradient of two colors of your choice, Tiles in light or dark; also in the introduction under “Pick a look”), animations on / off, lite mode (+ show OSD), assignment OSD on / off, hide Windows flyout (Windows 10 only: the volume and media indicator of Windows), start with Windows, introduction, reload settings |
| Volume | Step size, Spotify step size, own hotkeys for volume up / down / mute |
| Profiles & apps | Switch key, controlled apps, skip empty profiles, edit profiles, assignment OSD on / off, sort automatically, assign keys, app list with profile assignment, ignored apps |
| OSD | On / off, duration, click to dismiss, design (customizable: layout of every card, colors, opacity, templates, your own saved designs) |
| Media flyout | On / off, duration, keep on screen, “…” menu, design (customizable with colors, opacity, templates, cover background and your own saved designs), skip keys, gestures |
| Position | Arrangement, place on screen, test, drag, saved positions, reset |
| Design | All appearance settings in one place |
| Shortcuts | All keys and hold / double-press times in one place |
| Spotify | Setup guide, connect, test, disconnect |
| YouTube | YouTube player in Firefox and Chrome on/off, connection status per browser, setup guide |
| Info | Paths of the configuration and log file, a click opens the folder |

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
