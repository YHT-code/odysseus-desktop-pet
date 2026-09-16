$ErrorActionPreference = 'Stop'
& (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe') (Join-Path $PSScriptRoot 'Odysseus\Odysseus.dll') --disable-startup
if ($LASTEXITCODE -ne 0) { throw 'Could not disable startup.' }
Write-Output 'Odysseus startup is off. Right-click the pet and choose Quit to close the current session.'
