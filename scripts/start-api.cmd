@echo off
cd /d "%~dp0..\Back-end\src\Services\FraudDetectionService\FraudDetectionService.Api"
set "ASPNETCORE_ENVIRONMENT=Development"
dotnet run --no-launch-profile --urls http://127.0.0.1:5080
pause
