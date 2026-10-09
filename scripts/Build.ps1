param([string]$KspRoot = "C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program")
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
if (!(Test-Path -LiteralPath $root)) { throw "Workspace does not exist" }
if (!(Test-Path -LiteralPath "$KspRoot\KSP_x64_Data\Managed\Assembly-CSharp.dll")) { throw "Pass -KspRoot pointing to KSP 1.12" }
& "$PSScriptRoot\SetupUpstream.ps1"
$dist = "$root\dist\GameData\KerbalTogether"
New-Item -ItemType Directory -Path $dist -Force | Out-Null
dotnet publish "$root\src\KerbalTogether.Host\KerbalTogether.Host.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "$root\dist\HostBuild" -clp:ErrorsOnly
if ($LASTEXITCODE) { throw "Hosting tool build failed" }
dotnet publish "$root\upstream\Server\Server.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "$root\dist\ServerBuild" -clp:ErrorsOnly
if ($LASTEXITCODE) { throw "Luna server build failed" }
dotnet build "$root\src\KerbalTogether.Plugin\KerbalTogether.Plugin.csproj" -c Release "-p:KspRoot=$KspRoot"
if ($LASTEXITCODE) { throw "KSP addon build failed" }
Copy-Item -LiteralPath "$root\src\KerbalTogether.Plugin\bin\Release\net472\KerbalTogether.dll" -Destination $dist
Copy-Item -LiteralPath "$root\src\KerbalTogether.Plugin\bin\Release\net472\KerbalTogether.Core.dll" -Destination $dist
Copy-Item -LiteralPath "$root\dist\HostBuild\KerbalTogether.Host.exe" -Destination "$root\dist\KerbalTogether.Host.exe"
Copy-Item -LiteralPath "$root\dist\ServerBuild\Server.exe" -Destination "$root\dist\KerbalTogether.Server.exe"
Copy-Item -LiteralPath "$root\upstream\LICENSE" -Destination "$dist\Luna-LICENSE.txt"
Copy-Item -LiteralPath "$root\README.md" -Destination "$dist\README.md"
Write-Host "Ready: copy dist\GameData and the two KerbalTogether executables into KSP, alongside CKAN's Luna Multiplayer 0.29.2."
