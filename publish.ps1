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

# The config that lives beside the executable is the one thing in the install
# folder worth keeping across a republish.
$keptConfig = Join-Path $install 'config.json'
$savedConfig = $null
if (Test-Path $keptConfig) {
    $savedConfig = Get-Content $keptConfig -Raw
}

if (Test-Path $install) {
    Get-ChildItem $install -Recurse -Force | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}

try {
    dotnet publish $project -c Release -o $install --nologo | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "publish failed with exit code $LASTEXITCODE" }
}
finally {
    # Put back in a finally, because by the time anything can go wrong the folder
    # has already been emptied. A publish that failed - the app still running and
    # holding its own exe is enough - took the remembered library with it, and the
    # next launch opened on the first-run screen with no way back to it.
    if ($null -ne $savedConfig) {
        New-Item -ItemType Directory -Force -Path $install | Out-Null
        Set-Content $keptConfig $savedConfig -NoNewline
        Write-Host "  kept the existing config.json"
    }
}

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
