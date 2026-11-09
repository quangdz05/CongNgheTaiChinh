@echo off
cd /d "%~dp0.."
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "scripts\redeploy-share-fe.ps1"
pause
