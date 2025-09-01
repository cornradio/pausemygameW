# 打包脚本 - 生成单文件非自包含应用程序

# 清理旧的发布文件
Write-Host "正在清理旧的发布文件..." -ForegroundColor Cyan
if (Test-Path -Path "bin\Release\net8.0-windows\win-x64\publish") {
    Remove-Item -Path "bin\Release\net8.0-windows\win-x64\publish" -Recurse -Force
}

# 执行发布命令
Write-Host "正在发布应用程序..." -ForegroundColor Cyan
dotnet publish -c Release

# 检查发布是否成功
if ($LASTEXITCODE -eq 0) {
    $exePath = "bin\Release\net8.0-windows\win-x64\publish\WpfApp1.exe"
    if (Test-Path -Path $exePath) {
        $fileSize = (Get-Item -Path $exePath).Length
        $fileSizeKB = [math]::Round($fileSize / 1KB, 2)
        
        Write-Host "发布成功!" -ForegroundColor Green
        Write-Host "文件位置: $exePath" -ForegroundColor Yellow
        Write-Host "文件大小: $fileSizeKB KB" -ForegroundColor Yellow
        
        # 打开发布目录
        Write-Host "是否打开发布目录? (Y/N)" -ForegroundColor Cyan
        $response = Read-Host
        if ($response -eq "Y" -or $response -eq "y") {
            explorer "bin\Release\net8.0-windows\win-x64\publish"
        }
    } else {
        Write-Host "发布似乎成功，但找不到EXE文件。" -ForegroundColor Red
    }
} else {
    Write-Host "发布失败，请检查错误信息。" -ForegroundColor Red
}