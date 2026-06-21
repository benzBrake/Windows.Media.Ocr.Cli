[CmdletBinding()]
param(
    [string]$Version,

    [ValidateSet("major", "minor", "patch")]
    [string]$Bump = "patch",

    [switch]$NoPush
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Invoke-Git {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    & git @Arguments
    if ($LASTEXITCODE -ne 0) {
        $command = "git " + ($Arguments -join " ")
        throw "Command failed: $command"
    }
}

function Invoke-GitOutput {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $output = & git @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        $command = "git " + ($Arguments -join " ")
        throw "Command failed: $command`n$output"
    }

    return $output
}

function Assert-SemVer {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    if ($Value -notmatch "^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)$") {
        throw "Version must use X.Y.Z format. Got: $Value"
    }
}

try {
    $insideWorkTree = Invoke-GitOutput -Arguments @("rev-parse", "--is-inside-work-tree")
    if ($insideWorkTree -ne "true") {
        throw "Current directory is not inside a Git work tree."
    }

    $status = Invoke-GitOutput -Arguments @("status", "--porcelain")
    if ($status) {
        throw "Working tree is not clean. Commit or stash changes before creating a release tag."
    }

    Invoke-Git -Arguments @("fetch", "--tags", "--quiet")

    if (-not $Version) {
        $latestTag = Invoke-GitOutput -Arguments @("tag", "--list", "v[0-9]*", "--sort=-v:refname") | Select-Object -First 1

        if ($latestTag) {
            if ($latestTag -notmatch "^v(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)$") {
                throw "Latest version tag is not vX.Y.Z: $latestTag"
            }

            $major = [int]$Matches.major
            $minor = [int]$Matches.minor
            $patch = [int]$Matches.patch

            switch ($Bump) {
                "major" {
                    $major += 1
                    $minor = 0
                    $patch = 0
                }
                "minor" {
                    $minor += 1
                    $patch = 0
                }
                "patch" {
                    $patch += 1
                }
            }

            $Version = "$major.$minor.$patch"
        }
        else {
            $Version = "0.1.0"
        }
    }

    $Version = $Version.Trim()
    if ($Version.StartsWith("v")) {
        $Version = $Version.Substring(1)
    }

    Assert-SemVer -Value $Version

    $tag = "v$Version"
    $existingTag = Invoke-GitOutput -Arguments @("tag", "--list", $tag)
    if ($existingTag) {
        throw "Tag already exists: $tag"
    }

    Write-Output "Creating release tag: $tag"
    Invoke-Git -Arguments @("tag", "-a", $tag, "-m", "Release $tag")

    if ($NoPush) {
        Write-Output "Created local tag only: $tag"
        Write-Output "Push it later with: git push origin $tag"
    }
    else {
        Invoke-Git -Arguments @("push", "origin", $tag)
        Write-Output "Pushed tag: $tag"
        Write-Output "GitHub Actions will create the release from this tag."
    }
}
catch {
    Write-Error $_
    exit 1
}
