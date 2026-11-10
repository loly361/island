# Island installer

Requires Inno Setup 6: `winget install -e --id JRSoftware.InnoSetup`.

Build from the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
```

The script runs `dotnet publish` into `installer/payload`, compiles `installer/Island.iss`
and puts `Island-Setup.exe` and `Island-Setup.exe.sha256` into `installer/output`.

Parameters:
- `-Version 1.2.0`: the version (taken from `src/Island/Island.csproj` by default);
- `-SkipPublish`: do not publish, use a ready-made folder;
- `-PayloadDir <folder>`: where to take the program files from. Without `-SkipPublish` the folder is cleared
  before publishing, so it must be inside `installer/`; otherwise the script stops with an error.

## What the installer does

- Installs Island for the current user, without administrator rights, into `%LOCALAPPDATA%\Programs\Island`.
- Adds a Start menu shortcut and, optionally, a desktop shortcut.
- The "Start with Windows" task (on by default) writes
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Island` = `"<folder>\Island.exe"`,
  the same way the tray menu item does.
- Uninstalling removes that key only if it points at this copy.
  Data in `%APPDATA%\Island` is not deleted.
- Requires the .NET 9 Desktop Runtime (x64); if it is missing, the installer offers to open the download page.

The first installation through `Island-Setup.exe` moves the program into `%LOCALAPPDATA%\Programs\Island`
(the shortcut and autostart start pointing there). The old folder Island used to run from
can be deleted after that: settings and data are stored separately, in `%APPDATA%\Island`.

## Silent mode (auto-update)

```
Island-Setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
```

The installer closes a running Island from the install folder, installs the files and starts the new Island.
`/NOLAUNCH` turns the launch off and `/NOICONS` turns the shortcuts off. Silent mode leaves autostart alone:
if the user switched it off in the tray, an update will not switch it on.

Do not change the AppId `{AC7DE9DF-D68E-4599-85A6-968CC9FA163C}`.
