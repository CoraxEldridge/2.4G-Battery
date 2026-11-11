param(
    [string]$RepoName = "2.4G-Battery",
    [string]$Tag = "v1.0.0",
    [switch]$Private,
    [switch]$CreateRepo,
    [switch]$ReplaceAssets,
    [switch]$SourceOnly
)

$ErrorActionPreference = 'Stop'

$token = $env:GITHUB_TOKEN
if (-not $token) {
    throw "缺少 GITHUB_TOKEN（请用 op run 注入）"
}

$api = "https://api.github.com"
$headers = @{
    Authorization = "Bearer $token"
    Accept        = "application/vnd.github+json"
    "User-Agent"  = "battery-widget-publisher"
}

# PS 5.1 的 Invoke-RestMethod 默认按 ASCII 编码请求体，中文会变成 "?"，这里统一用 UTF-8
function Send-Json {
    param([string]$Method, [string]$Uri, $Object)

    $json = $Object | ConvertTo-Json -Depth 5
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
    return Invoke-RestMethod -Method $Method -Uri $Uri -Headers $headers -Body $bytes `
        -ContentType "application/json; charset=utf-8"
}
# 1) 身份
$user = Invoke-RestMethod -Uri "$api/user" -Headers $headers -Method Get
$owner = $user.login
Write-Host "已认证：$owner"

# 2) 仓库（默认要求已存在）
$repo = $null
try {
    $repo = Invoke-RestMethod -Uri "$api/repos/$owner/$RepoName" -Headers $headers -Method Get
    Write-Host "仓库已存在：$($repo.html_url)"
}
catch {
    if (-not $CreateRepo) {
        throw "仓库 $owner/$RepoName 不存在，请先在网页上创建空仓库（或用 -CreateRepo）"
    }
    $body = @{
        name        = $RepoName
        description = "2.4G Battery — 查看 2.4G 无线键盘 / 触摸板电量（Windows 托盘小工具）"
        private     = [bool]$Private
        has_issues  = $true
    }
    $repo = Send-Json -Method Post -Uri "$api/user/repos" -Object $body
    Write-Host "仓库已创建：$($repo.html_url)"
}

# 同步仓库简介（修复历史乱码）
$repo.Description = "2.4G Battery — 查看 2.4G 无线键盘 / 触摸板电量（Windows 托盘小工具）"
$repo = Send-Json -Method Patch -Uri "$api/repos/$owner/$RepoName" -Object @{ description = $repo.Description }
Write-Host "仓库简介已更新"

# 3) 本地提交并推送
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
$prevPreference = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
try {
    if (-not (Test-Path ".git")) {
        git init -b main | Out-Null
        Write-Host "已初始化本地仓库"
    }

    git add -A | Out-Null
    $status = git status --porcelain
    if ($status) {
        git commit -m "2.4G Battery $Tag" | Out-Null
        Write-Host "已提交本地改动"
    }
    else {
        Write-Host "没有需要提交的改动"
    }

    $remoteUrl = "https://github.com/$owner/$RepoName.git"
    $remotes = git remote
    if ($remotes -contains "origin") {
        git remote set-url origin $remoteUrl | Out-Null
    }
    else {
        git remote add origin $remoteUrl | Out-Null
    }

    # token 只出现在本次命令参数里，不写入 .git/config
    $pushUrl = "https://x-access-token:$token@github.com/$owner/$RepoName.git"
    git push $pushUrl "main:main"
    if ($LASTEXITCODE -ne 0) {
        throw "git push 失败（退出码 $LASTEXITCODE）"
    }
    Write-Host "已推送到 $remoteUrl"
}
finally {
    $ErrorActionPreference = $prevPreference
    Pop-Location
}

if ($SourceOnly) {
    Write-Host "仅推送源码，跳过 Release 与资产"
    Write-Host "仓库：$($repo.html_url)"
    return
}

# 4) Release
$release = $null
$notes = @"
## 2.4G Battery $Tag

Windows 托盘小工具，读取 2.4G 无线设备的电量。

- 支持迈从（MCHOSE）0xFF04 电量特性、罗技 HID++ 2.0 接收器、通用标准 HID 电量字段
- 自动扫描设备，可勾选要显示的设备
- 托盘图标为 macOS 菜单栏风格，充电绿色 / 低电红色
- 界面内嵌苹方字体，无需安装
- 低电量系统通知、开机自启、5 秒 / 30 秒刷新间隔

### 安装

下载 ``2.4G-Battery-Setup-$($Tag.TrimStart('v')).exe`` 直接安装（自带 .NET 运行时，免管理员）。

### 便携版

下载 ``2.4G-Battery-$($Tag.TrimStart('v'))-portable.zip`` 解压运行，需要 .NET 10 桌面运行时。
"@

try {
    $release = Invoke-RestMethod -Uri "$api/repos/$owner/$RepoName/releases/tags/$Tag" -Headers $headers -Method Get
    # 同步说明文字（修复历史乱码）
    $release = Send-Json -Method Patch -Uri "$api/repos/$owner/$RepoName/releases/$($release.id)" -Object @{
        name = "2.4G Battery $Tag"
        body = $notes
    }
    Write-Host "Release 已存在：$($release.html_url)"
}
catch {
    $release = Send-Json -Method Post -Uri "$api/repos/$owner/$RepoName/releases" -Object @{
        tag_name   = $Tag
        name       = "2.4G Battery $Tag"
        body       = $notes
        draft      = $false
        prerelease = $false
    }
    Write-Host "Release 已创建：$($release.html_url)"
}

# 5) 资产
$version = $Tag.TrimStart('v')
$installerPath = Join-Path $root "dist\installer\2.4G-Battery-Setup-$version.exe"
$portableDir = Join-Path $root "dist\BatteryWidget"

function Send-Asset {
    param([string]$Path, [string]$Name)

    if (-not (Test-Path $Path)) {
        Write-Host "跳过（不存在）：$Name"
        return
    }

    $current = Invoke-RestMethod -Uri "$api/repos/$owner/$RepoName/releases/$($release.id)/assets" -Headers $headers -Method Get
    $hit = @($current | Where-Object { $_.name -eq $Name })
    if ($hit.Count -gt 0) {
        if (-not $ReplaceAssets) {
            Write-Host "已存在，跳过：$Name"
            return
        }
        foreach ($asset in $hit) {
            Invoke-RestMethod -Method Delete -Uri "$api/repos/$owner/$RepoName/releases/assets/$($asset.id)" -Headers $headers | Out-Null
            Write-Host "已删除旧资产：$Name"
        }
    }

    $uploadUrl = "https://uploads.github.com/repos/$owner/$RepoName/releases/$($release.id)/assets?name=$Name"
    Invoke-RestMethod -Method Post -Uri $uploadUrl -Headers $headers -ContentType "application/octet-stream" -InFile $Path | Out-Null
    Write-Host "已上传：$Name"
}

Send-Asset -Path $installerPath -Name "2.4G-Battery-Setup-$version.exe"

if (Test-Path $portableDir) {
    Get-Process BatteryWidget -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 600

    $staging = Join-Path $env:TEMP "2.4G-Battery-portable-staging"
    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
    New-Item -ItemType Directory -Path $staging -Force | Out-Null
    Copy-Item (Join-Path $portableDir "*") $staging -Recurse -Force

    $zipPath = Join-Path $env:TEMP "2.4G-Battery-$version-portable.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path (Join-Path $staging "*") -DestinationPath $zipPath -CompressionLevel Optimal
    Send-Asset -Path $zipPath -Name "2.4G-Battery-$version-portable.zip"
    Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "仓库：$($repo.html_url)"
Write-Host "发布：$($release.html_url)"
