$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root 'src\SmbConnectionViewer.cs'
$manifest = Join-Path $root 'src\SmbConnectionViewer.exe.manifest'
$dist = Join-Path $root 'dist'
$out = Join-Path $dist 'SmbConnectionViewer.exe'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}

if (-not (Test-Path -LiteralPath $compiler)) {
    throw '找不到 .NET Framework C# 编译器 csc.exe。'
}

New-Item -ItemType Directory -Force -Path $dist | Out-Null

& $compiler /nologo /codepage:65001 /target:winexe /platform:anycpu /optimize+ `
    /out:$out `
    /win32manifest:$manifest `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    /reference:System.Web.Extensions.dll `
    $src

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "构建完成：$out"
