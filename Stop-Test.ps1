$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build\Release\TaskeeTest.exe') --stop
