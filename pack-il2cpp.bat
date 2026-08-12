@echo off
REM Kingdom Enhanced Mod — IL2CPP 发布打包脚本
REM 产出：release\KingdomEnhancedMod_v2.4.0_IL2CPP.zip
REM 前提：已用 dotnet8 build -c Debug 编译并部署到 E:\QQ 开发环境

setlocal
set SRC=E:\QQ\QQ下载文件\Kingdom Two Crowns (1)\Kingdom Two Crowns
set OUT=%~dp0release
set STAGE=%OUT%\KingdomEnhancedMod_v2.4.0_IL2CPP

if exist "%STAGE%" rmdir /s /q "%STAGE%"
mkdir "%STAGE%\BepInEx\plugins\KingdomEnhancedMod"
mkdir "%STAGE%\BepInEx\config"

REM 加载器（doorstop）
copy "%SRC%\winhttp.dll" "%STAGE%\" >nul
copy "%SRC%\doorstop_config.ini" "%STAGE%\" >nul
copy "%SRC%\.doorstop_version" "%STAGE%\" >nul

REM BepInEx 核心（不含 interop——首次启动自动生成；不含日志/缓存）
xcopy "%SRC%\BepInEx\core" "%STAGE%\BepInEx\core\" /e /i /q >nul
xcopy "%SRC%\BepInEx\unity-libs" "%STAGE%\BepInEx\unity-libs\" /e /i /q >nul

REM 插件本体
copy "%SRC%\BepInEx\plugins\KingdomEnhancedMod\KingdomEnhancedMod.dll" "%STAGE%\BepInEx\plugins\KingdomEnhancedMod\" >nul

REM 配置（默认值副本）
copy "%SRC%\BepInEx\config\KingdomEnhancedMod.cfg" "%STAGE%\BepInEx\config\" >nul

REM 安装说明
copy "%~dp0release-notes-il2cpp.md" "%STAGE%\安装说明.md" >nul

REM 打包
powershell -Command "Compress-Archive -Path '%STAGE%' -DestinationPath '%OUT%\KingdomEnhancedMod_v2.4.0_IL2CPP.zip' -Force"
rmdir /s /q "%STAGE%"

echo 打包完成: %OUT%\KingdomEnhancedMod_v2.4.0_IL2CPP.zip
endlocal
