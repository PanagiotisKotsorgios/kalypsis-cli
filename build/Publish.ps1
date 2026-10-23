[CmdletBinding()]
param(
    [string]$Version = '0.1.0',
    [string]$OutputDirectory = ''
)
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$artifactRoot = Join-Path $workspace 'artifacts'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $artifactRoot "release-$Version" }
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $outputRoot.StartsWith($artifactRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputDirectory must be inside this checkout''s artifacts directory.'
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must be a semantic version without a prefix.' }
$sourceVersion = ([xml](Get-Content (Join-Path $workspace 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
if ($sourceVersion -ne $Version) { throw 'Version differs from the source project version.' }
if (-not (Test-Path -LiteralPath (Join-Path $workspace '.git'))) { throw 'Packaging requires a Git checkout.' }
$sourceStatus = @(& git -C $workspace status --porcelain)
if ($LASTEXITCODE -ne 0 -or $sourceStatus.Count -ne 0) { throw 'Commit the source changes before packaging a release.' }
if (Test-Path -LiteralPath $outputRoot) { throw 'The output directory already exists. Use a fresh -OutputDirectory beneath artifacts.' }
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$env:TEMP = Join-Path $artifactRoot 'temp'; $env:TMP = $env:TEMP
if (-not $env:DOTNET_CLI_HOME) { $env:DOTNET_CLI_HOME = Join-Path $artifactRoot 'dotnet-home' }
if (-not $env:NUGET_PACKAGES) { $env:NUGET_PACKAGES = Join-Path $artifactRoot 'nuget-packages' }
New-Item -ItemType Directory -Path $env:TEMP,$env:DOTNET_CLI_HOME,$env:NUGET_PACKAGES -Force | Out-Null
$runtimeVersion = '10.0.13'
$publishRoot = Join-Path $outputRoot 'publish'
& dotnet publish (Join-Path $workspace 'src\Kalypsis.Cli\Kalypsis.Cli.csproj') -c Release -r win-x64 --self-contained true -o $publishRoot `
    /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:PublishTrimmed=false /p:DebugType=None /p:DebugSymbols=false `
    /p:RuntimeFrameworkVersion=$runtimeVersion /p:IncludeSourceRevisionInInformationalVersion=false
if ($LASTEXITCODE -ne 0) { throw 'Windows publish failed.' }
$executable = Join-Path $publishRoot 'kalypsis.exe'
$versionCheck = & $executable --version --json | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $versionCheck.version -ne $Version) { throw 'Published executable version mismatch.' }
Copy-Item -LiteralPath (Join-Path $workspace 'docs\USAGE.md'),(Join-Path $workspace 'docs\USAGE.el.md'),(Join-Path $workspace 'THIRD-PARTY-NOTICES.md') -Destination $publishRoot
$noticePath = Join-Path $publishRoot 'THIRD-PARTY-NOTICES.md'
[IO.File]::WriteAllText($noticePath, ([IO.File]::ReadAllText($noticePath).Replace('(docs/licenses/', '(licenses/')), [Text.UTF8Encoding]::new($false))
$licenses = Join-Path $publishRoot 'licenses'; New-Item -ItemType Directory -Path $licenses | Out-Null
Copy-Item -Path (Join-Path $workspace 'docs\licenses\*.txt') -Destination $licenses
$runtimePackage = Join-Path $env:NUGET_PACKAGES "microsoft.netcore.app.runtime.win-x64\$runtimeVersion"
foreach ($notice in @('LICENSE.TXT','THIRD-PARTY-NOTICES.TXT')) {
    $source = Join-Path $runtimePackage $notice
    if (-not (Test-Path -LiteralPath $source)) { throw "Missing runtime licence: $notice" }
    Copy-Item -LiteralPath $source -Destination (Join-Path $licenses "dotnet-$notice")
}
[IO.File]::WriteAllText((Join-Path $publishRoot 'README.txt'), @'
KALYPSIS CLI - Initial backoffice MVP

Open PowerShell in this folder and run .\kalypsis.exe for the interactive menu.
Run .\kalypsis.exe --help for commands. No .NET installation is required.
Greek guide: USAGE.el.md. English guide: USAGE.md.
Default data: %LOCALAPPDATA%\KalypsisCli\backoffice.db (created on first data command).
Use --db <path> for another database. Releases contain no customer data.
Licences and component notices are in THIRD-PARTY-NOTICES.md and licenses/.
This initial executable is unsigned; Windows may show a publisher warning.
'@, [Text.UTF8Encoding]::new($true))
$unexpected = Get-ChildItem -LiteralPath $publishRoot -Recurse -File | Where-Object { $_.Extension -in @('.db','.sqlite','.pfx','.csv','.pdb') -or $_.Name -match '^appsettings|password|credentials' }
if ($unexpected) { throw 'Unexpected private/development files in the publish output.' }
$releaseRoot = Join-Path $artifactRoot 'releases'; New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
$zip = Join-Path $releaseRoot "kalypsis-cli-$Version-win-x64.zip"
if (Test-Path -LiteralPath $zip) { throw 'Release ZIP already exists. Preserve published releases; use a new version.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($publishRoot, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
$sourceCommit = (& git -C $workspace rev-parse HEAD).Trim()
$buildInfo = [ordered]@{ version=$Version; framework='net10.0'; runtime='win-x64'; runtimeVersion=$runtimeVersion; selfContained=$true; singleFile=$true; signing='unsigned';
    sourceCommit=$sourceCommit; builtAtUtc=[DateTime]::UtcNow.ToString('o'); includesUserData=$false; sha256=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant() }
[IO.File]::WriteAllText((Join-Path $releaseRoot 'BUILD-INFO.json'), ($buildInfo | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
$hashes = Get-ChildItem -LiteralPath $releaseRoot -File | Where-Object { $_.Name -ne 'SHA256SUMS.txt' } | Sort-Object Name | ForEach-Object { "$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $($_.Name)" }
[IO.File]::WriteAllLines((Join-Path $releaseRoot 'SHA256SUMS.txt'), $hashes, [Text.UTF8Encoding]::new($false))
Write-Output "Packaged $Version for Windows x64: $zip"
