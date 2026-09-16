$ErrorActionPreference = 'Stop'
$appDir = Join-Path $PSScriptRoot 'Odysseus'
$runtime = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
& $runtime (Join-Path $appDir 'Odysseus.dll') --enable-startup
if ($LASTEXITCODE -ne 0) { throw 'Could not enable startup. The pet was not launched.' }
$actual = (Get-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run').OdysseusDesktopPet
$expected = '"' + (Join-Path $appDir 'Odysseus.exe') + '"'
if ($actual -ne $expected) { throw 'Startup verification failed.' }
& (Join-Path $appDir 'Launch.ps1')
Write-Output 'Odysseus is running, and startup at Windows sign-in is enabled for this account.'
