param([switch]$Portable, [switch]$Test, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
if (-not $OutputDirectory) {
    [xml]$taskProject = Get-Content -LiteralPath (Join-Path $taskRoot 'app\Taskee.App\Taskee.App.csproj') -Raw
    $OutputDirectory = 'dist\Taskee-' + [string]$taskProject.Project.PropertyGroup.Version
}
Push-Location -LiteralPath $taskRoot
try {
    cmake -S . -B build -G 'Visual Studio 17 2022' -A x64
    if ($LASTEXITCODE) { throw 'Native configuration failed.' }
    cmake --build build --config Release
    if ($LASTEXITCODE) { throw 'Native build failed.' }
    dotnet build '.\app\Taskee.App\Taskee.App.csproj' -c Release
    if ($LASTEXITCODE) { throw 'Options app build failed.' }
    if ($Test) {
        ctest --test-dir build -C Release --output-on-failure
        if ($LASTEXITCODE) { throw 'Native taskbar checks failed.' }
        dotnet run --project '.\app\Taskee.Tests' -c Release
        if ($LASTEXITCODE) { throw 'Metric checks failed.' }
    }
    if ($Portable) {
        $taskOutput = if ([IO.Path]::IsPathRooted($OutputDirectory)) { [IO.Path]::GetFullPath($OutputDirectory) } else { [IO.Path]::GetFullPath((Join-Path $taskRoot $OutputDirectory)) }
        dotnet publish '.\app\Taskee.App\Taskee.App.csproj' -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o $taskOutput
        if ($LASTEXITCODE) { throw 'Portable build failed.' }
        Copy-Item -LiteralPath '.\THIRD-PARTY.md' -Destination (Join-Path $taskOutput 'THIRD-PARTY.md') -Force
        Copy-Item -LiteralPath '.\LICENSE' -Destination (Join-Path $taskOutput 'LICENSE') -Force
        Copy-Item -LiteralPath '.\third_party' -Destination $taskOutput -Recurse -Force
        Copy-Item -LiteralPath '.\docs\portable-readme.txt' -Destination (Join-Path $taskOutput 'README.txt') -Force
        Write-Host "Portable app: $taskOutput\Taskee.exe"
    }
} finally { Pop-Location }
