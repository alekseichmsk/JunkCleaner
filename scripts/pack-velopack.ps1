param(
    [Parameter(Mandatory = $true)]
    [string] $Version,

    [Parameter(Mandatory = $false)]
    [string] $PackId = "JunkCleaner",

    [Parameter(Mandatory = $false)]
    [string] $Runtime = "win-x64",

    [Parameter(Mandatory = $false)]
    [switch] $SkipVpkInstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-VpkExe {
    if ($env:VPK_PATH -and (Test-Path $env:VPK_PATH)) {
        return (Resolve-Path $env:VPK_PATH).Path
    }

    $candidates = @(
        "$env:USERPROFILE\.dotnet\tools\vpk.exe"
    )

    foreach ($c in $candidates) {
        if (Test-Path $c) {
            return (Resolve-Path $c).Path
        }
    }

    return $null
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$solutionOrProject = Join-Path $repoRoot "JunkCleaner\JunkCleaner.csproj"
$publishDir = Join-Path $repoRoot "artifacts\velopack-publish"
$releasesDir = Join-Path $repoRoot "artifacts\velopack-releases"
$releaseNotes = Join-Path $repoRoot "artifacts\velopack-release-notes.md"

Write-Host "Repo:       $repoRoot"
Write-Host "Version:    $Version"
Write-Host "PackId:     $PackId"
Write-Host "Runtime:    $Runtime"
Write-Host "PublishDir: $publishDir"
Write-Host "Releases:   $releasesDir"

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $releaseNotes) | Out-Null
New-Item -ItemType Directory -Force -Path $releasesDir | Out-Null

@"
# JunkCleaner v$Version

## Что исправлено

- Исправлены зависания при проверке обновлений: добавлены таймауты и явные сообщения об ошибках сети/SSL.
- Улучшен сетевой загрузчик Velopack для Windows-прокси/TLS сценариев.
- В «Остатках программ» добавлены фильтры по всем колонкам: Тип, Уверенность, Размер, Имя, Причина, Расположение.
- Таблица результатов автоматически подгоняет ширину колонок под окно без горизонтального скролла.

## Примечание

Если обновление не находится из-за сети, проверьте прокси/SSL и повторите попытку.
"@ | Set-Content -Path $releaseNotes -Encoding UTF8

Write-Host "`nPublishing self-contained..."
dotnet publish $solutionOrProject `
    -c Release `
    -r $Runtime `
    --self-contained true `
    "-p:Version=$Version" `
    -o $publishDir `
    | Write-Host

$vpk = Resolve-VpkExe
if (-not $vpk) {
    if ($SkipVpkInstall) {
        throw "Velopack CLI (vpk) not found. Install it: dotnet tool install -g vpk --version 0.0.1298"
    }

    Write-Host "`nInstalling Velopack CLI (vpk 0.0.1298)..."
    dotnet tool install --global vpk --version 0.0.1298 | Write-Host

    $vpk = Resolve-VpkExe
    if (-not $vpk) {
        throw "vpk.exe still not found after install. Open a NEW terminal window or set VPK_PATH to vpk.exe."
    }
}

Write-Host "`nPacking Velopack release with: $vpk"
& $vpk pack `
    --packId $PackId `
    --packTitle JunkCleaner `
    --packAuthors alekseichmsk `
    "--packVersion=$Version" `
    "--packDir=$publishDir" `
    --mainExe JunkCleaner.exe `
    "--runtime=$Runtime" `
    "--outputDir=$releasesDir" `
    "--releaseNotes=$releaseNotes" `
    | Write-Host

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$setup = Get-ChildItem $releasesDir -Filter "*-Setup.exe" -File | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $setup) {
    throw "Expected *-Setup.exe in $releasesDir, but none was found."
}

Write-Host "`nOK!"
Write-Host "Installer: $($setup.FullName)"
Write-Host "Feed:      $(Join-Path $releasesDir 'releases.win.json')"
exit 0
