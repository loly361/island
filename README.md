# Island

English | [Русский](README.ru.md)

A "Dynamic Island" for Windows: a black pill at the top centre of the screen with the clock and the weather. Hover the mouse over it and it expands into a panel.

The interface is available in Russian, French and English (Settings, then Language; a restart is needed to apply it).

## Install and run

Download `Island-Setup.exe` from the [latest release](https://github.com/loly361/island/releases/latest) and run it. It installs Island for the current user into `%LOCALAPPDATA%\Programs\Island`, adds a Start menu shortcut and, if you leave the box ticked, starts Island with Windows. A copy installed there can be moved anywhere: autostart rewrites its path on every launch. After starting `Island.exe`, an icon appears in the tray (bottom right corner). The .NET 9 Desktop Runtime is required; the installer offers to open the download page if it is missing.

Island checks GitHub for new releases (Settings, then Updates), verifies the SHA-256 of the installer and installs the update silently. The check can be turned off. It works only while the repository is public.

Build from source:

```
dotnet build src\Island -c Release
dotnet test tests\Island.Tests
```

To build the installer see [installer/README.md](installer/README.md).

## Features

| Tab | What it does |
|---|---|
| Home | player (whatever is playing in Windows) and volume, quick actions, program shortcuts |
| Clipboard | copy history with search; click pastes, Shift+click only copies; pin and delete entries |
| Notifications | Windows notifications arrive as a banner on the island and pile up in a list (kept in memory only, never written to disk) |
| Notes | tabbed notes; bold, italic, strikethrough, lists, tasks with checkboxes (`☐` → `☑`, click the box), saved automatically. On the right: a timer (5/10/15/25/45 minutes or a custom time, keeps running while the island is collapsed) and reminders |
| Weather | now, the next 8 hours, 7 days, wind, humidity, precipitation, sunrise and sunset (Open-Meteo) |
| Controls | Wi-Fi, Bluetooth, dark theme, VPN (Windows connections), lock, sleep, restart and shutdown (press twice) |
| Search | `Alt+Space` (`Ctrl+Alt+Space` if that is taken): programs, files from the Windows index, "Ask AI" (claude.ai) and web search |
| Settings | mode (standard or minimal), game mode, language, automatic update check, city and coordinates for the weather, search and "Ask AI" addresses, accent colour (from the wallpaper or one of four), links to the settings file and the data folder. Changes are saved immediately and an invalid value is rolled back |

Quick actions: area screenshot, full-screen screenshot (both are copied to the clipboard and saved to `Pictures\Screenshots`), text from the screen (Windows OCR, languages are taken from your profile), colour picker (HEX colour to the clipboard), plain text (strips formatting from the clipboard), screen recording, and folders with screenshots and videos.

Screen recording: the "Screen recording" tile, then "Area" (select with the mouse, as for a screenshot) or "Full screen" (primary monitor). While recording, the island shows a red dot with the time as `mm:ss`, and the header of the open panel has a stop button. You can also stop it with the same tile ("Stop") or with "Stop recording" in the tray icon menu. The video is saved to `Videos\Island\Island-YYYY-MM-DD-HH-mm-ss.mp4` (H.264, 30 frames per second) and the island shows "Recording saved"; clicking the banner opens Explorer with the file selected. The mouse cursor is recorded, the island itself is not. Sound is not recorded. Recording stops on its own after 30 minutes, when disk space runs low (under 200 MB) and when the resolution or the monitor changes; whatever was recorded so far is kept. If you quit Island during a recording, the file is finished before the app exits. Nothing needs to be installed: it uses GDI and the encoder built into Windows (Media Foundation), on the GPU if it supports H.264.

Game mode (on by default, switch in Settings): while a full-screen window is open on the primary monitor (a game, a full-screen video, a presentation), the island hides, does not expand on hover and shows no banners or sounds. Notifications stay in the list, and banners for the timer, reminders and notifications queue up and are shown one by one (with sound) as soon as the full-screen window is closed or minimised. Maximised ordinary windows with a title bar, the desktop, Alt+Tab and Start are not treated as full-screen. A running screen recording is not interrupted by game mode. "Open" in the tray menu shows the island even above a full-screen window.

Do not disturb (Settings or the tray menu): no banners for Windows notifications and for programs that post to the island; the timer and reminders still show. On the Notifications tab the speaker button next to a notification turns banners from that app off or on; such notifications still appear in the list.

Reminders are written as an ordinary phrase, in Russian, French or English: "call mom tomorrow at 9", "pills every day at 23:00", "in 20 minutes turn off the stove", "friday at 6:30pm meeting". When the time comes the island shows a banner and plays a sound.

`Esc` closes the panel. The pin icon at the top right keeps the panel open.

Hotkeys: `Ctrl+Alt+S` takes an area screenshot, `Ctrl+Alt+R` starts a full-screen recording or stops the current one. If another program already uses a combination, it simply does not work in Island.

## Claude Code notifications

Island listens on `http://127.0.0.1:47831/notify` (local machine only). The request is a `POST` with the JSON body `{"title": "...", "message": "..."}` and an `X-Island-Token` header. The token is generated on first launch and stored in `settings.json` (field `NotifyToken`). Without the token the island answers `401`; GET requests and requests from a browser (with an `Origin` header) are rejected. A body larger than 8 KB is rejected even if no length header is given.

To make the island show "Claude Code is waiting for your answer", add a hook to `~/.claude/settings.json` (replace the script path with your own). The script reads the token from `%AppData%\Island\settings.json` itself, so nothing needs to be copied:

```json
{
  "hooks": {
    "Notification": [
      {
        "hooks": [
          {
            "type": "command",
            "command": "powershell -NoProfile -ExecutionPolicy Bypass -File \"C:\\path\\to\\Island\\tools\\claude-hook.ps1\""
          }
        ]
      }
    ]
  }
}
```

## Look and feel

- A clean black island with a thin outline, translucent white cards and large rounded corners, as in iOS.
- Icons are a single monochrome outline set in the style of Apple SF Symbols; their colour always follows the surrounding text colour.
- The accent colour comes from the wallpaper: the brightest shade is picked and it changes smoothly when the wallpaper changes. If the wallpaper is grey, the Windows accent is used. You can fix the colour in the Settings tab.
- The collapsed pill shows the cover of the track that is playing (if there is one) and a small timer ring with the countdown. The pill stretches to fit them.
- The island expands with a light spring, tabs switch with a sliding indicator and the tab content slides in smoothly. Buttons are slightly pressed in when clicked.
- Weather: the card background depends on the conditions (clear, cloudy, rain, snow, thunderstorm, night) and the week bars are tinted by temperature.

## Data and privacy

Everything is stored in `%AppData%\Island`: `settings.json`, `clipboard.dat` (the clipboard history, encrypted with Windows tools for your account), `notes.json`, `error.log`. Unpinned clipboard entries older than a day are deleted at startup; pinned ones stay. An old plain-text `clipboard.json` is moved into the encrypted file on first launch and deleted.

- Copies from password managers that mark the data as excluded from history are not recorded by the island.
- You can clear the history with the trash button on the Clipboard tab; pinned entries stay.
- Weather comes from open-meteo.com. The city is detected once by IP through ipwho.is and the coordinates are saved to `settings.json`. You can enter your own `Latitude` and `Longitude`.
- The update check calls the GitHub API (`api.github.com`) without a token and sends nothing about you.
- `settings.json` also holds the `SearchUrl` and `AskUrl` links, the `NotifyPort` port and the `Shortcuts` list.

## Limitations

- QR codes and a command field are not implemented.
- Screen recording has no sound. Games in exclusive full-screen mode are not always recorded (often you get a black frame): run them in borderless windowed mode. If a game changes the screen resolution when it starts, the recording stops and is saved.
- Game mode only watches the primary monitor, where the island lives.
- Windows notifications cannot be read if the program sits in a folder with a very long path (about 200 characters or more): this is a Windows limitation. Keep `Island.exe` in a short path.
- The VPN tile only switches connections added in Windows Settings. Third-party VPN clients (WireGuard, Cloudflare WARP and so on) are not visible to it.
- The weather city is detected by IP, and with a VPN it may be wrong. Enter your own `Latitude`, `Longitude` and `City` in `settings.json`.
- The island is drawn on the primary monitor.
- The installer is not code-signed, so Windows SmartScreen may warn about it. The update check compares the installer with the SHA-256 published next to it in the same release.
