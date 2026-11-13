# Publishes Photo Gallery as one self-contained executable and puts a shortcut
# on the Start menu, so it can be launched from anywhere.
#
# The app keeps its config beside the executable, so it is installed to a stable
# per-user location rather than left in the build output - a rebuild or a clean
# would otherwise take the remembered library with it.
#
# -Release also publishes the build as a GitHub release, which is what every
# installed copy offers to update itself from. The version is <Version> in
# Directory.Build.props: bump it and commit, push, then run this. It refuses to
# release uncommitted or unpushed work, or a version that already has a release.

param([switch] $Release)

$ErrorActionPreference = 'Stop'

if ($Release) {
    [xml] $props = Get-Content (Join-Path $PSScriptRoot 'Directory.Build.props')
    $version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
    $tag = "v$version"

    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'gh (GitHub CLI) is not installed.' }
    gh auth status 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'gh is not signed in. Run: gh auth login' }

    if (git -C $PSScriptRoot status --porcelain) { throw 'There are uncommitted changes. Commit them first.' }
    git -C $PSScriptRoot fetch origin main 2>&1 | Out-Null
    if ((git -C $PSScriptRoot rev-parse HEAD) -ne (git -C $PSScriptRoot rev-parse origin/main)) {
        throw 'This commit is not what GitHub main has. Push (or pull) first.'
    }

    gh release view $tag --repo luxcan/photo-gallery 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) {
        throw "Release $tag already exists. Bump <Version> in Directory.Build.props first."
    }
}

$project = Join-Path $PSScriptRoot 'src\PhotoGallery.App\PhotoGallery.App.csproj'
$install = Join-Path $env:LOCALAPPDATA 'Programs\PhotoGallery'
$startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
$shortcut = Join-Path $startMenu 'Photo Gallery.lnk'
$exe = Join-Path $install 'PhotoGallery.exe'

Write-Host "Publishing to $install ..."

# One upgrade step, and then never again. config.json used to live in the
# install folder, and the install folder is about to be emptied - so an older
# one is moved out to the per-user address this build reads. The application
# adopts a stray config by itself, but it cannot adopt a file the wipe has
# already deleted, and losing it costs the remembered library and the models
# folder.
$legacyConfig = Join-Path $install 'config.json'
$configHome = Join-Path $env:LOCALAPPDATA 'PhotoGallery'
$configFile = Join-Path $configHome 'config.json'
if ((Test-Path $legacyConfig) -and -not (Test-Path $configFile)) {
    New-Item -ItemType Directory -Force -Path $configHome | Out-Null
    Move-Item $legacyConfig $configFile
    Write-Host "  moved config.json to $configHome"
}

# Nothing else in here has to survive.
if (Test-Path $install) {
    Get-ChildItem $install -Recurse -Force | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}

dotnet publish $project -c Release -o $install --nologo | Out-Null
if ($LASTEXITCODE -ne 0) { throw "publish failed with exit code $LASTEXITCODE" }

# Everything the single-file build does not need at runtime. The .lib files come
# from ONNX Runtime and are import libraries for building against it in C++ - the
# native DLL itself is inside the executable.
Get-ChildItem $install -Include '*.pdb', '*.lib' -File -Recurse |
    Remove-Item -Force -ErrorAction SilentlyContinue

$wsh = New-Object -ComObject WScript.Shell
$link = $wsh.CreateShortcut($shortcut)
$link.TargetPath = $exe
$link.WorkingDirectory = $install
$link.Description = 'Photo Gallery'
$link.Save()

$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host ""
Write-Host "  exe      : $exe  ($size MB)"
Write-Host "  shortcut : $shortcut"
Write-Host ""
Write-Host "Search the Start menu for 'Photo Gallery'."

if ($Release) {
    # The digest travels beside the executable as well as in GitHub's own asset
    # metadata, so an installed copy can always prove what it downloads.
    $staging = Join-Path ([IO.Path]::GetTempPath()) "photogallery-release-$version"
    New-Item -ItemType Directory -Force -Path $staging | Out-Null
    $digest = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLowerInvariant()
    $digestFile = Join-Path $staging 'PhotoGallery.exe.sha256'
    Set-Content -Path $digestFile -Value "$digest  PhotoGallery.exe" -Encoding ascii -NoNewline

    gh release create $tag $exe $digestFile `
        --repo luxcan/photo-gallery `
        --target (git -C $PSScriptRoot rev-parse HEAD) `
        --title "Photo Gallery $version" `
        --generate-notes
    if ($LASTEXITCODE -ne 0) { throw "gh release create failed with exit code $LASTEXITCODE" }

    Remove-Item $staging -Recurse -Force
    Write-Host ""
    Write-Host "  released : $tag  (sha256 $digest)"
}
