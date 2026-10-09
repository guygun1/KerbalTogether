param([string]$Version = "0.1.0-alpha.1", [string]$GameVersion = "1.12.5")
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$archive = Join-Path $root "artifacts\KerbalTogether-$Version-win-x64.zip"
if (!(Test-Path -LiteralPath $archive)) { throw "Build and package the release before uploading." }
$description = Get-Content -LiteralPath (Join-Path $root "packaging\SpaceDock-description.md") -Raw
Add-Type -AssemblyName System.Net.Http
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$handler = [System.Net.Http.HttpClientHandler]::new()
$handler.CookieContainer = [System.Net.CookieContainer]::new()
$http = [System.Net.Http.HttpClient]::new($handler)
$http.Timeout = [TimeSpan]::FromMinutes(15)
$http.DefaultRequestHeaders.UserAgent.ParseAdd("KerbalTogetherUploader/0.1 (https://github.com/guygun1/KerbalTogether/issues)")
function Submit-Form([string]$Url, [System.Net.Http.HttpContent]$Form) {
    $response = $http.PostAsync($Url, $Form).GetAwaiter().GetResult()
    try {
        $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        $result = $text | ConvertFrom-Json
        if (!$response.IsSuccessStatusCode -or $result.error) { throw "SpaceDock request failed: $($result.reason)" }
        return $result
    } finally { $response.Dispose() }
}
try {
    # Credentials and login cookies remain in this process; none are logged or saved to disk.
    $credential = Get-Credential -Message "Sign in to your SpaceDock account (not GitHub)"
    if (!$credential) { throw "SpaceDock login cancelled" }
    $login = [System.Net.Http.MultipartFormDataContent]::new()
    try {
        $login.Add([System.Net.Http.StringContent]::new($credential.UserName), "username")
        $login.Add([System.Net.Http.StringContent]::new($credential.GetNetworkCredential().Password), "password")
        Submit-Form "https://spacedock.info/api/login" $login | Out-Null
    } finally { $login.Dispose(); $credential = $null }
    $games = $http.GetStringAsync("https://spacedock.info/api/games").GetAwaiter().GetResult() | ConvertFrom-Json
    $game = @($games | Where-Object { $_.short -eq "kerbal-space-program" })[0]
    if (!$game) { throw "SpaceDock did not return Kerbal Space Program" }
    $notifications = $http.GetStringAsync("https://spacedock.info/api/$($game.id)/notifications").GetAwaiter().GetResult() | ConvertFrom-Json
    $ckan = @($notifications | Where-Object { $_.name -eq "CKAN" })[0]
    if (!$ckan) { throw "CKAN notification integration was not found" }
    $form = [System.Net.Http.MultipartFormDataContent]::new()
    try {
        $values = @{ "name" = "Kerbal Together"; "short-description" = "Experimental local hosting and generic mod compatibility layer for Luna Multiplayer. Windows hosting."; "description" = $description; "version" = $Version; "game-id" = [string]$game.id; "game-version" = $GameVersion; "license" = "MIT"; "notifications" = [string]$ckan.id }
        foreach ($entry in $values.GetEnumerator()) { $form.Add([System.Net.Http.StringContent]::new([string]$entry.Value), [string]$entry.Key) }
        $file = [System.Net.Http.StreamContent]::new([System.IO.File]::OpenRead($archive))
        $file.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::new("application/zip")
        $form.Add($file, "zipball", [System.IO.Path]::GetFileName($archive))
        $created = Submit-Form "https://spacedock.info/api/mod/create" $form
    } finally { $form.Dispose() }
    $url = "https://spacedock.info$($created.url)"
    $metadata = Get-Content -LiteralPath (Join-Path $root "packaging\KerbalTogether.netkan.template") -Raw
    $metadata.Replace("SPACE_DOCK_ID", [string]$created.id) | Set-Content -LiteralPath (Join-Path $root "artifacts\KerbalTogether.netkan") -Encoding UTF8
    @{ id = $created.id; url = $url; version = $Version; ckan_notification_selected = $true } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root "artifacts\spacedock-result.json") -Encoding UTF8
    Write-Host "Uploaded to SpaceDock: $url"
    Write-Host "SpaceDock API creates an unpublished listing. Open it in your browser, log in, and click Publish. CKAN notifications are selected."
    Start-Process $url
} finally { $http.Dispose(); $handler.Dispose() }
