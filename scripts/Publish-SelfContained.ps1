[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',

    [ValidateSet('win-x64')]
    [string]$Runtime = 'win-x64',

    [string]$OutputDirectory = ''
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$OutputDirectory = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path $repositoryRoot 'artifacts\publish\win-x64'
} else {
    $OutputDirectory
}
$projectPath = Join-Path $repositoryRoot 'src\AnchorHoleWorkcell\AnchorHoleWorkcell.csproj'
$arenaAssembly = 'C:\Program Files\LUCID Vision Labs\Arena SDK\x64Release\ArenaNET_MP.dll'

if (-not (Test-Path -LiteralPath $arenaAssembly)) {
    throw 'LUCID Arena SDK is required at C:\Program Files\LUCID Vision Labs\Arena SDK.'
}

$dotnetCommand = Get-Command dotnet -ErrorAction Stop
$dotnetRoot = Split-Path $dotnetCommand.Source -Parent
$dotnetLicense = Join-Path $dotnetRoot 'LICENSE.txt'
$dotnetNotices = Join-Path $dotnetRoot 'ThirdPartyNotices.txt'
$sdkVersion = (& dotnet --version).Trim()
$windowsDesktopSdk = Join-Path $dotnetRoot "sdk\$sdkVersion\Sdks\Microsoft.NET.Sdk.WindowsDesktop"

& dotnet publish $projectPath `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    --output $OutputDirectory

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$licenseDirectory = Join-Path $OutputDirectory 'licenses'
New-Item -ItemType Directory -Force -Path $licenseDirectory | Out-Null

Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination (Join-Path $licenseDirectory 'ANCHOR_HOLE_WORKCELL_MIT.txt') -Force
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'THIRD_PARTY_NOTICES.md') -Destination (Join-Path $licenseDirectory 'THIRD_PARTY_NOTICES.md') -Force

if (Test-Path -LiteralPath $dotnetLicense) {
    Copy-Item -LiteralPath $dotnetLicense -Destination (Join-Path $licenseDirectory 'MICROSOFT_DOTNET_LICENSE.txt') -Force
}
if (Test-Path -LiteralPath $dotnetNotices) {
    Copy-Item -LiteralPath $dotnetNotices -Destination (Join-Path $licenseDirectory 'MICROSOFT_DOTNET_THIRD_PARTY_NOTICES.txt') -Force
}

$wpfLicense = Join-Path $windowsDesktopSdk 'LICENSE.TXT'
$wpfNotices = Join-Path $windowsDesktopSdk 'THIRD-PARTY-NOTICES.TXT'
if (Test-Path -LiteralPath $wpfLicense) {
    Copy-Item -LiteralPath $wpfLicense -Destination (Join-Path $licenseDirectory 'MICROSOFT_WPF_LICENSE.txt') -Force
}
if (Test-Path -LiteralPath $wpfNotices) {
    Copy-Item -LiteralPath $wpfNotices -Destination (Join-Path $licenseDirectory 'MICROSOFT_WPF_THIRD_PARTY_NOTICES.txt') -Force
}

$requiredNotices = @(
    'ANCHOR_HOLE_WORKCELL_MIT.txt',
    'THIRD_PARTY_NOTICES.md',
    'MICROSOFT_DOTNET_LICENSE.txt',
    'MICROSOFT_DOTNET_THIRD_PARTY_NOTICES.txt',
    'MICROSOFT_WPF_LICENSE.txt',
    'MICROSOFT_WPF_THIRD_PARTY_NOTICES.txt'
)

$missing = $requiredNotices | Where-Object { -not (Test-Path -LiteralPath (Join-Path $licenseDirectory $_)) }
if ($missing.Count -gt 0) {
    throw "Publish completed, but required license files are missing: $($missing -join ', ')"
}

Write-Host "Self-contained package created: $OutputDirectory"
Write-Host 'Microsoft, WPF, project and third-party notices were copied to the licenses directory.'
Write-Warning 'ArenaNET_MP.dll is LUCID software. Follow the Arena SDK distribution documentation and the agreement for the exact installed SDK version.'
