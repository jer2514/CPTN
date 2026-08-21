@echo off
cd /d "%~dp0"
python -m pip install -r requirements.txt
set PREDICTION_API_KEY=change-me-prediction-key
python -m uvicorn app:app --host 127.0.0.1 --port 8000
