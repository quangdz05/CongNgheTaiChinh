@echo off
cd /d "%~dp0.."
set "OMP_NUM_THREADS=2"
if not exist ".venv\Scripts\python.exe" (
  echo ERROR: Missing .venv\Scripts\python.exe. Create the Python 3.12 environment and install ai-service\requirements.txt first.
  pause
  exit /b 1
)
.venv\Scripts\python.exe ai-service\train.py --synthetic --samples 6000
if errorlevel 1 (
  echo ERROR: AI training failed. Check the error above; no new model was confirmed.
  pause
  exit /b 1
)
echo Restart the AI service to load the new model.
pause
