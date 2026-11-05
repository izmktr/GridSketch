param([Parameter(ValueFromRemainingArguments = $true)][string[]] $Options)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$OutputEncoding = [Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = $OutputEncoding

function Run {
    param([string] $Program, [string[]] $Arguments)
    $result = & $Program @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Program failed (exit $LASTEXITCODE)." }
    return $result
}
function Save-State {
    $state | ConvertTo-Json | Set-Content -LiteralPath "$statePath.tmp" -Encoding UTF8
    Move-Item -LiteralPath "$statePath.tmp" -Destination $statePath -Force
}
function Get-Sha256 {
    param([string] $Path)
    $stream = [IO.File]::OpenRead($Path)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        return [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '')
    } finally {
        $algorithm.Dispose()
        $stream.Dispose()
    }
}
function Find-Release {
    $items = @(Run gh @('api', '--paginate', "repos/$repo/releases", '--jq', '.[] | {tag_name, draft, prerelease}'))
    $releases = @($items | ForEach-Object { $_ | ConvertFrom-Json } | Where-Object { $_.tag_name -eq $tag })
    if ($releases.Count -gt 1) { throw "Multiple releases found for $tag." }
    if ($releases.Count -eq 1) { return $releases[0] }
    return $null
}
function Wait-Release {
    for ($attempt = 0; $attempt -lt 6; $attempt++) {
        $found = Find-Release
        if ($null -ne $found) { return $found }
        Start-Sleep -Seconds 2
    }
    throw "Release $tag is not visible yet. Retry with --resume $version."
}
function Test-ToolingChanges {
    param([string] $Commit)
    Run git @('merge-base', '--is-ancestor', $state.Commit, $Commit) | Out-Null
    $files = @(Run git @('diff', '--name-only', $state.Commit, $Commit))
    return @($files | Where-Object { $_ -notin @('release.ps1', 'release.bat') }).Count -eq 0
}
$usage = "Usage: release.bat --patch | --minor | --major | --version 1.2.3 | --resume 1.2.3"
if (!$Options -or $Options[0] -in @('--help', '-h')) {
    Write-Output $usage
    exit 0
}
$mode = $Options[0]
if (($mode -in @('--patch', '--minor', '--major') -and $Options.Count -ne 1) -or
    ($mode -in @('--version', '--resume') -and $Options.Count -ne 2) -or
    $mode -notin @('--patch', '--minor', '--major', '--version', '--resume')) {
    Write-Error $usage
    exit 1
}
if ($mode -in @('--version', '--resume') -and $Options[1] -notmatch '^\d+\.\d+\.\d+$') {
    Write-Error 'Specify a version such as 1.2.3.'
    exit 1
}

$repo = 'izmktr/GridSketch'
$lock = $null
Push-Location $PSScriptRoot
try {
    foreach ($tool in @('git', 'dotnet', 'gh')) { Get-Command $tool -ErrorAction Stop | Out-Null }
    $artifacts = Join-Path $PSScriptRoot 'artifacts'
    New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
    $lock = [IO.File]::Open((Join-Path $artifacts 'release.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
    $statePath = Join-Path $artifacts 'release-state.json'
    Run gh @('auth', 'status', '--hostname', 'github.com') | Out-Host
    if ((Run git @('branch', '--show-current')) -ne 'main') { throw 'Switch to main before releasing.' }
    $origin = Run git @('remote', 'get-url', 'origin')
    if ($origin -notmatch '^(https://github\.com/|git@github\.com:)izmktr/GridSketch(\.git)?$') {
        throw 'origin must point to izmktr/GridSketch on GitHub.'
    }
    Run git @('fetch', 'origin', 'main', '--tags') | Out-Host
    $headCommit = Run git @('rev-parse', 'HEAD')
    Run git @('merge-base', '--is-ancestor', 'origin/main', 'HEAD') | Out-Null

    if ($mode -eq '--resume') {
        if (!(Test-Path -LiteralPath $statePath)) { throw 'No saved release to resume.' }
        $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
        if ($state.Version -ne $Options[1]) { throw "Saved release is $($state.Version)." }
        if ($state.Complete) { throw 'This release is already complete.' }
    } else {
        if (Test-Path -LiteralPath $statePath) {
            $previous = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
            if (!$previous.Complete) { throw "Unfinished release. Run release.bat --resume $($previous.Version)" }
        }
        if (@(Run git @('status', '--porcelain')).Count) { throw 'Commit or stash all changes first.' }
        $latestTag = Run gh @('release', 'view', '--repo', $repo, '--json', 'tagName', '--jq', '.tagName')
        if ($latestTag -notmatch '^v(\d+\.\d+\.\d+)$') { throw 'Latest release must have a vMAJOR.MINOR.PATCH tag.' }
        $latest = [version] $Matches[1]
        if ($mode -eq '--version') {
            $version = [version] $Options[1]
        } else {
            $version = switch ($mode) {
                '--major' { [version] "$($latest.Major + 1).0.0" }
                '--minor' { [version] "$($latest.Major).$($latest.Minor + 1).0" }
                '--patch' { [version] "$($latest.Major).$($latest.Minor).$($latest.Build + 1)" }
            }
        }
        if ($version -le $latest) { throw "Version must be greater than $latest." }
        $tag = "v$version"
        if (Run git @('tag', '--list', $tag)) { throw "Tag $tag already exists." }
        if (Find-Release) { throw "Release $tag already exists." }
        $state = [pscustomobject] @{
            Version = "$version"; Commit = "$headCommit"; ZipHash = ''
            ReadmeCommit = ''; Complete = $false
        }
        Save-State
    }

    $version = $state.Version
    $tag = "v$version"
    $zipName = "GridSketch-$tag-win-x64.zip"
    $releaseDir = Join-Path $artifacts $tag
    $zipPath = Join-Path $releaseDir $zipName
    $readmePath = Join-Path $PSScriptRoot 'README.md'
    $readme = [IO.File]::ReadAllText($readmePath)
    $pattern = '\*\*\[GridSketch v\d+\.\d+\.\d+ をダウンロード（Windows x64・ZIP）\]\(https://github\.com/izmktr/GridSketch/releases/download/v\d+\.\d+\.\d+/GridSketch-v\d+\.\d+\.\d+-win-x64\.zip\)\*\*'
    if ([regex]::Matches($readme, $pattern).Count -ne 1) { throw 'Expected exactly one README download link.' }
    $link = "**[GridSketch $tag をダウンロード（Windows x64・ZIP）](https://github.com/$repo/releases/download/$tag/$zipName)**"
    $updatedReadme = [regex]::Replace($readme, $pattern, $link)
    $originalLines = @(Run git @('show', "$($state.Commit):README.md"))
    $originalReadme = $originalLines -join "`n"
    $expectedReadme = [regex]::Replace($originalReadme, $pattern, $link)
    $matchesExpectedReadme = ($readme.Replace("`r`n", "`n").TrimEnd("`n") -ceq $expectedReadme.TrimEnd("`n"))
    $changes = @(Run git @('status', '--porcelain'))
    $unexpectedChanges = @($changes | Where-Object {
        $readmeChange = $_ -in @(' M README.md', 'M  README.md', 'MM README.md') -and $matchesExpectedReadme
        $toolingChange = $mode -eq '--resume' -and $state.ZipHash -and $_ -in @(' M release.ps1', ' M release.bat')
        !$readmeChange -and !$toolingChange
    })
    if ($unexpectedChanges.Count) {
        throw 'Unexpected working tree changes. Commit or stash them before resuming.'
    }
    $releaseHead = $state.Commit
    if ($mode -eq '--resume' -and $state.ZipHash -and !$state.ReadmeCommit -and (Test-ToolingChanges $headCommit)) {
        $releaseHead = $headCommit
    }
    # Recover a README commit made just before an interrupted state-file write.
    if ($headCommit -ne $releaseHead -and !$state.ReadmeCommit) {
        $parent = Run git @('rev-parse', 'HEAD^')
        $subject = Run git @('log', '-1', '--format=%s')
        $files = @(Run git @('diff-tree', '--no-commit-id', '--name-only', '-r', 'HEAD'))
        if (!(Test-ToolingChanges $parent) -or $subject -ne "Update download link for $tag" -or
            $files.Count -ne 1 -or $files[0] -ne 'README.md' -or !$matchesExpectedReadme) {
            throw "Return to release commit $($state.Commit) before resuming."
        }
        $state.ReadmeCommit = "$headCommit"
        Save-State
    }
    $expectedHead = if ($state.ReadmeCommit) { $state.ReadmeCommit } else { $releaseHead }
    if ($headCommit -ne $expectedHead) { throw "Return to commit $expectedHead before resuming." }

    if (!$state.ZipHash) {
        if ($headCommit -ne $state.Commit -or $changes.Count) { throw 'Build requires the original clean release commit.' }
        Run dotnet @('run', '--project', 'GridSketch.Checks/GridSketch.Checks.csproj', '-c', 'Release') | Out-Host
        $appDir = Join-Path (Join-Path $releaseDir ([guid]::NewGuid().ToString('N'))) 'GridSketch'
        Run dotnet @('publish', 'GridSketch/GridSketch.csproj', '-c', 'Release', '-r', 'win-x64',
            '--self-contained', 'true', "-p:Version=$version", '-p:DebugType=None', '-o', $appDir) | Out-Host
        if (!(Test-Path (Join-Path $appDir 'GridSketch.exe'))) { throw 'GridSketch.exe missing.' }
        Compress-Archive -LiteralPath $appDir -DestinationPath $zipPath -Force
        $state.ZipHash = Get-Sha256 $zipPath
        Save-State
    }
    if (!(Test-Path -LiteralPath $zipPath) -or
        (Get-Sha256 $zipPath) -ne $state.ZipHash) {
        throw "ZIP missing or changed. Restore the original ZIP at $zipPath."
    }

    if (Run git @('tag', '--list', $tag)) {
        if ((Run git @('rev-parse', "$tag^{commit}")) -ne $state.Commit) { throw 'Tag points to a different commit.' }
    } else {
        Run git @('tag', $tag, $state.Commit) | Out-Host
    }
    Run git @('push', '--atomic', 'origin', 'main', "refs/tags/$tag") | Out-Host
    $release = Find-Release
    if (!$release) {
        $notesPath = Join-Path $releaseDir 'release-notes.md'
        [IO.File]::WriteAllText($notesPath,
            "Windows x64 版です。ZIP を展開し、GridSketch フォルダ内の GridSketch.exe を起動してください。.NET Desktop Runtime は同梱されています。",
            [Text.UTF8Encoding]::new($false))
        Run gh @('release', 'create', $tag, '--repo', $repo, '--verify-tag', '--draft',
            '--title', "GridSketch $tag", '--notes-file', $notesPath, '--generate-notes') | Out-Host
        $release = Wait-Release
    }
    if ($null -eq $release -or $null -eq $release.PSObject.Properties['prerelease'] -or
        $null -eq $release.PSObject.Properties['draft']) {
        throw "Unexpected release response. Retry with --resume $version."
    }
    if ($release.prerelease) { throw 'Existing release is marked as prerelease.' }
    $assetNames = @(Run gh @('release', 'view', $tag, '--repo', $repo, '--json', 'assets', '--jq', '.assets[].name'))
    if ($zipName -notin $assetNames) {
        if (!$release.draft) { throw 'Published release has no expected ZIP. Inspect it manually.' }
        Run gh @('release', 'upload', $tag, $zipPath, '--repo', $repo) | Out-Host
    }
    $verifyDir = Join-Path $releaseDir ('verify-' + [guid]::NewGuid().ToString('N'))
    Run gh @('release', 'download', $tag, '--repo', $repo, '--pattern', $zipName, '--dir', $verifyDir) | Out-Host
    if ((Get-Sha256 (Join-Path $verifyDir $zipName)) -ne $state.ZipHash) {
        throw 'Uploaded ZIP differs from the local ZIP.'
    }
    if ($release.draft) {
        Run gh @('release', 'edit', $tag, '--repo', $repo, '--draft=false', '--latest') | Out-Host
    }
    if (!$state.ReadmeCommit) {
        [IO.File]::WriteAllText($readmePath, $updatedReadme, [Text.UTF8Encoding]::new($false))
        Run git @('add', '--', 'README.md') | Out-Host
        Run git @('commit', '-m', "Update download link for $tag") | Out-Host
        $state.ReadmeCommit = "$(Run git @('rev-parse', 'HEAD'))"
        Save-State
    }
    Run git @('push', 'origin', 'main') | Out-Host
    $state.Complete = $true
    Save-State
    Write-Output "Published https://github.com/$repo/releases/tag/$tag"
    Write-Output "ZIP: $zipPath"
} catch {
    Write-Error -ErrorAction Continue $_
    Write-Output 'If a release state was saved, fix the problem and use --resume VERSION.'
    exit 1
} finally {
    if ($lock) { $lock.Dispose() }
    Pop-Location
}
