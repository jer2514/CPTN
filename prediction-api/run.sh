#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
python3 -m pip install -r requirements.txt
export PREDICTION_API_KEY="${PREDICTION_API_KEY:-change-me-prediction-key}"
exec python3 -m uvicorn app:app --host 127.0.0.1 --port 8000
