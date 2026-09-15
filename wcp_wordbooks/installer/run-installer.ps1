param(
    [string]$HostLabel = 'Windows PowerShell'
)

$ErrorActionPreference = 'Stop'

$version = $PSVersionTable.PSVersion.ToString()
Write-Host "运行环境：$HostLabel $version" -ForegroundColor DarkCyan
$installScript = Join-Path $PSScriptRoot 'Install-WCP-Japanese.ps1'
$success = $false

try {
    & $installScript
    $success = $true
} catch {
    Write-Host ''
    # GetAwaiter().GetResult() 这类调用会把真实原因包在 AggregateException
    # 的内层异常里，只显示最外层只会得到「发生一个或多个错误」。
    $ex = $_.Exception
    $depth = 0
    while ($ex -and $ex.InnerException -and $depth -lt 6) {
        Write-Host $ex.Message -ForegroundColor Red
        $ex = $ex.InnerException
        $depth++
    }
    if ($ex) { Write-Host $ex.Message -ForegroundColor Red }
    Write-Host '安装没有完成。请保留此窗口中的错误信息，便于排查。' -ForegroundColor Yellow
    $logPath = Join-Path $env:USERPROFILE 'AppData\LocalLow\WCP\wcp\installer-error.log'
    if (Test-Path -LiteralPath $logPath) {
        Write-Host "更多详情已记录在：$logPath（排查时请把此文件发给作者）" -ForegroundColor DarkYellow
    }
}

Write-Host ''
if ($success) {
    Write-Host '安装成功。请重新启动万词破。' -ForegroundColor Green
} else {
    Write-Host '安装失败。' -ForegroundColor Red
}
Write-Host '窗口将保持打开，请点击右上角 X 关闭；按 Enter 不会关闭窗口。' -ForegroundColor Yellow
