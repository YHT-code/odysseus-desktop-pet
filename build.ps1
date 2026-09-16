$ErrorActionPreference = 'Stop'
$outputDir = Join-Path $PSScriptRoot 'Odysseus'
New-Item -ItemType Directory -Force -Path (Join-Path $outputDir 'assets') | Out-Null
& dotnet build (Join-Path $PSScriptRoot 'src\Odysseus.csproj') -c Release -o $outputDir --nologo
if ($LASTEXITCODE -ne 0) { throw 'Odysseus build failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src\Launch.ps1') -Destination (Join-Path $outputDir 'Launch.ps1')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'art\odysseus-pixel\final\spritesheet.png') -Destination (Join-Path $outputDir 'assets\spritesheet.png') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'art\odysseus-pixel\final\sleeping.png') -Destination (Join-Path $outputDir 'assets\sleeping.png') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'art\odysseus-pixel\final\odysseus.ico') -Destination (Join-Path $outputDir 'assets\odysseus.ico') -Force
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut((Join-Path $outputDir 'Start Odysseus.lnk'))
$shortcut.TargetPath = Join-Path $outputDir 'Odysseus.exe'
$shortcut.WorkingDirectory = $outputDir
$shortcut.IconLocation = Join-Path $outputDir 'assets\odysseus.ico'
$shortcut.Description = 'Your pixel-art Odysseus desktop companion'
$shortcut.Save()
