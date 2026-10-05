[CmdletBinding()]
param([string]$CompilerPath)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $CompilerPath) {
    $compiler = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($compiler) { $CompilerPath = $compiler.Source }
    else {
        $candidates = @(
            "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
            "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
            (Join-Path $projectRoot 'build\tools\innosetup\tools\ISCC.exe')
        )
        $CompilerPath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    }
}
if (-not $CompilerPath -or -not (Test-Path -LiteralPath $CompilerPath)) {
    throw 'Inno Setup 6.6 or later is required. Pass -CompilerPath with the path to ISCC.exe.'
}
& (Join-Path $PSScriptRoot 'Package.ps1')
$dist = Join-Path $projectRoot 'dist'
$package = Get-Content -LiteralPath (Join-Path $dist 'package.json') -Raw | ConvertFrom-Json
$version = $package.Version
& $CompilerPath '/Qp' "/DAppVersion=$version" "/DPackageDir=$($package.PackageDirectory)" "/DOutputDirPath=$dist" (Join-Path $PSScriptRoot 'Taskee.iss')
if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed: $LASTEXITCODE" }
$checksums = foreach ($artifact in @("Taskee-$version-win-x64-setup.exe", "Taskee-$version-win-x64.zip")) {
    $hash = Get-FileHash -LiteralPath (Join-Path $dist $artifact) -Algorithm SHA256
    '{0}  {1}' -f $hash.Hash.ToLowerInvariant(), $artifact
}
Set-Content -LiteralPath (Join-Path $dist "Taskee-$version-SHA256SUMS.txt") -Value $checksums -Encoding ascii
Write-Host "Installer ready: $(Join-Path $dist "Taskee-$version-win-x64-setup.exe")"
