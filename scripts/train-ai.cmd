@echo off
cd /d "%~dp0.."
set "OMP_NUM_THREADS=2"
.venv\Scripts\python.exe ai-service\train.py --synthetic --samples 6000
echo Restart the AI service to load the new model.
pause
