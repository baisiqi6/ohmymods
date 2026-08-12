@echo off
REM 编译 MyMod（多文件版本，自动收集 Main.cs + Patch_*.cs，无需手改文件列表）
REM GAME_DIR 指向游戏 Managed 目录（E 盘绝对路径，ohmymods 与游戏目录分离）
REM 2026-08-12 切换到 GOG 2.1.0 (x86)：E:\Kingdom Two Crowns
REM 旧环境 (2.0.1 x64)：E:\Kingdom.Two.Crowns.Call.of.Olympus\Kingdom.Two.Crowns.Call.of.Olympus-P2P\KingdomTwoCrowns_Data\Managed
setlocal enabledelayedexpansion
set GAME_DIR=E:\Kingdom Two Crowns\KingdomTwoCrowns_Data\Managed
set UMM_DIR=%GAME_DIR%\UnityModManager
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe

REM 自动收集源文件（cmd FOR 支持通配，csc 不支持 glob）
set CS_FILES=
for %%F in (Main.cs Patch_*.cs) do set CS_FILES=!CS_FILES! %%F
echo 源文件:!CS_FILES!

%CSC% /target:library /out:MyMod.dll ^
    /reference:%GAME_DIR%\UnityEngine.dll ^
    /reference:%GAME_DIR%\UnityEngine.CoreModule.dll ^
    /reference:%GAME_DIR%\UnityEngine.Physics2DModule.dll ^
    /reference:%GAME_DIR%\UnityEngine.IMGUIModule.dll ^
    /reference:%GAME_DIR%\UnityEngine.PhysicsModule.dll ^
    /reference:%GAME_DIR%\Assembly-CSharp.dll ^
    /reference:%UMM_DIR%\UnityModManager.dll ^
    /reference:%UMM_DIR%\0Harmony-1.2.dll ^
    /reference:%GAME_DIR%\netstandard.dll ^
    !CS_FILES!

if %ERRORLEVEL% EQU 0 (
    echo 编译成功！MyMod.dll 已生成
    copy /y MyMod.dll "%GAME_DIR%\..\..\Mods\MyMod\MyMod.dll"
    echo 已部署到 Mods\MyMod\MyMod.dll
) else (
    echo 编译失败！
    pause
)
