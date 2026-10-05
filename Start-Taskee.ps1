param([switch]$Tray, [switch]$Stop)
$ErrorActionPreference = 'Stop'
$taskExecutable = Join-Path $PSScriptRoot 'dist\Taskee-0.1.1\Taskee.exe'
if (!(Test-Path -LiteralPath $taskExecutable)) { $taskExecutable = Join-Path $PSScriptRoot 'dist\Taskee\Taskee.exe' }
if (!(Test-Path -LiteralPath $taskExecutable)) { $taskExecutable = Join-Path $PSScriptRoot 'app\Taskee.App\bin\Release\net9.0-windows\Taskee.exe' }
if (!(Test-Path -LiteralPath $taskExecutable)) { throw 'Run .\Build.ps1 first.' }
if ($Stop) { & $taskExecutable --exit; return }
$taskOptions = @{ FilePath=$taskExecutable; WorkingDirectory=(Split-Path -Parent $taskExecutable) }
if ($Tray) { $taskOptions.ArgumentList='--tray'; $taskOptions.WindowStyle='Hidden' }
else { $taskOptions.WindowStyle='Normal' }
Start-Process @taskOptions
