# Virtual Monitor Window

Virtual Monitor Window is a portable Windows app for viewing a display in a
resizable window.

![Virtual Monitor Window 1.0.6 screenshot](./docs/images/virtual-monitor-window-v1.0.6.png)

## Why this exists

If people or objects block the view of a large screen, its content can be hard to see.
Virtual Monitor Window shows the extended display in a small resizable window, like a
little duplicate monitor.

![Illustration of the problem Virtual Monitor Window is designed to solve](./docs/images/problem-this-was-designed-to-solve.png)

## Features

- Live preview of detected Windows displays, with the visible cursor.
- Optional mouse and keyboard control for VDD virtual displays.
- Create and manage up to 16 VDD virtual displays.
- Light and dark themes.
- Self-contained x64 Windows executable; no separate .NET installation is
  required to run the published app.

## Run

1. Open the [latest release](https://github.com/Full-Quantum/Virtual-Monitor-Window/releases/latest).
2. Download `VirtualMonitorWindow-1.0.6.exe` and the two `Assets` ZIP files
   (or the release archive, if provided), keeping the `Assets` folder next to
   the exe.
3. Run the exe. No installer or separate .NET installation is needed, and the
   app does not need internet access for normal use.

The footer checks GitHub for the latest release on launch; click the version
indicator to open that release, or retry the check if it is unavailable.

Click **Preview only** to hide every control except the live preview and a
small **Exit preview only** button in its top-right corner. The preview then
fills the window edge to edge with no padding.

The bundled display driver is **not installed automatically**. To use virtual
displays, select **Install virtual display driver...** and approve the Windows
administrator prompt. Then choose a monitor count and apply it. The displays
picker lists detected VDD displays first.

The preview is view-only by default. Select a VDD display and enable **Control
the virtual display with mouse and keyboard** to forward input. Keyboard input
is sent to the virtual display after clicking the preview. Press Escape to
turn control off. The install, monitor-count, and input-control options are
under **Show virtual display settings**. Input outside the preview remains
local to Windows.

To build it yourself instead, see [Build](#build).

## Build

Requirements:

- Windows x64
- .NET 8 SDK

From this directory, publish a self-contained executable:

```powershell
dotnet publish .\VirtualMonitorWindow.csproj -c Release -r win-x64 --self-contained true
```

The executable is written to
`bin\Release\net8.0-windows\win-x64\publish\VirtualMonitorWindow-1.0.6.exe`.
The two ZIP packages in `Assets` are copied alongside it and are required for
the driver's installation workflow.

## Self-test

Run the published app with `--self-test`:

```powershell
.\bin\Release\net8.0-windows\win-x64\publish\VirtualMonitorWindow-1.0.6.exe --self-test
```

The self-test verifies the bundled package hashes, enumerates displays,
captures a display frame, and checks the VDD control pipe. It does not install
the driver or change the monitor count.

## Driver and safety notes

The Windows display driver is installed system-wide and is not portable. Moving
or deleting this app does not uninstall the driver. Use the vendor's Virtual
Driver Control tool or Device Manager to remove it.

Virtual display drivers can affect display arrangement and may cause display
priority issues or a black screen around major graphics or chipset driver
updates. Remove the VDD driver before those updates. Keep a physical display
connected and know how to enter Windows Safe Mode before installing a display
driver.

The installer verifies the bundled package hashes and relies on Windows to
validate the driver's signed catalog. It does not add a certificate to
Windows' trusted root or publisher stores.

## Third-party components

This project bundles the Virtual Display Driver and Nefcon helper packages.
Their project links, release versions, and license texts are in
[THIRD-PARTY-NOTICES.txt](./THIRD-PARTY-NOTICES.txt).

The Virtual Monitor Window source code, documentation, and included
illustration are available under the MIT License in [LICENSE](./LICENSE).
The self-contained Windows executable also bundles the open-source .NET
runtime. The bundled runtime, Virtual Display Driver, and Nefcon helper have
their own license and attribution notices in
[THIRD-PARTY-NOTICES.txt](./THIRD-PARTY-NOTICES.txt) and
[DOTNET-THIRD-PARTY-NOTICES.txt](./DOTNET-THIRD-PARTY-NOTICES.txt).

## Credit

Created by **Full_Quantum**.
