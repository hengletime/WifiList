<#
    WifiList.ps1  -  Windows WiFi credential inventory -> self-contained HTML

    Why this design:
      * netsh "show profiles" text output is locale dependent and mangles
        non-ASCII SSIDs when piped. So we never parse localized text.
        Instead we use: netsh wlan export profile key=clear -> per-profile XML
        (standard WLANProfile schema, UTF-8/UTF-16, language independent).
      * This file is 100% ASCII on purpose. Windows PowerShell 5.1 decodes
        BOM-less .ps1 files with the ANSI code page, which corrupts any CJK
        literal. All Chinese UI copy lives in assets\template.html instead.
      * No admin rights required. Works on Windows 10 / 11.

    Usage:
      powershell -ExecutionPolicy Bypass -File WifiList.ps1
      ... -NoPassword      export without plain text keys
      ... -OutPath <file>  custom output html path
      ... -NoOpen          do not launch the browser
      ... -Quiet           suppress console output
#>

[CmdletBinding()]
param(
    [switch]$NoPassword,
    [switch]$NoOpen,
    [switch]$Quiet,
    [string]$OutPath
)

$ErrorActionPreference = 'Stop'
$script:Root = Split-Path -Parent $MyInvocation.MyCommand.Definition

function Write-Note {
    param([string]$Text, [string]$Color = 'Gray')
    if (-not $Quiet) { Write-Host $Text -ForegroundColor $Color }
}

function Get-X { param($Doc, [string]$Name)
    $n = $Doc.SelectSingleNode("//*[local-name()='$Name']")
    if ($null -ne $n) { return $n.InnerText }
    return ''
}

try {
    # ---------- 0. preflight ----------
    $tplPath = Join-Path $script:Root 'assets\template.html'
    if (-not (Test-Path -LiteralPath $tplPath)) {
        throw "Template not found: $tplPath  (keep the assets folder next to this script)"
    }
    $template = [System.IO.File]::ReadAllText($tplPath, [System.Text.Encoding]::UTF8)

    $wlanAdapter = @(Get-NetAdapter -ErrorAction SilentlyContinue | Where-Object {
        $_.InterfaceDescription -like '*Wi-Fi*' -or $_.InterfaceDescription -like '*Wireless*' -or
        $_.Name -eq 'WLAN'
    } | Where-Object {
        $_.InterfaceDescription -notlike '*Virtual*' -and $_.InterfaceDescription -notlike '*Direct*'
    })

    Write-Note ""
    Write-Note "  WiFi Inventory  -  reading saved wireless profiles ..." 'Cyan'

    # ---------- 1. export every profile with plain text key ----------
    $tmp = Join-Path $env:TEMP ('wifi_export_' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $tmp -Force | Out-Null

    $exportLog = ''
    try {
        $exportLog = (netsh wlan export profile folder=$tmp key=clear 2>&1 | Out-String)
    } catch {
        throw "netsh export failed: $($_.Exception.Message)"
    }

    $files = @(Get-ChildItem -LiteralPath $tmp -Filter *.xml -ErrorAction SilentlyContinue)
    if ($files.Count -eq 0) {
        throw "No WLAN profile exported. Is the WLAN service running and at least one network saved?"
    }

    # ---------- 2. parse each profile xml ----------
    $nets = New-Object System.Collections.ArrayList
    $parseFail = 0
    foreach ($f in $files) {
        try {
            $doc = New-Object System.Xml.XmlDocument
            $doc.Load($f.FullName)

            $ssid = Get-X $doc 'name'
            if ([string]::IsNullOrWhiteSpace($ssid)) { $ssid = [System.IO.Path]::GetFileNameWithoutExtension($f.Name) }

            $auth  = Get-X $doc 'authentication'
            $enc   = Get-X $doc 'encryption'
            $onex  = Get-X $doc 'useOneX'
            $ktype = Get-X $doc 'keyType'
            $key   = Get-X $doc 'keyMaterial'
            $prot  = Get-X $doc 'protected'

            # hasKey is a property of the PROFILE (does it carry a shared key at
            # all), which must stay independent from whether we export the
            # plaintext. Otherwise -NoPassword would make every network look open.
            $hasKey = ($ktype -ne '')

            # Some profiles expose the key only as an encrypted blob; treat as unknown.
            if ($prot -eq 'true') { $key = '' }
            if ($NoPassword) { $key = '' }

            $null = $nets.Add([pscustomobject]@{
                ssid   = $ssid
                auth   = $auth
                enc    = $enc
                key    = $key
                hasKey = $hasKey
                mode   = (Get-X $doc 'connectionMode')
                onex   = ($onex -eq 'true')
                hidden = ((Get-X $doc 'nonBroadcast') -eq 'true')
                rand   = ((Get-X $doc 'enableRandomization') -eq 'true')
                cur    = $false
            })
        } catch {
            $parseFail++
        }
    }

    # how many profiles exist for THIS wireless interface (language independent
    # ground truth). Profiles are stored per-interface GUID, and stale folders
    # from removed adapters must not be counted.
    $expected = 0
    try {
        $pd = Join-Path $env:ProgramData 'Microsoft\Wlansvc\Profiles\Interfaces'
        if ($wlanAdapter.Count -gt 0 -and $wlanAdapter[0].InterfaceGuid) {
            $pd = Join-Path $pd ([string]$wlanAdapter[0].InterfaceGuid)
        }
        if (Test-Path -LiteralPath $pd) {
            $expected = @(Get-ChildItem -LiteralPath $pd -Filter *.xml -ErrorAction SilentlyContinue).Count
        }
    } catch { }

    # ---------- 3. current connection ----------
    $cur = $null
    $cp = @(Get-NetConnectionProfile -ErrorAction SilentlyContinue | Where-Object { $_.InterfaceAlias -eq 'WLAN' })
    if ($cp.Count -eq 0) { $cp = @(Get-NetConnectionProfile -ErrorAction SilentlyContinue) }

    $ifaceRaw = ''
    if ($cp.Count -gt 0 -and $cp[0].Name) {
        $oldEnc = [Console]::OutputEncoding
        try {
            [Console]::OutputEncoding = [System.Text.Encoding]::GetEncoding(0)
            $ifaceRaw = (netsh wlan show interfaces 2>&1 | Out-String)
        } catch { $ifaceRaw = '' } finally { [Console]::OutputEncoding = $oldEnc }

        $bssid = ''
        $mB = [regex]::Match($ifaceRaw, 'AP\s*BSSID\s*:\s*([0-9a-fA-F]{2}(?::[0-9a-fA-F]{2}){5})')
        if ($mB.Success) { $bssid = $mB.Groups[1].Value.ToLower() }

        $rssi = $null
        $mR = [regex]::Match($ifaceRaw, 'Rssi\s*:\s*(-?\d+)')
        if ($mR.Success) { $rssi = [int]$mR.Groups[1].Value }

        $signal = $null
        $mS = [regex]::Match($ifaceRaw, '(\d{1,3})\s*%')
        if ($mS.Success) { $signal = [int]$mS.Groups[1].Value }
        elseif ($null -ne $rssi) { $signal = [math]::Max(0, [math]::Min(100, 2 * ($rssi + 100))) }

        $ipInfo = $null
        try { $ipInfo = Get-NetIPConfiguration -InterfaceAlias $cp[0].InterfaceAlias -ErrorAction SilentlyContinue } catch { }

        $ip = ''; $gw = ''; $dns = @()
        if ($null -ne $ipInfo) {
            $v4 = @($ipInfo.IPv4Address)
            if ($v4.Count -gt 0) { $ip = $v4[0].IPAddress }
            if ($null -ne $ipInfo.IPv4DefaultGateway) { $gw = $ipInfo.IPv4DefaultGateway.NextHop }
            $dnsSrv = @($ipInfo.DNSServer | Where-Object { $_.AddressFamily -eq 2 })
            foreach ($d in $dnsSrv) { foreach ($a in @($d.ServerAddresses)) { if ($a) { $dns += $a } } }
        }

        $speed = ''
        if ($wlanAdapter.Count -gt 0) { $speed = [string]$wlanAdapter[0].LinkSpeed }

        # auth code of the active network is taken from its saved profile, so we
        # never have to parse the localized "authentication" line of netsh.
        $matched = $nets | Where-Object { $_.ssid -eq $cp[0].Name } | Select-Object -First 1
        $authCode = ''
        if ($null -ne $matched) {
            $authCode = $matched.auth
            $matched.cur = $true
        }

        $cur = [pscustomobject]@{
            connected = $true
            ssid      = $cp[0].Name
            bssid     = $bssid
            signal    = $signal
            rssi      = $rssi
            speed     = $speed
            ip        = $ip
            gateway   = $gw
            dns       = @($dns)
            category  = [string]$cp[0].NetworkCategory
            authCode  = $authCode
        }
    } else {
        $cur = [pscustomobject]@{
            connected = $false; ssid = ''; bssid = ''; signal = $null; rssi = $null
            speed = ''; ip = ''; gateway = ''; dns = @(); category = ''; authCode = ''
        }
    }

    # ---------- 4. meta ----------
    $osName = ''
    try { $osName = (Get-CimInstance Win32_OperatingSystem).Caption } catch { $osName = [string][Environment]::OSVersion.VersionString }

    $meta = [pscustomobject]@{
        host    = $env:COMPUTERNAME
        os      = $osName
        time    = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
        adapter = $(if ($wlanAdapter.Count -gt 0) { [string]$wlanAdapter[0].InterfaceDescription } else { '' })
        total   = $nets.Count
        masked  = [bool]$NoPassword
    }

    # ---------- 5. json -> html ----------
    $payload = [pscustomobject]@{
        meta    = $meta
        current = $cur
        nets    = @($nets)
    }
    $json = $payload | ConvertTo-Json -Depth 6 -Compress
    # keep the payload from ever closing the script block
    $json = $json.Replace('<', '\u003c').Replace('>', '\u003e').Replace('&', '\u0026')

    if ($json -notmatch '^\s*\{') { throw "JSON serialization produced unexpected output." }

    $html = $template.Replace('/*__WIFI_DATA__*/', $json)

    # ---------- 6. write ----------
    if ([string]::IsNullOrWhiteSpace($OutPath)) {
        $outDir = Join-Path $script:Root 'output'
    } elseif (Test-Path -LiteralPath $OutPath -PathType Container) {
        $outDir = $OutPath
    } else {
        $outDir = Split-Path -Parent $OutPath
        if ([string]::IsNullOrWhiteSpace($outDir)) { $outDir = $script:Root }
    }
    if (-not (Test-Path -LiteralPath $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }

    if ($OutPath -and -not (Test-Path -LiteralPath $OutPath -PathType Container) -and $OutPath -match '\.html?$') {
        $outFile = $OutPath
    } else {
        $outFile = Join-Path $outDir ('wifi-list-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.html')
    }

    $utf8 = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($outFile, $html, $utf8)

    # ---------- 7. cleanup ----------
    Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue

    # ---------- 8. report ----------
    $sizeKb = [math]::Round((Get-Item -LiteralPath $outFile).Length / 1KB, 1)
    Write-Note ("  profiles parsed : {0}" -f $nets.Count) 'Gray'
    if ($expected -gt $nets.Count) {
        Write-Note ("  note: {0} profile(x) on disk could not be exported (usually enterprise/802.1X or corrupted)." -f ($expected - $nets.Count)) 'DarkYellow'
    }
    if ($parseFail -gt 0) { Write-Note ("  note: {0} xml file(s) skipped." -f $parseFail) 'DarkYellow' }
    Write-Note ("  output          : {0}  ({1} KB)" -f $outFile, $sizeKb) 'Green'
    Write-Note ""

    # opening the browser is a convenience, never a failure condition
    if (-not $NoOpen) {
        try { Start-Process -FilePath $outFile | Out-Null } catch {
            Write-Note "  (could not launch the browser automatically - open the file manually)" 'DarkYellow'
        }
    }

    exit 0
}
catch {
    Write-Host ""
    Write-Host ("  [ERROR] " + $_.Exception.Message) -ForegroundColor Red
    Write-Host ""
    exit 1
}
