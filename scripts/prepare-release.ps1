param(
    [string]$PrereleaseLabel = "pre",

    [string]$MainBranch = "main",

    [switch]$DryRun,

    [switch]$SkipTests,

    [switch]$SkipBuild,

    [switch]$Push,

    [switch]$CreateGitHubRelease
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

function Get-SemVerParts {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    if ($Value -notmatch '^(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)(?:-(?<suffix>[0-9A-Za-z][0-9A-Za-z.-]*))?$') {
        throw "Invalid semantic version '$Value'. Use values like 1.2.3 or 1.2.3-pre.1."
    }

    [pscustomobject]@{
        Major = [int]$Matches.major
        Minor = [int]$Matches.minor
        Patch = [int]$Matches.patch
        Suffix = $Matches.suffix
    }
}

function Format-SemVer {
    param(
        [int]$Major,
        [int]$Minor,
        [int]$Patch,
        [string]$Suffix
    )

    $base = "$Major.$Minor.$Patch"
    if ([string]::IsNullOrWhiteSpace($Suffix)) {
        return $base
    }

    return "$base-$Suffix"
}

function Get-BumpedVersion {
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject]$Current,

        [Parameter(Mandatory = $true)]
        [string]$BumpKind
    )

    switch ($BumpKind) {
        "major" {
            return Format-SemVer -Major ($Current.Major + 1) -Minor 0 -Patch 0
        }
        "minor" {
            return Format-SemVer -Major $Current.Major -Minor ($Current.Minor + 1) -Patch 0
        }
        "patch" {
            return Format-SemVer -Major $Current.Major -Minor $Current.Minor -Patch ($Current.Patch + 1)
        }
        default {
            throw "Unsupported bump kind '$BumpKind'."
        }
    }
}

function Read-ReleaseVersion {
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject]$Current,

        [Parameter(Mandatory = $true)]
        [string]$CurrentVersionText,

        [Parameter(Mandatory = $true)]
        [string]$PrereleaseLabel
    )

    $majorVersion = Get-BumpedVersion -Current $Current -BumpKind "major"
    $minorVersion = Get-BumpedVersion -Current $Current -BumpKind "minor"
    $patchVersion = Get-BumpedVersion -Current $Current -BumpKind "patch"
    $basePatch = $Current.Patch
    if ([string]::IsNullOrWhiteSpace($Current.Suffix)) {
        $basePatch++
    }

    $prereleaseBaseVersion = Format-SemVer -Major $Current.Major -Minor $Current.Minor -Patch $basePatch
    $prereleaseVersion = "$prereleaseBaseVersion-$(Get-NextPrereleaseSuffix -BaseVersion $prereleaseBaseVersion -Label $PrereleaseLabel)"

    Write-Host "Current version: $CurrentVersionText"
    Write-Host ""
    Write-Host "Choose release version:"
    Write-Host "  1. Enter next version number"
    Write-Host "  2. Major ($majorVersion)"
    Write-Host "  3. Minor ($minorVersion)"
    Write-Host "  4. Patch ($patchVersion)"
    Write-Host "  5. Prerelease ($prereleaseVersion)"
    Write-Host ""

    while ($true) {
        $choice = Read-Host "Selection [1-5]"
        switch ($choice) {
            "1" {
                $customVersion = Read-Host "Next version number"
                [void](Get-SemVerParts $customVersion)
                return $customVersion
            }
            "2" {
                return $majorVersion
            }
            "3" {
                return $minorVersion
            }
            "4" {
                return $patchVersion
            }
            "5" {
                return $prereleaseVersion
            }
            default {
                Write-Host "Choose a number from 1 to 5."
            }
        }
    }
}

function Get-NextPrereleaseSuffix {
    param(
        [string]$BaseVersion,
        [string]$Label
    )

    $tags = git tag --list "v$BaseVersion-$Label.*"
    $latest = 0

    foreach ($tag in $tags) {
        if ($tag -match "^v$([regex]::Escape($BaseVersion))-$([regex]::Escape($Label))\.(?<number>\d+)$") {
            $latest = [Math]::Max($latest, [int]$Matches.number)
        }
    }

    return "$Label.$($latest + 1)"
}

function Set-XmlElementValue {
    param(
        [xml]$Xml,
        [string]$ElementName,
        [string]$Value
    )

    $node = $Xml.SelectSingleNode("/Project/PropertyGroup/$ElementName")
    if ($null -eq $node) {
        $node = $Xml.CreateElement($ElementName)
        $propertyGroup = $Xml.SelectSingleNode("/Project/PropertyGroup")
        [void]$propertyGroup.AppendChild($node)
    }

    $node.InnerText = $Value
}

function Update-ReadmeVersions {
    param(
        [string]$Path,
        [string]$NewVersion,
        [string]$NumericVersion
    )

    $content = Get-Content -LiteralPath $Path -Raw
    $content = [regex]::Replace($content, 'WheelVolume-v[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z][0-9A-Za-z.-]*)?-portable-win-x64\.zip', "WheelVolume-v$NewVersion-portable-win-x64.zip")
    $content = [regex]::Replace($content, 'WheelVolume-v[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z][0-9A-Za-z.-]*)?-win-x64\.zip', "WheelVolume-v$NewVersion-win-x64.zip")
    $content = [regex]::Replace($content, '/releases/download/v[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z][0-9A-Za-z.-]*)?/', "/releases/download/v$NewVersion/")
    $content = [regex]::Replace($content, '-p:Version=[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z][0-9A-Za-z.-]*)?', "-p:Version=$NewVersion")
    $content = [regex]::Replace($content, '-p:FileVersion=[0-9]+\.[0-9]+\.[0-9]+\.0', "-p:FileVersion=$NumericVersion.0")

    if (-not $DryRun) {
        Set-Content -LiteralPath $Path -Value $content -NoNewline
    }
}

function Get-NextReleaseBranchName {
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject]$ReleasedVersion
    )

    $nextVersion = Format-SemVer -Major $ReleasedVersion.Major -Minor $ReleasedVersion.Minor -Patch ($ReleasedVersion.Patch + 1)
    return "release/$nextVersion"
}

$scriptRoot = if ([string]::IsNullOrWhiteSpace($PSScriptRoot)) {
    Split-Path -Parent $MyInvocation.MyCommand.Path
} else {
    $PSScriptRoot
}

$repoRoot = (git -C $scriptRoot rev-parse --show-toplevel).Trim()
Set-Location $repoRoot

$currentBranch = (git branch --show-current).Trim()
$projectPath = Join-Path $repoRoot "WheelVolume\WheelVolume.csproj"
$readmePath = Join-Path $repoRoot "README.md"

[xml]$project = Get-Content -LiteralPath $projectPath -Raw
$currentVersionText = $project.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($currentVersionText) -or $currentVersionText -eq '$(VersionPrefix)') {
    $currentVersionText = $project.Project.PropertyGroup.VersionPrefix
}

$current = Get-SemVerParts $currentVersionText

$Version = Read-ReleaseVersion -Current $current -CurrentVersionText $currentVersionText -PrereleaseLabel $PrereleaseLabel

$next = Get-SemVerParts $Version
$numericVersion = Format-SemVer -Major $next.Major -Minor $next.Minor -Patch $next.Patch
$isPrerelease = -not [string]::IsNullOrWhiteSpace($next.Suffix)
$tagName = "v$Version"
$nextReleaseBranch = Get-NextReleaseBranchName -ReleasedVersion $next

if (-not $isPrerelease -and $currentBranch -ne $MainBranch) {
    throw "Stable releases must be prepared from '$MainBranch'. Current branch is '$currentBranch'. Choose prerelease for releases from another branch."
}

if ((git status --porcelain) -and -not $DryRun) {
    throw "Working tree is not clean. Commit or stash existing changes before preparing a release."
}

if (git rev-parse -q --verify "refs/tags/$tagName") {
    throw "Tag '$tagName' already exists."
}

if (git rev-parse -q --verify "refs/heads/$nextReleaseBranch") {
    throw "Branch '$nextReleaseBranch' already exists."
}

Write-Host "Preparing WheelVolume $Version from $currentVersionText on branch $currentBranch"

Set-XmlElementValue -Xml $project -ElementName "VersionPrefix" -Value $numericVersion
if ($isPrerelease) {
    Set-XmlElementValue -Xml $project -ElementName "Version" -Value $Version
} else {
    Set-XmlElementValue -Xml $project -ElementName "Version" -Value '$(VersionPrefix)'
}
Set-XmlElementValue -Xml $project -ElementName "AssemblyVersion" -Value "$numericVersion.0"
Set-XmlElementValue -Xml $project -ElementName "FileVersion" -Value "$numericVersion.0"
Set-XmlElementValue -Xml $project -ElementName "InformationalVersion" -Value '$(Version)'

if (-not $DryRun) {
    $project.Save($projectPath)
}

Update-ReadmeVersions -Path $readmePath -NewVersion $Version -NumericVersion $numericVersion

if (-not $SkipTests) {
    Invoke-CheckedCommand -FilePath "dotnet" -Arguments @("run", "--project", ".\WheelVolume.Tests\WheelVolume.Tests.csproj", "-c", "Release")
}

Invoke-CheckedCommand -FilePath "dotnet" -Arguments @("build", ".\WheelVolume.sln", "-c", "Release")

if (-not $SkipBuild) {
    $releaseRoot = Join-Path $repoRoot "release"
    $portableDir = Join-Path $releaseRoot "WheelVolume-portable-win-x64"
    $runtimeDir = Join-Path $releaseRoot "WheelVolume-win-x64"
    $portableZip = Join-Path $releaseRoot "WheelVolume-$tagName-portable-win-x64.zip"
    $runtimeZip = Join-Path $releaseRoot "WheelVolume-$tagName-win-x64.zip"

    if (-not $DryRun) {
        New-Item -ItemType Directory -Force -Path $releaseRoot | Out-Null
        Remove-Item -LiteralPath $portableDir, $runtimeDir, $portableZip, $runtimeZip -Recurse -Force -ErrorAction SilentlyContinue
    }

    Invoke-CheckedCommand -FilePath "dotnet" -Arguments @("publish", ".\WheelVolume\WheelVolume.csproj", "-c", "Release", "-r", "win-x64", "--self-contained", "true", "-p:Version=$Version", "-p:FileVersion=$numericVersion.0", "-p:PublishSingleFile=true", "-p:DebugType=None", "-p:DebugSymbols=false", "-o", $portableDir)
    Invoke-CheckedCommand -FilePath "dotnet" -Arguments @("publish", ".\WheelVolume\WheelVolume.csproj", "-c", "Release", "-r", "win-x64", "--self-contained", "false", "-p:Version=$Version", "-p:FileVersion=$numericVersion.0", "-p:PublishSingleFile=true", "-p:DebugType=None", "-p:DebugSymbols=false", "-o", $runtimeDir)

    if ($DryRun) {
        Write-Host ">Compress-Archive $portableDir -> $portableZip"
        Write-Host ">Compress-Archive $runtimeDir -> $runtimeZip"
    } else {
        Compress-Archive -Path (Join-Path $portableDir "*") -DestinationPath $portableZip -Force
        Compress-Archive -Path (Join-Path $runtimeDir "*") -DestinationPath $runtimeZip -Force
    }
}

Invoke-CheckedCommand -FilePath "git" -Arguments @("add", ".\WheelVolume\WheelVolume.csproj", ".\README.md")
Invoke-CheckedCommand -FilePath "git" -Arguments @("commit", "-m", "Prepare $tagName release")
Invoke-CheckedCommand -FilePath "git" -Arguments @("tag", "-a", $tagName, "-m", "WheelVolume $tagName")

if ($Push) {
    Invoke-CheckedCommand -FilePath "git" -Arguments @("push", "origin", $currentBranch)
    Invoke-CheckedCommand -FilePath "git" -Arguments @("push", "origin", $tagName)
}

if ($CreateGitHubRelease) {
    if (-not $Push) {
        throw "Use -Push with -CreateGitHubRelease so the tag exists on GitHub before creating the release."
    }

    $releaseArgs = @("release", "create", $tagName, "--title", "WheelVolume $tagName", "--notes", "Release $tagName")
    if ($isPrerelease) {
        $releaseArgs += "--prerelease"
    }

    if (-not $SkipBuild) {
        $releaseArgs += ".\release\WheelVolume-$tagName-portable-win-x64.zip"
        $releaseArgs += ".\release\WheelVolume-$tagName-win-x64.zip"
    }

    Invoke-CheckedCommand -FilePath "gh" -Arguments $releaseArgs
}

Invoke-CheckedCommand -FilePath "git" -Arguments @("switch", $MainBranch)
Invoke-CheckedCommand -FilePath "git" -Arguments @("switch", "-c", $nextReleaseBranch)

Write-Host "Release preparation complete for $tagName"
Write-Host "Created and switched to $nextReleaseBranch"
