# Third-party components

Kerbal Together additions are MIT licensed (see `LICENSE`). The bundled server is based on [Luna Multiplayer](https://github.com/LunaMultiplayer/LunaMultiplayer), tag `0.29.2`, licensed under MIT. Its complete license is included as `GameData/KerbalTogether/Luna-LICENSE.txt`. The modified integration is distributed as source and an upstream patch.

The self-contained executables bundle Microsoft .NET runtime components and libraries. .NET is MIT licensed; see [runtime license](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) and [third-party notices](https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT). Runtime notices are also retained within the single-file bundles when supplied by publishing.

The hosting launcher uses [Open.NAT](https://github.com/lontivero/Open.NAT), MIT licensed, through `open.nat.core` 2.1.0.5. The server retains Luna's dependency set and licensing; consult its pinned project files and upstream license for complete dependency information. Harmony is not bundled and is installed separately through CKAN.

KSP and Unity game assemblies are used only as local compilation references and are not distributed in the repository or release archives. The normal Luna client is not bundled; install it through CKAN.
