[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$package = Get-Content -LiteralPath (Join-Path $projectRoot 'dist\package.json') -Raw | ConvertFrom-Json
$installer = Join-Path $projectRoot "dist\Taskee-$($package.Version)-win-x64-setup.exe"
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{85458756-5947-4781-A37F-F4F650F2E4ED}_is1'
if (Test-Path -LiteralPath $uninstallKey) { throw 'Taskee is already installed. Run installer QA in a separate Windows account to preserve that installation.' }
$qaRoot = Join-Path $projectRoot ('build\installer-qa\' + [Guid]::NewGuid().ToString('N'))
$installDir = Join-Path $qaRoot 'installed'
$captureDir = Join-Path $qaRoot 'captures'
[IO.Directory]::CreateDirectory($qaRoot) | Out-Null
$groupName = 'Taskee QA ' + (Split-Path $qaRoot -Leaf)
$groupDir = Join-Path ([Environment]::GetFolderPath('Programs')) $groupName
$profileDir = Join-Path $env:LOCALAPPDATA 'Taskee'
$startupKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
function Get-ProtectedState {
    $files = [ordered]@{}
    foreach ($name in @('settings.json', 'settings.json.bak')) {
        $path = Join-Path $profileDir $name
        $files[$name] = if (Test-Path -LiteralPath $path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash } else { $null }
    }
    $startup = Get-ItemProperty -LiteralPath $startupKey -Name Taskee -ErrorAction SilentlyContinue
    [ordered]@{ Files = $files; Startup = if ($startup) { $startup.Taskee } else { $null } } | ConvertTo-Json -Compress -Depth 4
}
function Invoke-QAProcess([string]$FilePath, [string]$Arguments, [int]$TimeoutMs = 60000) {
    $process = Start-Process -FilePath $FilePath -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit($TimeoutMs)) { throw "QA process did not finish: $FilePath (PID $($process.Id))" }
    if ($process.ExitCode -ne 0) { throw "QA process failed with exit code $($process.ExitCode): $FilePath" }
}
$protectedBefore = Get-ProtectedState
$explorerBefore = @(Get-Process explorer -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)
$uiProcess = $null
$payloadCount = 0
try {
    $installArgs = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOCLOSEAPPLICATIONS /TASKS= /DIR="' + $installDir + '" /GROUP="' + $groupName + '" /LOG="' + (Join-Path $qaRoot 'install.log') + '"'
    Invoke-QAProcess $installer $installArgs
    if (-not (Test-Path -LiteralPath $uninstallKey)) { throw 'The installer did not register its uninstaller.' }
    foreach ($source in Get-ChildItem -LiteralPath $package.PackageDirectory -File -Recurse) {
        $relative = [IO.Path]::GetRelativePath($package.PackageDirectory, $source.FullName)
        $target = Join-Path $installDir $relative
        if (-not (Test-Path -LiteralPath $target)) { throw "Installed payload missing: $relative" }
        if ((Get-FileHash -LiteralPath $source.FullName).Hash -ne (Get-FileHash -LiteralPath $target).Hash) { throw "Installed payload mismatch: $relative" }
        $payloadCount++
    }
    foreach ($name in @('Taskee.lnk', 'Taskee User Guide.lnk')) {
        if (-not (Test-Path -LiteralPath (Join-Path $groupDir $name))) { throw "Shortcut missing: $name" }
    }
    $uiArgs = '--ui-test --no-taskbar --tray --capture-dir "' + $captureDir + '"'
    $uiProcess = Start-Process -FilePath (Join-Path $installDir 'Taskee.exe') -ArgumentList $uiArgs -WindowStyle Hidden -PassThru
    if (-not $uiProcess.WaitForExit(30000)) { throw 'Installed options UI test timed out.' }
    if ($uiProcess.ExitCode -ne 0) { throw "Installed options UI test failed: $($uiProcess.ExitCode)" }
    foreach ($page in @('taskbar', 'appearance', 'sensors', 'general')) {
        if (-not (Test-Path -LiteralPath (Join-Path $captureDir "$page.png"))) { throw "Options page capture missing: $page" }
    }
    $historyDir = Join-Path $qaRoot 'history'
    Invoke-QAProcess (Join-Path $installDir 'Taskee.exe') ('--ui-test --no-taskbar --history-test --tray --capture-dir "' + $historyDir + '"') 30000
    $historyResult = Get-Content -LiteralPath (Join-Path $historyDir 'history-ui-checks.json') -Raw | ConvertFrom-Json
    if (-not $historyResult.Passed) { throw 'Installed history graph checks failed.' }
    # Reinstall the identical release to exercise the upgrade/repair path.
    Invoke-QAProcess $installer ($installArgs.Replace('install.log', 'reinstall.log'))
} finally {
    if ($uiProcess -and -not $uiProcess.HasExited) { $uiProcess.Kill(); $uiProcess.WaitForExit() }
    $uninstaller = Join-Path $installDir 'unins000.exe'
    if (Test-Path -LiteralPath $uninstaller) {
        Invoke-QAProcess $uninstaller ('/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG="' + (Join-Path $qaRoot 'uninstall.log') + '"')
    }
}
if (Test-Path -LiteralPath (Join-Path $installDir 'Taskee.exe')) { throw 'Uninstall left the installed executable.' }
if (Test-Path -LiteralPath $uninstallKey) { throw 'Uninstall registration was not removed.' }
if (Test-Path -LiteralPath $groupDir) { throw 'Uninstall did not remove the QA shortcuts.' }
if ($protectedBefore -ne (Get-ProtectedState)) { throw 'Installer QA changed the saved profile, backup or Windows startup entry.' }
$explorerAfter = @(Get-Process explorer -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)
if (@(Compare-Object $explorerBefore $explorerAfter).Count) { throw 'Explorer process identity changed during installer QA.' }
$result = [ordered]@{
    Version = $package.Version; PayloadFiles = $payloadCount; OptionsPages = 4; HistoryChecks = $historyResult.Checks.Count
    Install = 'passed'; Reinstall = 'passed'; Uninstall = 'passed'
    ProfilesAndStartup = 'unchanged'; Explorer = 'unchanged'; CaptureDirectory = $captureDir
}
$result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $qaRoot 'result.json') -Encoding utf8
$result | ConvertTo-Json
