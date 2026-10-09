# Kerbal Together — experimental test build

Local hosting and a generic compatibility layer for **Luna Multiplayer 0.29.2 / KSP 1.12**.

## Features
- In-game main-menu hosting controls, with a locally managed Luna server.
- UPnP, NAT-PMP and existing Luna rendezvous-assisted UDP hole-punching fallback.
- LAN/IPv6 connection candidates and actionable pack/config mismatch diagnostics.
- Generic discovery/synchronization of persistent scalar/string/enum fields in modded part modules, with vessel ownership validation.
- Optional experimental single-writer modded scenario snapshots, with nested saved-node transport and leader handover.

## Installation
Install **Luna Multiplayer 0.29.2** and **Harmony2** through CKAN. Extract this ZIP into the KSP root folder. It installs `GameData/KerbalTogether` and two executable files in the KSP root. Start KSP, select **Kerbal Together → Prepare mod pack**, then open **Host / pack diagnostics**.

**The hosting executables are Windows x64 only.** Their runtimes are bundled; no separate .NET install is needed. Friends also install the addon and matching gameplay mods.

## Current testing status
This is an early experimental release. Automated build/protocol checks and the local server lifecycle are tested. In-game two-client synchronization and real router/internet traversal remain unverified. Generic synchronization does not mean every installed mod is compatible: standalone controllers, separate static databases, external files, large state graphs and custom operations may need adapters. Kerbalism/colony/background-simulation support is not certified. Experimental scenario authority is disabled by default.

Hosted worlds are generated under `KerbalTogetherData/Universe`; preserve this folder when upgrading. The archive contains no personal worlds or proprietary KSP/Unity assemblies.

[Source and documentation](https://github.com/guygun1/KerbalTogether) · [Report an issue](https://github.com/guygun1/KerbalTogether/issues)

License: MIT; Luna and dependency notices are included.
