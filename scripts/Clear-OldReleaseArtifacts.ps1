[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$CurrentVersion
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).ProviderPath
$current = [version]$CurrentVersion
$artifacts = Join-Path $repo 'artifacts'
$appName = 'BossMeter'
if (-not (Test-Path -LiteralPath (Join-Path $artifacts "publish-$CurrentVersion/$appName.exe") -PathType Leaf)) {
    $appName = 'LocalDpsTracker'
}
$zip = Join-Path $artifacts "$appName-$CurrentVersion-windows-x64.zip"
$exe = Join-Path $artifacts "publish-$CurrentVersion/$appName.exe"
foreach ($required in @($zip, "$zip.sha256", $exe)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Missing current release file: $required" }
}
$expected = (Get-Content -LiteralPath "$zip.sha256" -Raw).Trim().Split(' ')[0]
if ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne $expected) {
    throw 'Current ZIP failed checksum verification.'
}

$running = @(Get-Process -Name BossMeter, LocalDpsTracker, LocalDps -ErrorAction SilentlyContinue |
    Where-Object Path | Select-Object -ExpandProperty Path)
foreach ($folder in @('artifacts', 'distributions', 'work')) {
    $root = Join-Path $repo $folder
    if (-not (Test-Path -LiteralPath $root -PathType Container)) { continue }
    if ((Get-Item -LiteralPath $root -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw "Cleanup root is a link: $root"
    }
    foreach ($item in Get-ChildItem -LiteralPath $root -Force) {
        $isOld = switch ($folder) {
            'artifacts' {
                if ($item.Name -match '^(?:publish-|BossMeter-|LocalDps(?:Tracker)?-)(\d+\.\d+\.\d+)(?:-windows-x64\.zip(?:\.sha256)?)?$') {
                    [version]$Matches[1] -lt $current
                } else {
                    $item.PSIsContainer -and $item.Name -match '^(?:[a-z-]+-preview|preview-[a-z-]+|staging-[0-9a-f]{32})$'
                }
            }
            'distributions' {
                $item.Name -match '^(?:BossMeter|LocalDps(?:Tracker)?)-(\d+\.\d+\.\d+)-friends(?:\.zip)?$' -and [version]$Matches[1] -lt $current
            }
            'work' {
                $item.PSIsContainer -and $item.Name -match '^release-(\d+\.\d+\.\d+)$' -and [version]$Matches[1] -lt $current
            }
        }
        if (-not $isOld) { continue }

        $path = (Resolve-Path -LiteralPath $item.FullName).ProviderPath
        if ($path -ne (Join-Path $root $item.Name) -or -not $path.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw "Unexpected cleanup path: $path"
        }
        $relative = "$folder/$($item.Name)"
        $tracked = @(git -C $repo ls-files -- $relative)
        if ($LASTEXITCODE -ne 0) { throw "Could not check source control for $relative" }
        if ($tracked.Count -gt 0) { Write-Warning "Keeping tracked files in $relative"; continue }
        if ($running | Where-Object { $_.StartsWith($path + '\', [StringComparison]::OrdinalIgnoreCase) }) {
            Write-Warning "Keeping active build $relative"
            continue
        }
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
            Write-Warning "Keeping linked artifact $relative"
            continue
        }
        if ($item.PSIsContainer) {
            $protected = Get-ChildItem -LiteralPath $path -Force -Recurse | Where-Object {
                ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
                $_.Extension -in @('.ndjson', '.pcap', '.pcapng', '.cap', '.etl', '.har', '.saz')
            } | Select-Object -First 1
            if ($protected) { Write-Warning "Keeping diagnostics or links in $relative"; continue }
        }
        if ($folder -eq 'work') {
            $status = @(git -C $path status --porcelain)
            if ($LASTEXITCODE -ne 0 -or $status.Count -gt 0) { Write-Warning "Keeping checkout with source work $relative"; continue }
            $head = git -C $path rev-parse HEAD
            if ($LASTEXITCODE -ne 0) { throw "Could not verify commit in $relative" }
            git -C $repo merge-base --is-ancestor $head HEAD
            if ($LASTEXITCODE -ne 0) { Write-Warning "Keeping checkout with separate history $relative"; continue }
            if (Test-Path -LiteralPath (Join-Path $path '.git') -PathType Leaf) {
                git -C $repo worktree remove -- $path
                if ($LASTEXITCODE -ne 0) { throw "Could not remove worktree $relative" }
                Write-Output "Removed $relative"
                continue
            }
        }
        if ($item.PSIsContainer) { Remove-Item -LiteralPath $path -Recurse -Force }
        else { Remove-Item -LiteralPath $path -Force }
        Write-Output "Removed $relative"
    }
}