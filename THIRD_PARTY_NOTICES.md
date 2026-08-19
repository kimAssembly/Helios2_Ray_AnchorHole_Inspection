# Third-Party Software and License Notice

Audit date: **2026-08-19**

This document records the software dependencies used by Anchor Hole Workcell. It is an engineering inventory, not legal advice. Always review the license files shipped with the exact SDK/runtime version used for a release.

## Dependency summary

| Component | How it is used | License/status | Included in this repository? | Included in the current build output? |
|---|---|---|---|---|
| Microsoft .NET 10 runtime and base class libraries | Application runtime, collections, numerics, threading and other `System.*` APIs | .NET source and library packages are primarily MIT; Windows product distributions can also contain components governed by the .NET Library License, Windows SDK terms and Microsoft Visual C++ runtime terms | No | No — the default build is framework-dependent |
| Windows Presentation Foundation (WPF) | Windows desktop UI, XAML, controls and drawing | MIT for the WPF source repository; Windows runtime distributions may contain additional Microsoft-licensed binary components | No | No — supplied by the installed Windows Desktop Runtime |
| LUCID Arena SDK / `ArenaNET_MP.dll` | Helios2 Ray discovery, configuration, Force-IP and `Coord3D_ABCY16` streaming | Proprietary LUCID Vision Labs software; use and redistribution are governed by the Arena SDK installer/download agreement applicable to the installed version | No | **Yes** — `ArenaNET_MP.dll` is copied locally because the project reference has `Private=true` |
| Helios2 Ray firmware | Runs on the camera; not linked into the application | Proprietary LUCID Vision Labs firmware/product terms | No | No |

## NuGet audit

The solution currently has **no direct or transitive NuGet package references**.

Audited with:

```powershell
dotnet list .\AnchorHoleWorkcell.slnx package --include-transitive
```

Both projects reported no packages for `net10.0-windows`.

The following namespaces are part of the .NET/WPF framework and are not separately installed NuGet packages in this project:

- `System.Numerics`
- `System.Collections.Concurrent`
- `System.Windows`
- `System.Windows.Controls`
- `System.Windows.Media`
- `System.Windows.Media.Imaging`
- `System.Windows.Shapes`

## Microsoft .NET and WPF

- [.NET runtime MIT license](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT)
- [.NET licensing overview](https://github.com/dotnet/core/blob/main/license-information.md)
- [.NET Windows distribution license information](https://github.com/dotnet/core/blob/main/license-information-windows.md)
- [WPF repository and MIT license](https://github.com/dotnet/wpf)
- [.NET runtime third-party notices](https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT)
- [WPF third-party notices](https://github.com/dotnet/wpf/blob/main/THIRD-PARTY-NOTICES.TXT)

The checked-in project does not vendor Microsoft runtime binaries. A normal build requires the compatible .NET Windows Desktop Runtime to be installed on the target PC. If the application is later published as **self-contained**, the release package will contain Microsoft runtime files; the matching license and third-party notice files from that exact runtime distribution must then accompany the release.

## LUCID Arena SDK

The source code references:

```text
C:\Program Files\LUCID Vision Labs\Arena SDK\x64Release\ArenaNET_MP.dll
```

Relevant official resources:

- [LUCID Arena SDK downloads](https://thinklucid.com/downloads-hub/)
- [LUCID support center](https://support.thinklucid.com/)
- [LUCID terms and conditions](https://thinklucid.com/terms-and-conditions/)

The installed SDK directory audited on 2026-08-19 did not expose a general Arena SDK EULA as a standalone text file. Therefore this repository does **not** claim that `ArenaNET_MP.dll` may be freely redistributed.

### Distribution rule

- The Git repository does not include `ArenaNET_MP.dll`; `bin/` and `obj/` are ignored.
- Developers must obtain and install Arena SDK from LUCID.
- The current local Release output copies `ArenaNET_MP.dll` next to the application executable.
- Before publishing a binary ZIP, installer, container or commercial product containing `ArenaNET_MP.dll`, review the license accepted during installation for the exact Arena SDK version or obtain redistribution confirmation from LUCID Vision Labs.
- Do not copy the entire `x64Release` SDK directory into a release. It contains additional vendor and third-party components not audited as application dependencies here.

## Components present in the Arena SDK but not used directly

The local Arena SDK installation contains optional viewer/media components and an ImGui license file. This application does not reference ImGui, FFmpeg, OpenCV or Save SDK assemblies, and those components are not copied into the current build output. They are therefore not listed as application dependencies. If future code adds any of them, update this notice before distribution.

## Project source license

At the time of this audit the repository does not contain a project-level `LICENSE` file. Under default copyright rules, public access to the source does not automatically grant permission to copy, modify or redistribute the project itself. The repository owner should choose and add a project license separately if those permissions are intended.

## Release checklist

Before every public binary release:

1. Re-run the NuGet audit command.
2. Inspect the publish directory for newly bundled DLLs.
3. Record the exact .NET and Arena SDK versions.
4. Include the matching Microsoft notices if publishing self-contained.
5. Confirm LUCID redistribution rights before bundling `ArenaNET_MP.dll` or other Arena binaries.
6. Update this file when a new dependency is introduced.
