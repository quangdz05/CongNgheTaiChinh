@echo off
cd /d "%~dp0..\ai-service"
set "APP_ENV=development"
set "AI_SERVICE_KEY=local-ai-service-key"
set "OMP_NUM_THREADS=2"
..\.venv\Scripts\python.exe -m uvicorn app:app --host 127.0.0.1 --port 8001
pause
