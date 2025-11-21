# 打包脚本 - 生成单文件非自包含应用程序

# 清理旧的发布文件
Write-Host "正在清理旧的发布文件..." -ForegroundColor Cyan
if (Test-Path -Path "bin\Release\net8.0-windows\win-x64\publish") {
    Remove-Item -Path "bin\Release\net8.0-windows\win-x64\publish" -Recurse -Force
}

# 执行发布命令
Write-Host "正在发布应用程序..." -ForegroundColor Cyan
dotnet publish -c Release

