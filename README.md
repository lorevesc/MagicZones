# MagicZones

Magic zones for multi-monitor setups on Windows 10/11. Drag a window and **a popup with a mini-map**
of all your monitors and their zones **appears above the window**. Move the cursor onto a zone in the
popup and release: the window is **thrown** there, even to the farthest monitor, with a flight
animation. Releasing outside the popup = normal move.

There is also an alternative "full-screen zones" mode (menu *Mode*) where zones are drawn
directly on the monitors and you can throw the window with a flick of the mouse.

The interface is in **English and Italian**: by default it follows the Windows display language
(Italian if Windows is in Italian, English otherwise). To force one, right-click the tray icon →
*Lingua / Language*, or set `"language"` in the configuration. The installer picks its language the
same way.

## Build and install

```powershell
.\build.ps1              # Release -> bin\Release\MagicZones.exe
.\build.ps1 -Installer   # + dist\MagicZones-Setup-<version>.exe (requires Inno Setup 6)
```

`MagicZones.csproj` is a classic MSBuild project targeting .NET Framework 4.8 (already included in
Windows, nothing to install to run it). To build it you only need Visual Studio's MSBuild:
`build.ps1` finds it on its own. No NuGet packages, no obfuscator or packer: the exe is a plain IL
assembly with product name, company and version in its file properties (edit them in the `.csproj`).

For everyday use, install it with `MagicZones-Setup-x.y.z.exe`: it goes into
`%LOCALAPPDATA%\Programs\MagicZones`, without administrator rights, with a Start menu shortcut, a
"Start with Windows" option and uninstall from Settings > Apps. Launching the exe a second time opens
the zone editor in the instance that is already running.

## Usage

### Popup mode (default)

| Action | What happens |
|---|---|
| Drag a window | The mini-map appears above the window (or over its content, if the window is at the top of the screen) |
| Cursor over a zone in the popup | The zone lights up, an arrow shows the throw and a preview of the destination appears on the real monitor |
| Release on the zone | The window flies there and snaps into place (even on another monitor) |
| Release outside the popup | Normal move, the popup fades out |
| Move away from the popup | It becomes transparent so it doesn't get in the way |
| `Ctrl` while passing over several zones | Merges them: the window fills all of them |
| `Shift` while dragging | Closes the popup (the opposite in "Only while holding Shift" mode) |
| Monitor without zones | Shown in the popup as a single full-screen zone |

### Full-screen zones mode

| Action | What happens |
|---|---|
| Drag a window | Zones appear on all monitors and the one under the cursor lights up |
| Release over a zone | The window snaps into the zone (with the configured gap) |
| **Throw** (release while moving) | The window flies to the zone in that direction; orange zone = destination |
| `Ctrl` while dragging | Merges several zones: the window fills all of them |
| `Shift` while dragging | No zones, normal move (the opposite in "Only while holding Shift" mode) |

### Always active

| Action | What happens |
|---|---|
| Drag a snapped window out | It goes back to its original size |
| `Ctrl+Alt+Win+←↑→↓` | Moves the active window to the neighbouring zone (across monitors too) |
| `Ctrl+Alt+Win+Z` or double-click the tray icon | Opens the zone editor |

### Flick throw (full-screen mode only): how it works

While you drag, the cursor speed over the last 80 ms is measured. If it exceeds `throwMinSpeed`
(px/s) on release, the window keeps going along its trajectory for `speed × throwMomentum` pixels
and lands in the last zone it crossed. A strong throw hits the edge of the desktop, empty gaps
between monitors are flown over. If you stop before releasing, it is a normal drop.

### Zone editor

Opens full screen on all monitors.

- Drag on empty space to draw a zone. Drag edges and corners to resize, the center to move.
- Edges snap (magnet) to screen edges, other zones, halves/thirds/quarters. `Shift` disables the magnet.
- **Double-click** or `T` changes the zone type: `SNAP` (snap into place), `MAXIMIZE` (maximize on that monitor), `MINIMIZE` (minimize, a "trash" zone). In Italian: `MASSIMIZZA`, `MINIMIZZA`.
- Right-click or `Del` deletes, `Ctrl+Z` undoes, `H` hides the toolbar.
- `Enter` saves, `Esc` exits (asks for confirmation if there are unsaved changes).
- Ready-made presets: 2/3 columns, 25|50|25, Main + 2, 2×2, 2/3 rows (handy on a vertical monitor).

## Configuration

`%APPDATA%\MagicZones\config.json` (from the tray icon menu: *Open config folder*, then
*Reload config*). Zones are stored as fractions of each monitor's work area, so they survive
resolution changes. Monitors are identified by hardware ID, so zones stay on the right monitor even
if the numbering changes.

| Key | Default | Meaning |
|---|---|---|
| `language` | `"auto"` | `"auto"` = Windows display language, `"it"` = Italian, `"en"` = English |
| `mode` | `"popup"` | `"popup"` = mini-map above the window, `"overlay"` = full-screen zones |
| `popupWidth` | `460` | Popup width (px at 100%, scaled with DPI) |
| `activation` | `"always"` | `"always"` = popup/zones on every drag, `"shift"` = only while holding Shift |
| `throwEnabled` | `true` | Flick throw (overlay mode only) |
| `throwMinSpeed` | `2600` | Minimum speed on release to count as a throw (physical px/s) |
| `throwMomentum` | `0.35` | Flight distance = speed × this value (seconds) |
| `animate`, `animationMs` | `true`, `220` | Landing animation |
| `gap` | `8` | Gap between windows (px at 100%, scaled with the monitor's DPI) |
| `restoreSizeOnUnsnap` | `true` | Restores the original size when you drag the window out of the zones |
| `hotkeys` | `true` | `Ctrl+Alt+Win+…` shortcuts |
| `accentColor`, `throwColor` | blue, orange | Overlay colors |
| `excludedProcesses` | `[]` | Processes to ignore, e.g. `["Photoshop", "obs64"]` |
| `debugLog` | `false` | Traces every drag in the log (for troubleshooting) |

Error log: `%APPDATA%\MagicZones\log.txt`.

The window flight: the window takes the zone's size immediately and then only moves (no resizing on
every frame, which makes the content "wobble"), slowing down on landing without bouncing. If you grab
it again while it's flying or right after it lands, MagicZones lets go of it immediately.

## Notes

- Windows of programs started **as administrator** (e.g. Supremo, Task Manager, an admin terminal)
  cannot be moved by a normal process: this is a Windows restriction (UIPI). MagicZones detects
  them: the popup says so in yellow and explains what to do on release. If you need it, right-click
  the tray icon → **Restart as administrator** (one UAC prompt, lasts until you exit). Apps running
  as SYSTEM remain untouchable even then.
- **Autostart**: only through the `MagicZones` value in
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, written by the installer or by the
  *Start with Windows* menu item (which also removes it). It is only enabled for the copy installed
  in `%LOCALAPPDATA%\Programs` or `Program Files`, never from Desktop, OneDrive, Downloads or Temp.
  No scheduled tasks and no running as administrator: MagicZones doesn't need them.
- If Windows 11 Snap Layouts (the menu that appears when dragging to the top) get in the way, you can
  turn them off in *Settings > System > Multitasking*.

## Antivirus (Microsoft Defender)

A previous version was deleted by Defender (`Behavior:Win32/Execution.A!ml`, a false positive): an
unsigned exe, launched from a OneDrive folder, ran a hidden `schtasks.exe` to create a scheduled task
with highest privileges. Now the app:

- never launches `schtasks`, `cmd` or `powershell` and never creates scheduled tasks;
- never copies or reinstalls itself: installation is handled by `MagicZones-Setup`;
- writes the autostart entry only on request, in the user's Run key, pointing to the installed copy;
- runs `asInvoker` (no administrator prompt at startup).

To further reduce false positives: sign the exe and installer with a code signing certificate (the
`SignTool` directive is already prepared in `installer\MagicZones.iss`) and, if Defender still flags
something, submit the file to Microsoft as a false positive
(https://www.microsoft.com/wdsi/filesubmission). Don't create Defender exclusions to work around it.

## Development

```
MagicZones.csproj   MSBuild project (file identity, Release in bin\Release)
installer/          MagicZones.iss: per-user Inno Setup installer
src/
  Program.cs        entry point, single instance, --selftest / --preview / --export-icon
  TrayApp.cs        tray icon, menu, hotkeys, monitor changes
  Lang.cs           UI strings (Italian/English) and language detection
  Startup.cs        autostart (HKCU Run key) and stable path check
  Integrity.cs      detects windows of apps with higher privileges (UIPI)
  DragTracker.cs    WinEvent hook on drag, measures speed, decides drop/throw
  Popup.cs          mini-map above the dragged window
  ZoneManager.cs    zones in pixels, hit testing, throw physics, neighbouring zone
  WindowMover.cs    SetWindowPos with invisible border compensation, animation, DPI settling
  Overlay.cs        per-monitor overlay while dragging
  Editor.cs         full-screen zone editor
  LayeredWindow.cs  per-pixel alpha windows (UpdateLayeredWindow + DIB)
  Config.cs, Json.cs, Monitors.cs, Geometry.cs, Support.cs, Native.cs
```

- `bin\Release\MagicZones.exe --selftest | Out-String` runs the logic tests (JSON, zones, throw, startup paths, language) on a synthetic desktop.
- `bin\Release\MagicZones.exe --preview .\preview [it|en]` saves overlay, popup and editor of each monitor as PNG without showing them, optionally in a given language.
- `tools\DragHarness.cs` is an end-to-end test: it opens a test window and drags it with the real mouse
  (SendInput) while MagicZones is running: drop at rest, window at the top, maximized, popup with a
  change of mind, throw, size restore, grabbing right after landing. It moves the mouse for about
  30 seconds and only clicks on its own window.
