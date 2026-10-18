[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
if (-not [Environment]::Is64BitProcess -or $env:OS -ne 'Windows_NT') {
    throw 'Build on Windows x64 with the .NET 8 SDK.'
}
$repoRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repoRoot 'src/LocalDps/LocalDps.csproj'
[xml]$projectXml = Get-Content -LiteralPath $project -Raw
$version = [string]$projectXml.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Expected a three-part project version.' }
$artifacts = Join-Path $repoRoot 'artifacts'
$publish = Join-Path $artifacts ('publish-' + $version)

& dotnet build $project -c Release
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$testExe = Join-Path $repoRoot 'src/LocalDps/bin/Release/net8.0-windows/LocalDpsTracker.exe'
$test = Start-Process -FilePath $testExe -ArgumentList '--self-test' -Wait -PassThru -WindowStyle Hidden
if ($test.ExitCode -ne 0) {
    $failure = Join-Path (Split-Path $testExe) 'self-test-failure.txt'
    if (Test-Path -LiteralPath $failure) { Get-Content -LiteralPath $failure }
    throw 'Offline self-tests failed.'
}
& dotnet publish $project -c Release --no-build --no-restore -o $publish
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

# A fresh staging directory and explicit allowlist prevent private files from
# previous app runs or local development from entering a public release.
$packageName = 'LocalDpsTracker-' + $version + '-windows-x64'
$stage = Join-Path $artifacts ('staging-' + [Guid]::NewGuid().ToString('N'))
$package = Join-Path $stage $packageName
[IO.Directory]::CreateDirectory($package) | Out-Null
foreach ($name in @('LocalDpsTracker.exe','LocalDpsTracker.dll','LocalDpsTracker.deps.json','LocalDpsTracker.runtimeconfig.json')) {
    Copy-Item -LiteralPath (Join-Path $publish $name) -Destination (Join-Path $package $name)
}
[IO.File]::WriteAllText((Join-Path $package 'track-people.json'), '{"selfName":""}')
[IO.File]::WriteAllText((Join-Path $package 'learned-character-names.json'), '[]')
foreach ($name in @('LICENSE','NOTICE.md')) {
    Copy-Item -LiteralPath (Join-Path $repoRoot $name) -Destination (Join-Path $package $name)
}
@'
Local DPS Tracker for Aion 2

Install Microsoft .NET 8 Desktop Runtime (Windows x64) and Npcap separately:
https://dotnet.microsoft.com/en-us/download/dotnet/8.0
https://npcap.com/#download

Extract the complete ZIP into a writable folder. Enter Aion 2, open LocalDpsTracker.exe,
and click Start capturing. Adapter and ports are automatic. Character identity
arrives during area loading: if your name is missing, keep capture running and
teleport once. Opening equipment does not resend your identity.
Or use the optional name field. Names are saved locally across release folders.

The app includes 6,084 English enemy names for offline use. Keep capture running
and teleport once to receive NPC spawn mappings. Unknown targets may be absent
from the catalog or missing a spawn message. Optional local target-names.json
entries can override or extend the bundled names. See NOTICE.md for attribution.

No account, AionFlex install, telemetry, or automatic uploads. Diagnostic recordings
are saved only on request and can contain names/identifiers; keep them private.

Source, full instructions, known limits and issue tracker:
https://github.com/sccdmaomao/aion2-dps-tracker
'@ | Set-Content -LiteralPath (Join-Path $package 'START-HERE.txt')
$zip = Join-Path $artifacts ($packageName + '.zip')
Compress-Archive -LiteralPath $package -DestinationPath $zip -CompressionLevel Optimal -Force
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($zip + '.sha256', $hash + '  ' + [IO.Path]::GetFileName($zip) + "`n")
Write-Host "Build and offline tests passed. Release: $zip"
