@echo off
setlocal
set "GRIDSKETCH_ROOT=%~dp0"
set "GRIDSKETCH_VERSION=%~1"
if not defined GRIDSKETCH_VERSION set "GRIDSKETCH_VERSION=1.0.0"

powershell.exe -NoProfile -Command ^
    "$ErrorActionPreference = 'Stop';" ^
    "$version = $env:GRIDSKETCH_VERSION;" ^
    "if ($version -notmatch '\A[0-9]+\.[0-9]+\.[0-9]+\z') { throw 'Use a version such as 1.0.0.' };" ^
    "$releaseDir = Join-Path ([IO.Path]::GetTempPath()) ('GridSketch-v' + $version + '-' + [guid]::NewGuid().ToString('N'));" ^
    "$appDir = Join-Path $releaseDir 'GridSketch';" ^
    "$zipPath = Join-Path $releaseDir ('GridSketch-v' + $version + '-win-x64.zip');" ^
    "$project = Join-Path $env:GRIDSKETCH_ROOT 'GridSketch\GridSketch.csproj';" ^
    "dotnet publish $project -c Release -r win-x64 --self-contained true ('-p:Version=' + $version) -p:DebugType=None -o $appDir;" ^
    "if ($LASTEXITCODE -ne 0) { throw 'GridSketch publish failed.' };" ^
    "Compress-Archive -LiteralPath $appDir -DestinationPath $zipPath;" ^
    "Write-Output $zipPath;"
exit /b %errorlevel%
