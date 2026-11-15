param(
    [string]$Tag = "v1.6.1",
    [string]$Notes = "RELEASE_NOTES_v1.6.1.md",
    [string]$Repo = "hengletime/WifiList"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

Write-Host ""
Write-Host "  Publishing release $Tag to $Repo" -ForegroundColor Cyan
Write-Host ""

# ---- 1. read the token from the credential manager (never printed) ----
$cred = "protocol=https`nhost=github.com`n`n" | & git credential fill
$token = ($cred | Where-Object { $_ -like "password=*" }) -replace "^password=", ""
if (-not $token) {
    Write-Host "  [ERROR] no stored GitHub credential. Run a git push once, or set one with git credential." -ForegroundColor Red
    exit 1
}

$headers = @{
    Authorization = "token $token"
    "User-Agent"  = "WifiList-release"
    Accept        = "application/vnd.github+json"
}

# ---- 2. make sure the tag exists on the remote ----
Write-Host "  [1/3] pushing tag $Tag ..."
& git push origin $Tag 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host "  [warn] tag push returned $LASTEXITCODE (it may already exist)" -ForegroundColor Yellow }

# ---- 3. build the release body ----
$notesPath = Join-Path $root $Notes
if (-not (Test-Path $notesPath)) {
    Write-Host "  [ERROR] release notes not found: $notesPath" -ForegroundColor Red
    exit 1
}
$body = Get-Content $notesPath -Raw -Encoding UTF8

# append sha256 of the artefacts so downloaders can verify them
$dist = Join-Path $root "dist"
$assets = @()
if (Test-Path $dist) {
    $assets = Get-ChildItem $dist -File | Where-Object { $_.Extension -eq ".exe" -or $_.Extension -eq ".zip" }
}
if ($assets.Count -gt 0) {
    $lines = @("", "### SHA-256", "", '```')
    foreach ($a in $assets) {
        $hash = (Get-FileHash $a.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        $lines += ("{0}  {1}" -f $hash, $a.Name)
    }
    $lines += '```'
    $body = $body -replace '<!--SHA-->', (($lines -join "`n"))
}

$payload = @{
    tag_name   = $Tag
    name       = "WifiList $Tag"
    body       = $body
    draft      = $false
    prerelease = $false
} | ConvertTo-Json -Depth 4

$json = [System.Text.Encoding]::UTF8.GetBytes($payload)

# ---- 4. create (or reuse) the release ----
Write-Host "  [2/3] creating the release ..."
$release = $null
try {
    $release = Invoke-RestMethod -Method Post -Uri "https://api.github.com/repos/$Repo/releases" -Headers $headers -ContentType "application/json; charset=utf-8" -Body $json
} catch {
    $msg = $_.Exception.Message
    if ($msg -match "422") {
        Write-Host "  release already exists, looking it up ..." -ForegroundColor Yellow
        $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/tags/$Tag" -Headers $headers
    } else {
        throw
    }
}
Write-Host "  release id: $($release.id)"

# ---- 5. upload the artefacts ----
Write-Host "  [3/3] uploading artefacts ..."
foreach ($a in $assets) {
    $name = [uri]::EscapeDataString($a.Name)
    $uri = "https://uploads.github.com/repos/$Repo/releases/$($release.id)/assets?name=$name"
    try {
        Invoke-RestMethod -Method Post -Uri $uri -Headers $headers -ContentType "application/octet-stream" -InFile $a.FullName -TimeoutSec 300 | Out-Null
        Write-Host ("    uploaded {0}  ({1:N0} bytes)" -f $a.Name, $a.Length)
    } catch {
        Write-Host ("    [warn] {0}: {1}" -f $a.Name, $_.Exception.Message) -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "  [OK] $($release.html_url)" -ForegroundColor Green
Write-Host ""
