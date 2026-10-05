[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
[xml]$project = Get-Content -LiteralPath (Join-Path $projectRoot 'app\Taskee.App\Taskee.App.csproj') -Raw
$version = [string]$project.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'The app version must have three numeric components.' }
$packageDir = Join-Path $projectRoot ('build\packages\' + [Guid]::NewGuid().ToString('N') + '\Taskee')
& (Join-Path $projectRoot 'Build.ps1') -Test -Portable -OutputDirectory $packageDir
$dist = Join-Path $projectRoot 'dist'
[IO.Directory]::CreateDirectory($dist) | Out-Null
$zipPath = Join-Path $dist "Taskee-$version-win-x64.zip"
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory((Split-Path $packageDir -Parent), $zipPath, [IO.Compression.CompressionLevel]::Optimal, $false)
$hash = Get-FileHash -LiteralPath $zipPath -Algorithm SHA256
Set-Content -LiteralPath "$zipPath.sha256" -Value ('{0}  {1}' -f $hash.Hash.ToLowerInvariant(), (Split-Path $zipPath -Leaf)) -Encoding ascii
$result = [ordered]@{ Version = $version; PackageDirectory = $packageDir; PortableZip = $zipPath }
$result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $dist 'package.json') -Encoding utf8
Write-Host "Portable ZIP: $zipPath"
