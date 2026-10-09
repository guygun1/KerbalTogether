# Luna server patch

`Luna-0.29.2.patch` applies to upstream commit `60a2ed5dbfdddc08536a98b999c781e3c12b0649` (tag `0.29.2`). It adds the compatibility relay integration and graceful managed shutdown. The new relay implementation is in `src/KerbalTogether.Server`; the patch links that file into the upstream server project.

`scripts/SetupUpstream.ps1` obtains the MIT-licensed upstream source and applies the patch. No KSP/Unity game DLLs are included in this repository or the release archives.
