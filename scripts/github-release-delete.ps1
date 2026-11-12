param(
    [string]$RepoName = "2.4G-Battery",
    [string]$Tag = "v1.0.0",
    [switch]$DeleteTag
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
    "User-Agent"  = "battery-widget-release-remover"
}

$user = Invoke-RestMethod -Uri "$api/user" -Headers $headers -Method Get
$owner = $user.login
Write-Host "已认证：$owner"

# 1) 找到 Release
$release = $null
try {
    $release = Invoke-RestMethod -Uri "$api/repos/$owner/$RepoName/releases/tags/$Tag" -Headers $headers -Method Get
}
catch {
    Write-Host "没有找到 tag 为 $Tag 的 Release（可能已删除）"
}

if ($release) {
    Write-Host "Release: $($release.name) ($($release.html_url))"
    $assets = Invoke-RestMethod -Uri "$api/repos/$owner/$RepoName/releases/$($release.id)/assets" -Headers $headers -Method Get
    foreach ($asset in @($assets)) {
        Write-Host "  删除资产：$($asset.name)"
    }

    Invoke-RestMethod -Method Delete -Uri "$api/repos/$owner/$RepoName/releases/$($release.id)" -Headers $headers | Out-Null
    Write-Host "Release 已删除"
}

# 2) 可选：删除 tag
if ($DeleteTag) {
    try {
        Invoke-RestMethod -Method Delete -Uri "$api/repos/$owner/$RepoName/git/refs/tags/$Tag" -Headers $headers | Out-Null
        Write-Host "tag $Tag 已删除"
    }
    catch {
        Write-Host "tag $Tag 删除失败或不存在：$($_.Exception.Message)"
    }
}

Write-Host ""
Write-Host "仓库：https://github.com/$owner/$RepoName/releases"
