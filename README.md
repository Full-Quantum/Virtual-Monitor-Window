# Virtual Monitor Window

Virtual Monitor Window is a portable Windows app for viewing a display in a
resizable window.

![Virtual Monitor Window 1.0.7 screenshot](./docs/images/virtual-monitor-window-v1.0.7.png)

## Why this exists

If people or objects block the view of a large screen, its content can be hard to see.
Virtual Monitor Window shows the extended display in a small resizable window, like a
little duplicate monitor.

![Illustration of the problem Virtual Monitor Window is designed to solve](./docs/images/problem-this-was-designed-to-solve.png)

## Features

- Virtual Monitor view of detected Windows displays, with the visible cursor.
- Optional mouse and keyboard control for VDD virtual displays.
- Create and manage up to 16 VDD virtual displays.
- Light and dark themes.
- Self-contained x64 Windows executable; no separate .NET installation is
  required to run the published app.

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

I'm probably getting some of this wrong. I'm trying to credit everyone in the
most acceptable way, so feedback is appreciated. If you spot a missing or
incorrect attribution or license notice, please open an issue.

The Virtual Monitor Window source code, documentation, and included
illustration are available under the MIT License in [LICENSE](./LICENSE).
The self-contained Windows executable also bundles the open-source .NET
runtime. The bundled runtime, Virtual Display Driver, and Nefcon helper have
their own license and attribution notices in
[THIRD-PARTY-NOTICES.txt](./THIRD-PARTY-NOTICES.txt) and
[DOTNET-THIRD-PARTY-NOTICES.txt](./DOTNET-THIRD-PARTY-NOTICES.txt).

## Credit

Created by **Full_Quantum**.
