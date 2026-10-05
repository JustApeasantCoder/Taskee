param([switch]$SelfTest)
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskBinaryRoot = Join-Path $taskRoot 'build\Release'
if (!(Test-Path -LiteralPath (Join-Path $taskBinaryRoot 'TaskeeTest.exe'))) {
    throw 'Build first: cmake -S . -B build -A x64; cmake --build build --config Release'
}
# A unique session directory lets development builds coexist with a resident
# diagnostics DLL. No installation or Explorer restart is needed.
$taskSessionName = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8)
$taskSessionRoot = Join-Path $taskRoot ('build\sessions\' + $taskSessionName)
New-Item -ItemType Directory -Path $taskSessionRoot -Force | Out-Null
foreach ($taskFileName in @('TaskeeTest.exe','TaskeeTap.dll')) {
    Copy-Item -LiteralPath (Join-Path $taskBinaryRoot $taskFileName) -Destination (Join-Path $taskSessionRoot $taskFileName)
}
$taskStartOptions = @{
    FilePath = Join-Path $taskSessionRoot 'TaskeeTest.exe'
    WindowStyle = 'Hidden'
    PassThru = $true
    RedirectStandardOutput = Join-Path $taskSessionRoot 'controller.log'
    RedirectStandardError = Join-Path $taskSessionRoot 'controller-error.log'
}
if ($SelfTest) { $taskStartOptions.ArgumentList = '--self-test' }
$taskProcess = Start-Process @taskStartOptions
[PSCustomObject]@{ ProcessId = $taskProcess.Id; SessionDirectory = $taskSessionRoot; Mode = $(if ($SelfTest) {'30-second automatic crowding test'} else {'3-minute interactive test'}) }
