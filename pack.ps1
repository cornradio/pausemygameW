# 打包脚本 - 生成单文件非自包含应用程序

# 清理旧的发布文件
Write-Host "cleaning..." -ForegroundColor Cyan
if (Test-Path -Path "bin\Release\net8.0-windows\win-x64\publish") {
    Remove-Item -Path "bin\Release\net8.0-windows\win-x64\publish" -Recurse -Force
}

# 执行发布命令
Write-Host "publishing..." -ForegroundColor Cyan
dotnet publish -c Release

# 复制exe 到 out 目录 ,并重命名PMG.exe
if (!(Test-Path -Path "out")) {
    New-Item -ItemType Directory -Path "out" | Out-Null
}
$sourcePath = "bin\Release\net8.0-windows\win-x64\publish\WpfApp1.exe"
$destinationPath =  "out\PMG.exe"
Write-Host "copy to out..." -ForegroundColor Cyan
Copy-Item -Path $sourcePath -Destination $destinationPath -Force
Write-Host "PMG.exe : $destinationPath" -ForegroundColor Green

