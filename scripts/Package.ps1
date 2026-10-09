param([string]$Version = "0.1.0-alpha.1")
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $root "dist"
$artifacts = Join-Path $root "artifacts"
if (!(Test-Path -LiteralPath $root)) { throw "Workspace does not exist" }
foreach ($file in @("GameData\KerbalTogether\KerbalTogether.dll", "GameData\KerbalTogether\KerbalTogether.Core.dll", "KerbalTogether.Host.exe", "KerbalTogether.Server.exe")) {
    if (!(Test-Path -LiteralPath (Join-Path $dist $file))) { throw "Missing built release file: $file" }
}
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
$archive = Join-Path $artifacts "KerbalTogether-$Version-win-x64.zip"
if (Test-Path -LiteralPath $archive) { throw "Archive already exists: $archive (use a new version)" }
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::Open($archive, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    # Explicit allowlist: never include personal worlds, passwords, CKAN registries, or KSP/Unity assemblies.
    foreach ($file in @("GameData\KerbalTogether\KerbalTogether.dll", "GameData\KerbalTogether\KerbalTogether.Core.dll", "GameData\KerbalTogether\Luna-LICENSE.txt", "GameData\KerbalTogether\README.md", "KerbalTogether.Host.exe", "KerbalTogether.Server.exe")) {
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $dist $file), $file.Replace('\','/'), [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
    foreach ($file in @("LICENSE", "README.md", "THIRD-PARTY-NOTICES.md")) {
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $root $file), $file, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $zip.Dispose() }
Get-FileHash -LiteralPath $archive -Algorithm SHA256
