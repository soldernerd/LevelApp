# Work Package 0.09 — CI/CD Pipeline

> Target version: **v0.11.0**
> Prerequisite: WP0.10 complete (v0.10.0) ✓

---

## Goal

Set up a GitHub Actions CI/CD pipeline that automatically builds the solution
in Release mode, runs all unit tests, packages the output as a zip, and
publishes a versioned GitHub Release on every push to `master`.

This gives immediate value: broken builds and failing tests are caught
automatically on every commit, and a distributable artifact is produced
without any manual steps.

---

## New file: `.github/workflows/ci.yml`

```yaml
name: CI/CD

on:
  push:
    branches: [ master ]

jobs:
  build-test-release:
    runs-on: windows-latest

    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'

      - name: Restore dependencies
        run: dotnet restore LevelApp.slnx

      - name: Build (Release)
        run: dotnet build LevelApp.slnx --configuration Release --no-restore

      - name: Run unit tests
        run: dotnet test LevelApp.Tests/LevelApp.Tests.csproj --configuration Release --no-build --verbosity normal

      - name: Read version
        id: version
        shell: pwsh
        run: |
          $content = Get-Content LevelApp.Core/AppVersion.cs -Raw
          $major = [regex]::Match($content, 'Major\s*=\s*(\d+)').Groups[1].Value
          $minor = [regex]::Match($content, 'Minor\s*=\s*(\d+)').Groups[1].Value
          $patch = [regex]::Match($content, 'Patch\s*=\s*(\d+)').Groups[1].Value
          $version = "$major.$minor.$patch"
          echo "VERSION=$version" >> $env:GITHUB_OUTPUT
          echo "TAG=v$version" >> $env:GITHUB_OUTPUT

      - name: Package release zip
        shell: pwsh
        run: |
          $publishDir = "publish"
          dotnet publish LevelApp.App/LevelApp.App.csproj `
            --configuration Release `
            --output $publishDir `
            --no-build
          Compress-Archive -Path "$publishDir\*" -DestinationPath "LevelApp-${{ steps.version.outputs.VERSION }}.zip"

      - name: Create GitHub Release
        uses: softprops/action-gh-release@v2
        with:
          tag_name: ${{ steps.version.outputs.TAG }}
          name: LevelApp ${{ steps.version.outputs.TAG }}
          files: LevelApp-${{ steps.version.outputs.VERSION }}.zip
          generate_release_notes: true
        env:
          GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
```

### Notes on the pipeline

- **`GITHUB_TOKEN`** is automatically provided by GitHub Actions for public
  repos — no secrets to configure manually.
- **`softprops/action-gh-release`** creates the release and attaches the zip.
  If a release with the same tag already exists (e.g. from a re-push without
  a version bump) the step will fail visibly, which is the correct behaviour —
  it signals that `AppVersion.cs` was not incremented.
- **`generate_release_notes: true`** auto-populates the release description
  from commit messages since the previous tag.
- The zip is named `LevelApp-X.Y.Z.zip` and contains the flat publish output
  (all `.exe`, `.dll`, and asset files). No installer, no subfolder — matches
  the current manual deployment model exactly.

---

## Required: verify publish output

Before merging, confirm that `dotnet publish` on `LevelApp.App` produces a
self-contained flat folder with all required files. If the project currently
relies on framework-dependent deployment, the publish command may need
`--self-contained true --runtime win-x64` added. Claude Code should inspect
`LevelApp.App.csproj` and adjust the publish step accordingly.

---

## Changes to `CLAUDE.md`

Add a note under the versioning/commit section:

```
## CI/CD

A GitHub Actions pipeline runs on every push to master:
- Builds the solution in Release mode
- Runs all tests in LevelApp.Tests
- Packages the publish output as LevelApp-X.Y.Z.zip
- Creates a GitHub Release tagged vX.Y.Z

**Always increment AppVersion.cs before pushing to master.** Pushing
without a version bump will cause the release step to fail if a release
with that tag already exists.
```

---

## Acceptance criteria

1. `.github/workflows/ci.yml` exists and is valid YAML
2. A push to `master` triggers the workflow automatically
3. The workflow builds in Release mode without errors
4. All tests in `LevelApp.Tests` pass in the workflow
5. A `LevelApp-X.Y.Z.zip` artifact is attached to a new GitHub Release
   tagged `vX.Y.Z` matching `AppVersion.cs`
6. Pushing a second commit with the same version fails the release step
   visibly (correct behaviour — not a bug to fix)

---

## Version bump

Set `AppVersion.Minor` → `9`, `AppVersion.Patch` → `0` in `AppVersion.cs`
before committing. Commit message:

```
[v0.11.0] WP0.11: GitHub Actions CI/CD pipeline
```
