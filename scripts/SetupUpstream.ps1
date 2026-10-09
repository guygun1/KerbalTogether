$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$upstream = Join-Path $root "upstream"
$patch = Join-Path $root "patches\Luna-0.29.2.patch"
if (!(Test-Path -LiteralPath $root)) { throw "Workspace does not exist" }
if (!(Test-Path -LiteralPath "$upstream\.git")) {
    git clone --depth 1 --branch 0.29.2 https://github.com/LunaMultiplayer/LunaMultiplayer.git $upstream
    if ($LASTEXITCODE) { throw "Could not clone Luna Multiplayer" }
}
$commit = git -C $upstream rev-parse HEAD
if ($commit -ne "60a2ed5dbfdddc08536a98b999c781e3c12b0649") { throw "Unexpected upstream commit; expected Luna 0.29.2" }
git -C $upstream apply --reverse --check $patch 2>$null
if ($LASTEXITCODE -eq 0) { return }
git -C $upstream apply --check $patch
if ($LASTEXITCODE) { throw "Upstream changes conflict with the release patch" }
git -C $upstream apply $patch
if ($LASTEXITCODE) { throw "Could not apply Luna integration patch" }
