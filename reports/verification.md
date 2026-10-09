# Verification results

- Hosting tool build: passed, no warnings/errors in new projects.
- KSP addon build: passed using the installed KSP 1.12 managed assemblies, no warnings/errors.
- Luna server build and Windows x64 self-contained packaging: passed. Stable upstream emits existing obsolete API, .NET 6 end-of-support, package compatibility and dependency advisory warnings.
- Actual installation audit: 164 CKAN modules, 8,809 DLL/CFG/XML/version files, captured in `installed-pack.txt` / `installed-pack.xml`.
- 31 automated checks: passed. Covers pack scanning/round-trip, unknown mods/root-level DLLs, missing/changed/extra files, Kerbalism/unknown-mod assessment, private/CGNAT address identification, strict mod control, mandatory loaded parts, modded resources, XML escaping, rendezvous settings and aligned-clock settings. Generic layer checks cover nested node/JSON/duplicate-key transport, malformed/oversized snapshots, participation, ownership validation, forged-owner replacement, repeated module indices, replay rejection, global writer handover/epochs and culture-independent fields.
- Single-file self-contained server lifecycle smoke test: passed. Used an isolated temporary world on UDP 18809 with router mapping/rendezvous disabled. Registered the generic compatibility relay, reached “All systems up and running”, received `/shutdown`, logged completed backups, and exited with status 0.

Not yet verified: loading the addon in a running KSP instance; actual UPnP/NAT-PMP router behavior; public rendezvous/hole punching from two internet connections; multiplayer gameplay using this pack; Kerbalism-specific simulation adapters. The implementation must not be described as certified compatible with all installed mods.
