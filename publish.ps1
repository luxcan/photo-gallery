# Publishes Photo Gallery as one self-contained executable and puts a shortcut
# on the Start menu, so it can be launched from anywhere.
#
# The app keeps its config beside the executable, so it is installed to a stable
# per-user location rather than left in the build output - a rebuild or a clean
# would otherwise take the remembered library with it.

$ErrorActionPreference = 'Stop'

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
