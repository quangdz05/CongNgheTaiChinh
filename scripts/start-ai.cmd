@echo off
cd /d "%~dp0..\ai-service"
set "APP_ENV=development"
set "AI_SERVICE_KEY=local-ai-service-key"
set "OMP_NUM_THREADS=2"
if not exist "..\.venv\Scripts\python.exe" (
  echo ERROR: Missing Python environment. Create .venv and install ai-service\requirements.txt first.
  pause
  exit /b 1
)
if not exist "artifacts\model.joblib" (
  echo ERROR: Missing AI model. Run scripts\train-ai.cmd first.
  pause
  exit /b 1
)
..\.venv\Scripts\python.exe -m uvicorn app:app --host 127.0.0.1 --port 8001
pause
