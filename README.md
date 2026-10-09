# Kerbal Together

An experimental Essential-style convenience and generic compatibility layer for **KSP 1.12 + Luna Multiplayer 0.29.2**, with an in-game menu, automatically managed Windows local hosting, UPnP/NAT-PMP, and mod-pack diagnostics.

**Early test release:** builds, protocol checks and local server lifecycle are tested. In-game synchronization and real internet traversal still need two-client validation. This is a separate addon alongside the normal CKAN Luna client, plus a modified server managed by the hosting launcher.

## Why Luna?

Both [Luna](https://github.com/KSP-CKAN/NetKAN/blob/master/NetKAN/LunaMultiplayer.netkan) and [DarkMultiPlayer](https://github.com/KSP-CKAN/NetKAN/blob/master/NetKAN/DarkMultiPlayer.netkan) are on CKAN. Luna already implements reliable UDP, IPv6, UPnP, rendezvous-assisted NAT punching, persistent part-field synchronization and mod-control checks. These are the closest starting point. DMP is a viable alternative but adapting its TCP-oriented transport for UDP punching would be more work.

The upstream checkout is pinned to stable tag `0.29.2`, commit `60a2ed5dbfdddc08536a98b999c781e3c12b0649`. [CKAN metadata](https://github.com/KSP-CKAN/CKAN-meta/blob/master/LunaMultiplayer/LunaMultiplayer-0.29.2.ckan) targets KSP 1.12 and requires Harmony2. Draft releases are not the target.

## Build and install

Requires .NET 8 or newer SDK and your own KSP 1.12 installation for compilation references. From this project's root:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Build.ps1 -KspRoot "C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program"
```

1. Install **Luna Multiplayer 0.29.2** and **Harmony2** via CKAN on every player's KSP instance.
2. Extract the release ZIP into each player's KSP root folder, keeping its `GameData/KerbalTogether` folder and `KerbalTogether.Host.exe` / `KerbalTogether.Server.exe` root files. The self-contained executables include their runtimes; friends don't need a .NET SDK. Keep them outside GameData.
3. Start KSP. At the main menu open **Kerbal Together → Prepare mod pack**. This uses KSP's actual loaded part/resource list, including ModuleManager patches, and generates `KSP/LMPModControl.xml`.
4. Open **Host / pack diagnostics**, set a session name/password, then **Host game**. Read the pack assessment before proceeding.
5. Enter the password in the KSP menu and click **Join my hosted game**. Your friend compares your `kerbal-together-pack.xml` using **Compare friend's pack**, resolves differences, and joins your address or the Luna server-browser entry.
6. **Save & stop** or close the hosting window when finished. The game world persists under `KerbalTogetherData/Universe`. Keep this directory when upgrading; it is generated at runtime and is not part of the release archive.

This is an addon around Luna, not a new multiplayer transport. The launcher manages a local server process because Luna is client/server. You don't run a separate public server or configure a cloud backend. Closing KSP does not automatically close the hosting window: close it to end the session. Current packaged launcher is Windows x64.

## Connection fallback

1. Attempt UPnP UDP mapping for the game port (default 8800).
2. If unavailable, attempt NAT-PMP. Leases are renewed every minute and deleted on orderly stop; otherwise they expire after five minutes.
3. If mapping failed or the router reports a private/CGNAT address, enable **Luna's existing public rendezvous**. Friends use Luna's server browser, which invokes its hole-punching flow. This advertises the session to public infrastructure and requires those services to be available. The password is required by this launcher; game traffic is direct to your PC.
4. Also display LAN and globally scoped IPv6 candidates. Direct IPv6 avoids IPv4 NAT but still needs IPv6 connectivity on both players and an inbound firewall allowance.
5. If punching fails behind symmetric NAT/CGNAT, use manual forwarding, ISP-provided public IPv4, direct IPv6 or an existing peer-to-peer VPN. There is no built-in relay.

Successful mapping means the router accepted a rule, **not** that an internet player can reach you. Check with an actual friend. A TCP port-check website cannot verify Luna's UDP port. The tool reports mapping failures, renewal failures, startup errors and private WAN addresses. Allow `KerbalTogether.Server.exe` through Windows Firewall. Use a distinct server name to find your fallback session.

Uncheck public rendezvous to disable server registration/punching. Luna's existing version checks and client server-browser functionality may still contact external services; this is not a fully offline fork.

## Mod packs and synchronization

**Pack identity and gameplay compatibility are separate.** Every DLL, CFG, version file and XML in GameData is hashed, including stock/DLC data and unmanaged mods. CKAN's installed registry adds resolved identifiers and versions; a CKAN export alone doesn't necessarily pin versions or include manually installed mods. Textures/models, PluginData and generated ModuleManager caches are not compared. Some visual/UI config differences may be harmless but are still reported for review.

The generated Luna mod-control file is made strict: required plugin hashes, no unlisted plugins, loaded parts/resources and expansions. File/config differences have explicit missing/different/extra messages. Luna still handles join-time plugin/part checks; CFG and root-level DLL matching (including ModuleManager) are manual preflight via shared manifests, not a new network-enforced handshake.

The launcher uses `WarpMode=None` to avoid separate subspace clocks with background simulations. This disables multiplayer time warp for now. It doesn't make arbitrary background simulation authoritative.

### Generic compatibility layer

The addon automatically discovers modded `PartModule` types and `[KSPField(isPersistant=true)]` fields. Supported scalar/string/enum state is sent when changed, with periodic complete refreshes. The server checks Luna's vessel update ownership before forwarding snapshots. Repeated module instances are addressed by vessel GUID, part flight ID and module index, with assembly/type identity and sequence checks.

Tick callbacks on tracked modded part modules are guarded so non-owning clients do not independently run those callbacks while the layer is negotiated and multiplayer is running. Stock modules remain under Luna's existing synchronization. This covers those entry points, not every way a mod may change state.

The host can optionally enable **experimental generic scenario single-writer sync**. Modded scenario save-node trees are transported without text parsing (preserving nested data, duplicate keys and literal JSON braces). The first negotiated participant is elected writer; departure promotes another participant with a new authority epoch. The server caches/persists the latest snapshots and forwards them on join/handover. Registered scenarios are excluded from Luna's legacy all-writers save stream. Scenario tick callbacks are guarded on followers.

This mode is off by default: calling a mod's `Load` during play may rebuild its state, and interactions outside its guarded tick methods are not automatically forwarded to the writer. Scenario snapshots have a 256 KiB size limit and bounded tree depth/count. Large databases and standalone background controllers remain adapter cases. The runtime coverage catalog is written to `GameData/KerbalTogether/PluginData/runtime-compatibility.txt`.

**Kerbalism is not certified supported.** Its separate global/per-vessel databases and background controllers illustrate the remaining cases. The main approach is generic; small adapters can connect uncovered state/controllers to it rather than rewriting synchronization separately for every mod. Unknown mods are unverified, not automatically rejected or labeled compatible.

## Verification

```powershell
dotnet run --project tests/KerbalTogether.Checks -c Release
```

After building the distribution, also run the server lifecycle check:

```powershell
dotnet run --project tests/KerbalTogether.Checks -c Release -- --server "dist"
```

Checks cover pack mismatches, generic persistence encoding, ownership/sequence/authority handover, culture-independent values, generated configuration and the packaged server lifecycle. See `reports/verification.md` for results. The addon is compiled against real locally installed KSP assemblies. Internet traversal and in-game two-player synchronization have not been verified.

To audit another installed instance without opening the GUI:

```powershell
src\KerbalTogether.Host\bin\Release\net8.0-windows\KerbalTogether.Host.exe --audit "C:\path\to\KSP" "reports\installed-pack"
```

Router traversal and gameplay require real network/two-client testing; compiling and pack-identity tests don't prove those work. Source builds use installed KSP assemblies, which are not redistributed.

## Source layout and licensing

- `src/KerbalTogether.Plugin`: main-menu addon calling Luna's public API via reflection.
- `src/KerbalTogether.Host`: managed server lifecycle, router fallback, CKAN/pack assessment.
- `src/KerbalTogether.Core`: shared pack scanner/comparison.
- `src/KerbalTogether.Server`: generic ownership-validated compatibility relay.
- `patches/Luna-0.29.2.patch`: changes integrating the relay, managed data location and shutdown into the pinned Luna source.
- `upstream`: local MIT-licensed Luna checkout obtained by `scripts/SetupUpstream.ps1`; not duplicated in this repository.

## Distribution

Source: https://github.com/guygun1/KerbalTogether

Release downloads are intended for SpaceDock. CKAN submission is pending; this repository alone does not make the addon available in CKAN. `packaging/KerbalTogether.netkan.template` documents dependencies and installation rules. The hosting executables are Windows x64; CKAN currently does not express this platform restriction, so the listing must disclose it.

After building, create the upload ZIP with:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Package.ps1
```

The maintainer can create an unpublished SpaceDock listing and select CKAN notifications using `scripts/UploadSpaceDock.ps1`. It prompts locally for SpaceDock credentials, stores no credentials/cookies, uploads the ZIP, and opens the listing for SpaceDock's required browser publication step. The generated NetKAN file and upload result are written to ignored `artifacts/`.

Kerbal Together additions are MIT licensed; upstream notices are retained. Open.NAT and bundled runtime/dependency notices remain in their respective distributions.
