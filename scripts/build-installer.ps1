param(
    [string]$InnoCompiler = "D:\Program Files\Inno Setup 7\ISCC.exe"
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\BatteryWidget\BatteryWidget.csproj'
$payload = Join-Path $root 'dist\installer\payload'
$iss = Join-Path $root 'installer\BatteryWidget.iss'

if (-not (Test-Path $InnoCompiler)) {
    throw "找不到 Inno Setup 编译器：$InnoCompiler（可用 -InnoCompiler 指定 ISCC.exe 路径）"
}

# 单文件自包含发布（安装后无需 .NET 运行时）
Get-Process BatteryWidget -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

if (Test-Path $payload) { Remove-Item $payload -Recurse -Force }
New-Item -ItemType Directory -Path $payload -Force | Out-Null

dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false `
    -o $payload --nologo

Write-Host "编译安装程序…"
& $InnoCompiler $iss

Get-ChildItem (Join-Path $root 'dist\installer') -Filter '*.exe' |
    Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }, LastWriteTime |
    Format-Table -AutoSize
