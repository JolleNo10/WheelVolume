param(
    [ValidateSet("Prepare", "Publish")]
    [string]$Mode = "Prepare",

    [string]$Version,

    [string]$MainBranch = "main",

    [string]$Remote = "origin",

    [string[]]$Fixes,

    [switch]$DryRun,

    [switch]$SkipTests,

    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]]$Arguments
    )

    Write-Host ">$FilePath $($Arguments -join ' ')"

    if ($DryRun) {
        return
    }

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code $LASTEXITCODE`: $FilePath $($Arguments -join ' ')"
    }
}

function Invoke-OutputCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]]$Arguments
    )

    Write-Host ">$FilePath $($Arguments -join ' ')"

    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = & $FilePath @Arguments 2>&1
    } finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code $LASTEXITCODE`: $FilePath $($Arguments -join ' ')`n$output"
    }

    return ($output -join "`n").Trim()
}

function Invoke-OptionalOutputCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]]$Arguments
    )

    Write-Host ">$FilePath $($Arguments -join ' ')"

    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = & $FilePath @Arguments 2>&1
    } finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
    if ($LASTEXITCODE -ne 0) {
        return $null
    }

    return ($output -join "`n").Trim()
}

function Assert-CommandAvailable {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Command
    )

    if (-not (Get-Command $Command -ErrorAction SilentlyContinue)) {
        throw "Required command '$Command' was not found on PATH."
    }
}

function Read-YesNo {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Prompt
    )

    while ($true) {
        $answer = Read-Host "$Prompt [y/n]"
        switch ($answer.ToLowerInvariant()) {
            "y" { return $true }
            "yes" { return $true }
            "n" { return $false }
            "no" { return $false }
            default { Write-Host "Answer y or n." }
        }
    }
}

function Get-SemVerParts {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    if ($Value -notmatch '^(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)$') {
        throw "Invalid release version '$Value'. Use values like 1.2.3."
    }

    [pscustomobject]@{
        Major = [int]$Matches.major
        Minor = [int]$Matches.minor
        Patch = [int]$Matches.patch
    }
}

function Compare-SemVer {
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject]$Left,

        [Parameter(Mandatory = $true)]
        [pscustomobject]$Right
    )

    foreach ($part in @("Major", "Minor", "Patch")) {
        if ($Left.$part -gt $Right.$part) { return 1 }
        if ($Left.$part -lt $Right.$part) { return -1 }
    }

    return 0
}

function Get-ProjectVersionFromContent {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Content
    )

    [xml]$project = $Content
    $version = $project.Project.PropertyGroup.Version
    if ([string]::IsNullOrWhiteSpace($version) -or $version -eq '$(VersionPrefix)') {
        $version = $project.Project.PropertyGroup.VersionPrefix
    }

    return $version
}

function Assert-CleanWorkingTree {
    if ((git status --porcelain) -and -not $DryRun) {
        throw "Working tree is not clean. Commit or stash existing changes before running the release script."
    }
}

function Test-WorkingTreeHasChanges {
    param(
        [string[]]$Paths
    )

    if ($DryRun) {
        return $false
    }

    $arguments = @("status", "--porcelain", "--")
    $arguments += $Paths
    $status = & git @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Could not check working tree changes."
    }

    return -not [string]::IsNullOrWhiteSpace(($status -join "`n"))
}

function Set-XmlElementText {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Content,

        [Parameter(Mandatory = $true)]
        [string]$ElementName,

        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    $pattern = "(<$ElementName>)[^<]*(</$ElementName>)"
    if ($Content -notmatch $pattern) {
        throw "Could not find <$ElementName> in project file."
    }

    return [regex]::Replace(
        $Content,
        $pattern,
        {
            param($match)
            "$($match.Groups[1].Value)$Value$($match.Groups[2].Value)"
        },
        1)
}

function Set-TextFileContentPreservingUtf8Bom {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Content
    )

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xef -and $bytes[1] -eq 0xbb -and $bytes[2] -eq 0xbf
    $encoding = New-Object System.Text.UTF8Encoding($hasBom)
    [System.IO.File]::WriteAllText($Path, $Content, $encoding)
}

function Update-ReleaseVersionFiles {
    param(
        [string]$ProjectPath,
        [string]$ReadmePath,
        [string]$Version
    )

    Write-Host "Updating release version files for v$Version"

    if ($DryRun) {
        Write-Host ">Update $ProjectPath VersionPrefix/AssemblyVersion/FileVersion to $Version"
        Write-Host ">Update $ReadmePath release links and publish version examples to v$Version"
        return
    }

    $project = Get-Content -LiteralPath $ProjectPath -Raw
    $project = Set-XmlElementText -Content $project -ElementName "VersionPrefix" -Value $Version
    $project = Set-XmlElementText -Content $project -ElementName "AssemblyVersion" -Value "$Version.0"
    $project = Set-XmlElementText -Content $project -ElementName "FileVersion" -Value "$Version.0"
    Set-TextFileContentPreservingUtf8Bom -Path $ProjectPath -Content $project

    $readme = Get-Content -LiteralPath $ReadmePath -Raw
    $readme = [regex]::Replace($readme, 'WheelVolume-v\d+\.\d+\.\d+-portable-win-x64\.zip', "WheelVolume-v$Version-portable-win-x64.zip")
    $readme = [regex]::Replace($readme, 'WheelVolume-v\d+\.\d+\.\d+-win-x64\.zip', "WheelVolume-v$Version-win-x64.zip")
    $readme = [regex]::Replace($readme, '/releases/download/v\d+\.\d+\.\d+/', "/releases/download/v$Version/")
    $readme = [regex]::Replace($readme, '-p:Version=\d+\.\d+\.\d+', "-p:Version=$Version")
    $readme = [regex]::Replace($readme, '-p:FileVersion=\d+\.\d+\.\d+\.0', "-p:FileVersion=$Version.0")
    Set-TextFileContentPreservingUtf8Bom -Path $ReadmePath -Content $readme
}

function Commit-AndPushReleaseVersionFiles {
    param(
        [string]$ProjectPath,
        [string]$ReadmePath,
        [string]$Version,
        [string]$Remote,
        [string]$Branch
    )

    $paths = @($ProjectPath, $ReadmePath)
    if (-not (Test-WorkingTreeHasChanges -Paths $paths)) {
        Write-Host "Release version files are already up to date."
        return
    }

    Invoke-CheckedCommand -FilePath "git" -Arguments @("add", "--", $ProjectPath, $ReadmePath)
    Invoke-CheckedCommand -FilePath "git" -Arguments @("commit", "-m", "Prepare v$Version release version files")
    Invoke-CheckedCommand -FilePath "git" -Arguments @("push", "-u", $Remote, $Branch)
}

function Test-LocalTagExists {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Tag
    )

    if ($DryRun) {
        return $false
    }

    git rev-parse -q --verify "refs/tags/$Tag" *> $null
    return ($LASTEXITCODE -eq 0)
}

function Test-RemoteTagExists {
    param(
        [string]$Remote,
        [string]$Tag
    )

    if ($DryRun) {
        return $false
    }

    $result = git ls-remote --tags $Remote $Tag
    if ($LASTEXITCODE -ne 0) {
        throw "Could not check remote tag '$Remote/$Tag'."
    }

    return -not [string]::IsNullOrWhiteSpace($result)
}

function Assert-ReleaseVersionIsSet {
    param(
        [string]$ProjectPath,
        [string]$ReadmePath,
        [string]$Version
    )

    [xml]$project = Get-Content -LiteralPath $ProjectPath -Raw
    $propertyGroup = $project.Project.PropertyGroup
    $errors = @()

    if ($propertyGroup.VersionPrefix -ne $Version) {
        $errors += "WheelVolume.csproj VersionPrefix is '$($propertyGroup.VersionPrefix)', expected '$Version'."
    }

    if ($propertyGroup.Version -ne '$(VersionPrefix)' -and $propertyGroup.Version -ne $Version) {
        $errors += "WheelVolume.csproj Version is '$($propertyGroup.Version)', expected '$(VersionPrefix)' or '$Version'."
    }

    if ($propertyGroup.AssemblyVersion -ne "$Version.0") {
        $errors += "WheelVolume.csproj AssemblyVersion is '$($propertyGroup.AssemblyVersion)', expected '$Version.0'."
    }

    if ($propertyGroup.FileVersion -ne "$Version.0") {
        $errors += "WheelVolume.csproj FileVersion is '$($propertyGroup.FileVersion)', expected '$Version.0'."
    }

    $readme = Get-Content -LiteralPath $ReadmePath -Raw
    foreach ($expected in @(
        "WheelVolume-v$Version-portable-win-x64.zip",
        "WheelVolume-v$Version-win-x64.zip",
        "/releases/download/v$Version/",
        "-p:Version=$Version",
        "-p:FileVersion=$Version.0"
    )) {
        if (-not $readme.Contains($expected)) {
            $errors += "README.md is missing '$expected'."
        }
    }

    if ($errors.Count -gt 0) {
        throw "Release version files are not set correctly:`n$($errors -join "`n")"
    }
}

function Assert-ReleaseDoesNotExist {
    param(
        [string]$TagName,
        [string]$Remote
    )

    if (Test-LocalTagExists -Tag $TagName) {
        throw "Tag '$TagName' already exists locally."
    }

    if (Test-RemoteTagExists -Remote $Remote -Tag $TagName) {
        throw "Tag '$Remote/$TagName' already exists."
    }

    if (-not $DryRun) {
        $existingReleaseUrl = Invoke-OptionalOutputCommand -FilePath "gh" -Arguments @("release", "view", $TagName, "--json", "url", "--jq", ".url")
        if (-not [string]::IsNullOrWhiteSpace($existingReleaseUrl)) {
            throw "GitHub release '$TagName' already exists: $existingReleaseUrl"
        }
    }
}

function Get-OrCreatePullRequest {
    param(
        [string]$BaseBranch,
        [string]$HeadBranch,
        [string]$TagName,
        [string[]]$Fixes
    )

    if ($DryRun) {
        return "Dry run: pull request URL would be printed here."
    }

    $existingUrl = Invoke-OptionalOutputCommand -FilePath "gh" -Arguments @("pr", "list", "--base", $BaseBranch, "--head", $HeadBranch, "--state", "open", "--json", "url", "--jq", ".[0].url")
    if (-not [string]::IsNullOrWhiteSpace($existingUrl)) {
        return $existingUrl
    }

    $bodyLines = @(
        "## Release $TagName",
        "",
        "Prepare WheelVolume $TagName release.",
        "",
        "## Testing",
        "",
        "- [ ] Release branch build/test completed",
        "- [ ] Release branch reviewed",
        "- [ ] Ready to merge to $BaseBranch"
    )

    if ($Fixes -and $Fixes.Count -gt 0) {
        $bodyLines += ""
        $bodyLines += "## Issues"
        $bodyLines += ""
        foreach ($issue in $Fixes) {
            $cleanIssue = $issue.Trim()
            if ($cleanIssue -notmatch '^#') {
                $cleanIssue = "#$cleanIssue"
            }
            $bodyLines += "Fixes $cleanIssue"
        }
    }

    $body = $bodyLines -join "`n"

    return Invoke-OutputCommand -FilePath "gh" -Arguments @("pr", "create", "--base", $BaseBranch, "--head", $HeadBranch, "--title", "Release $TagName", "--body", $body)
}

function Invoke-ReleaseChecks {
    if (-not $SkipTests) {
        Invoke-CheckedCommand -FilePath "dotnet" -Arguments @("run", "--project", ".\WheelVolume.Tests\WheelVolume.Tests.csproj", "-c", "Release")
    }

    Invoke-CheckedCommand -FilePath "dotnet" -Arguments @("build", ".\WheelVolume.sln", "-c", "Release")
}

function Publish-ReleaseArtifacts {
    param(
        [string]$RepoRoot,
        [string]$ReleaseVersion,
        [string]$TagName
    )

    $releaseRoot = Join-Path $RepoRoot "release"
    $portableDir = Join-Path $releaseRoot "WheelVolume-portable-win-x64"
    $runtimeDir = Join-Path $releaseRoot "WheelVolume-win-x64"
    $portableZip = Join-Path $releaseRoot "WheelVolume-$TagName-portable-win-x64.zip"
    $runtimeZip = Join-Path $releaseRoot "WheelVolume-$TagName-win-x64.zip"
    $checksumsPath = Join-Path $releaseRoot "SHA256SUMS.txt"

    if (-not $DryRun) {
        New-Item -ItemType Directory -Force -Path $releaseRoot | Out-Null
        Remove-Item -LiteralPath $portableDir, $runtimeDir, $portableZip, $runtimeZip, $checksumsPath -Recurse -Force -ErrorAction SilentlyContinue
    }

    Invoke-CheckedCommand -FilePath "dotnet" -Arguments @("publish", ".\WheelVolume\WheelVolume.csproj", "-c", "Release", "-r", "win-x64", "--self-contained", "true", "-p:Version=$ReleaseVersion", "-p:FileVersion=$ReleaseVersion.0", "-p:PublishSingleFile=true", "-p:DebugType=None", "-p:DebugSymbols=false", "-o", $portableDir) | Out-Host
    Invoke-CheckedCommand -FilePath "dotnet" -Arguments @("publish", ".\WheelVolume\WheelVolume.csproj", "-c", "Release", "-r", "win-x64", "--self-contained", "false", "-p:Version=$ReleaseVersion", "-p:FileVersion=$ReleaseVersion.0", "-p:PublishSingleFile=true", "-p:DebugType=None", "-p:DebugSymbols=false", "-o", $runtimeDir) | Out-Host

    if ($DryRun) {
        Write-Host ">Compress-Archive $portableDir -> $portableZip"
        Write-Host ">Compress-Archive $runtimeDir -> $runtimeZip"
        Write-Host ">Get-FileHash -> $checksumsPath"
        return @($portableZip, $runtimeZip, $checksumsPath)
    }

    Compress-Archive -Path (Join-Path $portableDir "*") -DestinationPath $portableZip -Force
    Compress-Archive -Path (Join-Path $runtimeDir "*") -DestinationPath $runtimeZip -Force

    $hashLines = @()
    foreach ($zip in @($portableZip, $runtimeZip)) {
        $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $zip
        $hashLines += "$($hash.Hash.ToLowerInvariant())  $(Split-Path -Leaf $zip)"
    }
    Set-Content -LiteralPath $checksumsPath -Value ($hashLines -join "`n")

    return @($portableZip, $runtimeZip, $checksumsPath)
}

$scriptRoot = if ([string]::IsNullOrWhiteSpace($PSScriptRoot)) {
    Split-Path -Parent $MyInvocation.MyCommand.Path
} else {
    $PSScriptRoot
}

Assert-CommandAvailable "git"

$repoRoot = (git -C $scriptRoot rev-parse --show-toplevel).Trim()
Set-Location $repoRoot

Assert-CommandAvailable "dotnet"
if (-not $DryRun) {
    Assert-CommandAvailable "gh"
    Invoke-CheckedCommand -FilePath "gh" -Arguments @("auth", "status")
}

$projectPath = Join-Path $repoRoot "WheelVolume\WheelVolume.csproj"
$readmePath = Join-Path $repoRoot "README.md"
$currentBranch = (git branch --show-current).Trim()

if ($Mode -eq "Prepare") {
    if ($currentBranch -notmatch '^release/(?<version>[0-9]+\.[0-9]+\.[0-9]+)$') {
        throw "Prepare mode must be run from a release branch named release/x.x.x. Current branch is '$currentBranch'."
    }

    $releaseVersion = $Matches.version
    $releaseParts = Get-SemVerParts $releaseVersion
    $tagName = "v$releaseVersion"

    Assert-CleanWorkingTree
    Invoke-CheckedCommand -FilePath "git" -Arguments @("fetch", "--tags", $Remote, $MainBranch)

    $mainRef = "$Remote/$MainBranch"
    $mainProject = Invoke-OutputCommand -FilePath "git" -Arguments @("show", "${mainRef}:WheelVolume/WheelVolume.csproj")
    $mainVersionText = Get-ProjectVersionFromContent $mainProject
    $mainParts = Get-SemVerParts $mainVersionText

    if ((Compare-SemVer -Left $releaseParts -Right $mainParts) -le 0) {
        throw "Release branch version '$releaseVersion' must be higher than $mainRef version '$mainVersionText'."
    }

    Assert-ReleaseDoesNotExist -TagName $tagName -Remote $Remote
    Update-ReleaseVersionFiles -ProjectPath $projectPath -ReadmePath $readmePath -Version $releaseVersion
    Assert-ReleaseVersionIsSet -ProjectPath $projectPath -ReadmePath $readmePath -Version $releaseVersion

    if (-not (Read-YesNo "Prepare release $tagName from branch $currentBranch into $MainBranch?")) {
        throw "Release preparation cancelled."
    }

    Commit-AndPushReleaseVersionFiles -ProjectPath $projectPath -ReadmePath $readmePath -Version $releaseVersion -Remote $Remote -Branch $currentBranch
    Assert-CleanWorkingTree
    Invoke-ReleaseChecks
    Invoke-CheckedCommand -FilePath "git" -Arguments @("push", "-u", $Remote, $currentBranch)

    $prUrl = Get-OrCreatePullRequest -BaseBranch $MainBranch -HeadBranch $currentBranch -TagName $tagName -Fixes $Fixes

    Write-Host ""
    Write-Host "Release PR ready: $prUrl"
    Write-Host ""
    Write-Host "Next step: review and merge the PR manually."
    Write-Host "After merge, run:"
    Write-Host "  .\scripts\release.ps1 -Mode Publish -Version $releaseVersion"
    return
}

if ($Mode -eq "Publish") {
    if ([string]::IsNullOrWhiteSpace($Version)) {
        if ($currentBranch -match '^release/(?<version>[0-9]+\.[0-9]+\.[0-9]+)$') {
            $Version = $Matches.version
        } else {
            throw "Publish mode requires -Version x.x.x unless you are currently on release/x.x.x."
        }
    }

    $releaseVersion = $Version
    $releaseParts = Get-SemVerParts $releaseVersion
    $tagName = "v$releaseVersion"

    Assert-CleanWorkingTree
    Invoke-CheckedCommand -FilePath "git" -Arguments @("fetch", "--tags", $Remote, $MainBranch)
    Assert-ReleaseDoesNotExist -TagName $tagName -Remote $Remote

    if (-not (Read-YesNo "Publish $tagName from $MainBranch? This assumes the release PR has already been merged.")) {
        throw "Release publish cancelled."
    }

    Invoke-CheckedCommand -FilePath "git" -Arguments @("switch", $MainBranch)
    Invoke-CheckedCommand -FilePath "git" -Arguments @("pull", "--ff-only", $Remote, $MainBranch)

    Assert-ReleaseVersionIsSet -ProjectPath $projectPath -ReadmePath $readmePath -Version $releaseVersion
    Invoke-ReleaseChecks

    $releaseFiles = @()
    if (-not $SkipBuild) {
        $releaseFiles = Publish-ReleaseArtifacts -RepoRoot $repoRoot -ReleaseVersion $releaseVersion -TagName $tagName
    }

    Invoke-CheckedCommand -FilePath "git" -Arguments @("tag", "-a", $tagName, "-m", "WheelVolume $tagName")
    Invoke-CheckedCommand -FilePath "git" -Arguments @("push", $Remote, $tagName)

    $notes = @(
        "WheelVolume $tagName",
        "",
        "See the merged release PR and milestone for included fixes."
    ) -join "`n"

    $releaseArgs = @("release", "create", $tagName, "--title", "WheelVolume $tagName", "--notes", $notes)
    if (-not $SkipBuild) {
        foreach ($file in $releaseFiles) {
            $releaseArgs += $file
        }
    }

    Invoke-CheckedCommand -FilePath "gh" -Arguments $releaseArgs

    Write-Host ""
    Write-Host "Release complete for $tagName"
    Write-Host "Suggested cleanup:"
    Write-Host "  git push $Remote --delete release/$releaseVersion"
    Write-Host "  git branch -d release/$releaseVersion"
    Write-Host "  close milestone $tagName / v$releaseVersion in GitHub if all issues are closed"
    return
}
