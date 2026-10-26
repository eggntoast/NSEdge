param(
    [Parameter(Mandatory)][string]$PublishPath,
    [Parameter(Mandatory)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
[xml]$project = Get-Content (Join-Path $root 'src/NSEdge.App/NSEdge.App.csproj') -Raw
$version = [string]$project.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+$') { throw "Invalid version: $version" }
$msixVersion = "$version.0.0"

# Each invocation stages only this publish output; a reused directory could retain
# assemblies removed since an earlier build. Keep previous layouts for inspection.
$layout = Join-Path $OutputDirectory ('layout-' + [Guid]::NewGuid().ToString('N'))
$assets = Join-Path $layout 'Assets'
New-Item -ItemType Directory -Force $layout, $assets | Out-Null
Copy-Item -Path (Join-Path $PublishPath '*') -Destination $layout -Recurse -Force
Get-ChildItem $layout -Filter '*.pdb' | Remove-Item
$exe = Join-Path $layout 'NSEdge.exe'
if (-not (Test-Path $exe)) { throw 'Published NSEdge.exe not found' }
if (-not (Test-Path (Join-Path $layout 'LICENSE'))) { throw 'Published license not found' }

# Partner Center supplies the production package identity.
$manifest = @'
<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
         xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
         xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
         IgnorableNamespaces="uap rescap">
  <Identity Name="NSEdge.Prototype" Publisher="CN=NSEdge Prototype"
            Version="__VERSION__" ProcessorArchitecture="x64" />
  <Properties>
    <DisplayName>NSEdge</DisplayName>
    <PublisherDisplayName>NS</PublisherDisplayName>
    <Logo>Assets\StoreLogo.png</Logo>
  </Properties>
  <Dependencies>
    <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.19041.0"
                        MaxVersionTested="10.0.26100.0" />
  </Dependencies>
  <Resources><Resource Language="en-us" /></Resources>
  <Applications>
    <Application Id="NSEdge" Executable="NSEdge.exe" EntryPoint="Windows.FullTrustApplication">
      <uap:VisualElements DisplayName="NSEdge" Description="DualSense Edge companion"
                          BackgroundColor="transparent"
                          Square150x150Logo="Assets\Logo150.png"
                          Square44x44Logo="Assets\Logo44.png" />
    </Application>
  </Applications>
  <Capabilities><rescap:Capability Name="runFullTrust" /></Capabilities>
</Package>
'@
[System.IO.File]::WriteAllText((Join-Path $layout 'AppxManifest.xml'),
    $manifest.Replace('__VERSION__', $msixVersion), [System.Text.UTF8Encoding]::new($false))

Add-Type -AssemblyName System.Drawing
$icon = [System.Drawing.Icon]::ExtractAssociatedIcon($exe)
if ($null -eq $icon) { throw 'Cannot read executable icon' }
try {
    $source = $icon.ToBitmap()
    try {
        foreach ($size in @(44, 50, 150)) {
            $bitmap = [System.Drawing.Bitmap]::new($size, $size)
            try {
                $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
                try {
                    $graphics.Clear([System.Drawing.Color]::Transparent)
                    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                    $graphics.DrawImage($source, 0, 0, $size, $size)
                }
                finally { $graphics.Dispose() }
                $filename = if ($size -eq 50) { 'StoreLogo.png' } else { "Logo$size.png" }
                $bitmap.Save((Join-Path $assets $filename), [System.Drawing.Imaging.ImageFormat]::Png)
            }
            finally { $bitmap.Dispose() }
        }
    }
    finally { $source.Dispose() }
}
finally { $icon.Dispose() }

$kitRoot = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)) 'Windows Kits\10\bin'
$makeAppx = Get-ChildItem $kitRoot -Recurse -Filter 'makeappx.exe' -File |
    Where-Object { $_.FullName -match '\\x64\\makeappx\.exe$' } |
    Sort-Object FullName -Descending | Select-Object -First 1
if (-not $makeAppx) { throw 'Windows SDK MakeAppx.exe not found' }

$package = Join-Path $OutputDirectory "NSEdge-v$version-unsigned-prototype.msix"
& $makeAppx.FullName pack /d $layout /p $package /o
if ($LASTEXITCODE -ne 0) { throw "MakeAppx pack failed: $LASTEXITCODE" }

$unpacked = Join-Path $OutputDirectory 'unpacked'
New-Item -ItemType Directory -Force $unpacked | Out-Null
& $makeAppx.FullName unpack /p $package /d $unpacked /o
if ($LASTEXITCODE -ne 0) { throw "MakeAppx unpack failed: $LASTEXITCODE" }
if (-not (Test-Path (Join-Path $unpacked 'NSEdge.exe'))) { throw 'Package missing NSEdge.exe' }
[xml]$check = Get-Content (Join-Path $unpacked 'AppxManifest.xml') -Raw
if ($check.Package.Identity.Version -ne $msixVersion) { throw 'Package version mismatch' }

Write-Host "Unsigned MSIX package validated: $package"
Write-Host 'Not installed, Store-certified, or associated with a Partner Center identity.'