param([switch]$Test, [switch]$Native, [switch]$Gui, [switch]$Package)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Set-Location (Split-Path $PSScriptRoot -Parent)
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$root = (Get-Location).Path
$out = Join-Path $root 'artifacts'
New-Item -ItemType Directory -Force $out | Out-Null
$packageDir = Join-Path $root 'packages/net451-1.0.3'
$packageFile = Join-Path $root 'packages/net451-1.0.3.zip'
$packageHash = '770b33dfa64dc430c9d9ba4e1438b920dee905f7ea60791147d70689f68692d3'
New-Item -ItemType Directory -Force (Split-Path $packageFile) | Out-Null
if (!(Test-Path $packageFile)) {
    Invoke-WebRequest -UseBasicParsing 'https://api.nuget.org/v3-flatcontainer/microsoft.netframework.referenceassemblies.net451/1.0.3/microsoft.netframework.referenceassemblies.net451.1.0.3.nupkg' -OutFile $packageFile
}
if ((Get-FileHash $packageFile -Algorithm SHA256).Hash.ToLowerInvariant() -ne $packageHash) { throw 'Reference package SHA256 mismatch.' }
# Re-extract each build so modified cached reference files cannot change the API target.
if (Test-Path $packageDir) { Remove-Item -Recurse -Force $packageDir }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory($packageFile, $packageDir)
$references = Join-Path $packageDir 'build/.NETFramework/v4.5.1'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
if (!(Test-Path $compiler)) { throw 'The .NET Framework C# compiler was not found.' }
$source = @(Get-ChildItem 'src/Lean81/*.cs' | ForEach-Object { $_.FullName })
$common = @('/nologo', '/noconfig', '/nostdlib+', '/langversion:5', '/optimize+', '/warn:4', '/warnaserror+', '/platform:anycpu')
foreach ($assembly in @('mscorlib', 'System', 'System.Core', 'System.Drawing', 'System.Windows.Forms', 'System.ServiceProcess', 'System.Web.Extensions', 'System.Xml', 'Microsoft.CSharp')) {
    $common += '/reference:' + (Join-Path $references ($assembly + '.dll'))
}
# The icon is generated from vector geometry; no external asset or build tool is required.
Add-Type -AssemblyName System.Drawing
$iconBuilder = @'
using System; using System.Drawing; using System.IO; using System.Runtime.InteropServices;
public static class LeanIcon {
 [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
 public static void Write(string path) {
  using (var image = new Bitmap(64,64)) {
   using (var g=Graphics.FromImage(image)) {
    g.Clear(Color.FromArgb(27,43,59)); g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
    using(var p=new Pen(Color.FromArgb(51,207,173),7)) { p.StartCap=p.EndCap=System.Drawing.Drawing2D.LineCap.Round; g.DrawLines(p,new[]{new Point(17,16),new Point(17,47),new Point(46,47)}); }
    using(var p=new Pen(Color.White,4)) g.DrawLines(p,new[]{new Point(29,32),new Point(36,24),new Point(42,31),new Point(51,18)});
   }
   IntPtr handle=image.GetHicon(); try { using(var icon=Icon.FromHandle(handle)) using(var file=File.Create(path)) icon.Save(file); } finally { DestroyIcon(handle); }
  }
 }
}
'@
Add-Type -TypeDefinition $iconBuilder -ReferencedAssemblies System.Drawing
$icon = Join-Path $out 'Lean81.ico'
[LeanIcon]::Write($icon)
$exe = Join-Path $out 'Lean81.exe'
& $compiler @common '/target:winexe' ('/out:' + $exe) '/win32manifest:src/Lean81/app.manifest' ('/win32icon:' + $icon) @source
if ($LASTEXITCODE -ne 0) { throw 'App compilation failed.' }
$config = '<?xml version="1.0" encoding="utf-8"?><configuration><startup useLegacyV2RuntimeActivationPolicy="false"><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.5.1"/></startup></configuration>'
[IO.File]::WriteAllText((Join-Path $out 'Lean81.exe.config'), $config, (New-Object Text.UTF8Encoding($false)))
if ($Test -or $Native) {
    & $compiler @common '/target:exe' '/main:Tests' ('/out:' + (Join-Path $out 'Lean81.Tests.exe')) @source (Join-Path $root 'tests/Tests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    $testArgs = @(); if ($Native) { $testArgs += '--native' }
    & (Join-Path $out 'Lean81.Tests.exe') @testArgs | Tee-Object (Join-Path $out 'tests.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
if ($Gui) {
    $guiDir = Join-Path $out 'gui'
    New-Item -ItemType Directory -Force $guiDir | Out-Null
    $process = Start-Process -PassThru $exe -ArgumentList @('--gui-smoke', ('"' + $guiDir + '"'))
    if (!$process.WaitForExit(60000)) { $process.Kill(); throw 'GUI smoke timed out.' }
    if ($process.ExitCode -ne 0 -or !(Test-Path (Join-Path $guiDir 'gui-smoke.txt'))) { throw 'GUI smoke failed; inspect GUI artifacts.' }
}
if ($Package) {
    $version = '0.1.0'
    $portable = Join-Path $out ('Lean81-' + $version + '-portable')
    if (Test-Path $portable) { Remove-Item -Recurse -Force $portable }
    New-Item -ItemType Directory $portable | Out-Null
    Copy-Item $exe, (Join-Path $out 'Lean81.exe.config'), 'LICENSE', 'docs/QUICKSTART.txt', 'docs/RECOVERY.md', 'docs/COMPATIBILITY.md' $portable
    $zip = Join-Path $out ('Lean81-' + $version + '-portable.zip')
    if (Test-Path $zip) { Remove-Item $zip }
    [IO.Compression.ZipFile]::CreateFromDirectory($portable, $zip, [IO.Compression.CompressionLevel]::Optimal, $true)
    $sourceZip = Join-Path $out ('Lean81-' + $version + '-source.zip')
    & git archive --format=zip ('--prefix=Lean81-' + $version + '-source/') ('--output=' + $sourceZip) HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Source archive failed.' }
    $assets = @($exe, $zip, $sourceZip)
    $lines = @($assets | ForEach-Object { (Get-FileHash $_ -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($_) })
    [IO.File]::WriteAllLines((Join-Path $out 'SHA256SUMS.txt'), $lines, (New-Object Text.UTF8Encoding($false)))
    if ((Get-Item $exe).Length -gt 1MB) { throw 'Unexpected executable size; review before release.' }
    $metadata = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
    if ($metadata.FileVersion -ne ($version + '.0')) { throw 'Executable version mismatch.' }
    Write-Host ('Packaged Lean81 ' + $version + ', EXE ' + (Get-Item $exe).Length + ' bytes')
}
