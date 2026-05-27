
## Release with script

The release script has two modes:

```powershell
.\scripts\release.ps1 -Mode Prepare
.\scripts\release.ps1 -Mode Publish -Version x.x.x
```

The script requires GitHub CLI (`gh`) authenticated for this repository.

### 1. Prepare the release

Create and switch to a release branch named `release/x.x.x`, for example:

```powershell
git checkout main
git pull
git checkout -b release/1.0.1
```

Make sure `WheelVolume.csproj` and `README.md` already contain the release version.

Then run:

```powershell
.\scripts\release.ps1 -Mode Prepare
```

If the release fixes one or more GitHub issues, pass them with `-Fixes`:

```powershell
.\scripts\release.ps1 -Mode Prepare -Fixes 1
```

The `Prepare` step:

- reads `x.x.x` from the current branch name
- verifies the branch name is valid
- verifies the version is higher than `main`
- checks that `WheelVolume.csproj` and `README.md` already match the release version
- checks that the tag and GitHub release do not already exist
- runs tests and build, unless skipped
- pushes the release branch
- creates or reuses a pull request from `release/x.x.x` into `main`
- prints the pull request link

Review and merge the pull request manually in GitHub.

If `-Fixes 1` was used, the release pull request will include `Fixes #1`, so GitHub will close that issue when the pull request is merged into `main`.

### 2. Publish the release

After the release pull request has been merged into `main`, run:

```powershell
.\scripts\release.ps1 -Mode Publish -Version x.x.x
```

Example:

```powershell
.\scripts\release.ps1 -Mode Publish -Version 1.0.1
```

The `Publish` step:

- switches to `main`
- pulls the latest version of `main`
- verifies that `WheelVolume.csproj` and `README.md` still match the release version
- runs tests and build again, unless skipped
- publishes release artifacts
- creates `SHA256SUMS.txt`
- creates and pushes the Git tag `vx.x.x`
- creates the GitHub release
- uploads the generated release artifacts

The release tag is created only after the release branch has been merged into `main`.