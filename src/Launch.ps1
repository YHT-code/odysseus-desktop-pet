$ErrorActionPreference = 'Stop'
$runtime = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
$appFile = Join-Path $PSScriptRoot 'Odysseus.dll'
if (-not (Test-Path -LiteralPath $runtime)) { throw 'The Microsoft .NET 10 Desktop Runtime is required.' }
Start-Process -FilePath $runtime -ArgumentList ('"' + $appFile + '"') -WorkingDirectory $PSScriptRoot -WindowStyle Hidden
