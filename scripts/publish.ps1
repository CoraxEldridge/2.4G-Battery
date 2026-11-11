param(
    [switch]$SelfContained
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\BatteryWidget\BatteryWidget.csproj'

if ($SelfContained) {
    $out = Join-Path $root 'dist\BatteryWidget-standalone'
    dotnet publish $project -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $out
}
else {
    $out = Join-Path $root 'dist\BatteryWidget'
    dotnet publish $project -c Release -r win-x64 --self-contained false -o $out
}

Write-Host "输出目录: $out"
Get-ChildItem $out -Filter 'BatteryWidget.exe' | Select-Object Name, Length, LastWriteTime
