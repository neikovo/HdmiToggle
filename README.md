# HdmiToggle

A tiny Windows 11 tray app that turns the picture to the external HDMI display off and on with one click.

## Usage

Run `HdmiToggle.exe` — an icon appears in the system tray.

- **Left click** the icon or press **Ctrl+Alt+H** — turns the HDMI picture off/on.
- **Right click** — menu:
  - when turning on: extend the desktop (default) or duplicate the screen;
  - start with Windows;
  - exit.

The icon is a green monitor when HDMI is on, and a grey crossed-out one when it is off.

### Command line

```
HdmiToggle.exe on       turn the picture on
HdmiToggle.exe off      turn the picture off
HdmiToggle.exe toggle   switch
HdmiToggle.exe status   exit code: 0 = on, 1 = off, 2 = no monitor
```

## Notes

- With no HDMI monitor but another external one (DisplayPort, USB-C dock), the app controls that one.
- If HDMI is the only active display, turning it off moves the picture to the built-in screen.
- Over Remote Desktop the physical displays are not visible, so the app reports "no monitor connected".

## Build

Nothing to install — it uses the C# compiler that ships with Windows (.NET Framework 4.x):

```
build.cmd
```

The app uses the Windows `QueryDisplayConfig` / `SetDisplayConfig` APIs.
