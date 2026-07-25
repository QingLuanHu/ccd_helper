@echo off
chcp 65001 >nul
echo ========================================
echo  外观检查辅助系统 - 打包脚本
echo ========================================
echo.

:: 设置发布参数
set PROJECT_NAME=ccd_helper
set OUTPUT_DIR=.\Publish
set RUNTIME=win-x64
set CONFIG=Release

:: 清理旧发布目录
if exist %OUTPUT_DIR% (
    echo 清理旧的发布目录...
    rmdir /s /q %OUTPUT_DIR%
)

:: 执行发布
echo 正在发布 %PROJECT_NAME% ...
dotnet publish %PROJECT_NAME%.csproj -c %CONFIG% -r %RUNTIME% --self-contained true -o %OUTPUT_DIR% -p:PublishSingleFile=true -p:DebugType=none -p:DebugSymbols=false

if %errorlevel% neq 0 (
    echo.
    echo 发布失败！请检查项目文件或网络连接。
    pause
    exit /b %errorlevel%
)

echo.
echo ========================================
echo  发布成功！
echo  输出目录: %OUTPUT_DIR%
echo  可执行文件: %OUTPUT_DIR%\%PROJECT_NAME%.exe
echo.
echo  请将 Data 文件夹复制到 %OUTPUT_DIR% 目录下，
echo  并确保与 %PROJECT_NAME%.exe 同级。
echo ========================================
pause