#!/usr/bin/env python3
"""Payroll prediction API proposed in the RSD capstone.

POST /predict
{
  "previous_payroll_1": 123000.00,
  "previous_payroll_2": 123000.00,
  "allocated_budget": 175000.00,
  "anomaly_percent": 25
}

Next-month payroll is a two-point linear trend of the previous two months.
Budget exceedance and unusual month-to-month swings use the rule-based checks
described in the thesis (not a trained ML model).
"""

from __future__ import annotations

import json
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import os


ANOMALY_PERCENT = float(os.environ.get("PREDICTION_ANOMALY_PERCENT", "25"))
HOST = os.environ.get("PREDICTION_HOST", "0.0.0.0")
PORT = int(os.environ.get("PREDICTION_PORT", "8001"))


def _num(payload: dict, *keys: str, default: float = 0.0) -> float:
    for key in keys:
        if key in payload and payload[key] is not None:
            return float(payload[key])
    return default


def forecast(payload: dict) -> dict:
    month1 = round(_num(payload, "previous_payroll_1", "previousPayroll1"), 2)
    month2 = round(_num(payload, "previous_payroll_2", "previousPayroll2"), 2)
    budget = round(_num(payload, "allocated_budget", "allocatedBudget"), 2)
    threshold = _num(payload, "anomaly_percent", "anomalyPercent", default=ANOMALY_PERCENT)
    if threshold <= 0:
        threshold = ANOMALY_PERCENT

    predicted = round(month2 + (month2 - month1), 2)
    if predicted < 0:
        predicted = 0.0

    difference = round(predicted - budget, 2)
    exceeds = predicted > budget

    if month1 == 0:
        change_percent = 0.0 if month2 == 0 else 100.0
    else:
        change_percent = round(abs(month2 - month1) / month1 * 100.0, 2)

    unusual = change_percent >= threshold

    risk_title = None
    risk_detail = None
    if exceeds:
        risk_title = "Budget Exceeding Risk"
        risk_detail = "The predicted amount for next month exceeds the allocated budget."
    elif unusual:
        risk_title = "Unusual Payroll Change"
        risk_detail = (
            "Payroll rose sharply between the last two months."
            if month2 > month1
            else "Payroll dropped sharply between the last two months."
        )

    return {
        "success": True,
        "predicted_payroll": predicted,
        "predictedPayroll": predicted,
        "allocated_budget": budget,
        "allocatedBudget": budget,
        "budget_difference": difference,
        "budgetDifference": difference,
        "exceeds_budget": exceeds,
        "exceedsBudget": exceeds,
        "unusual_change": unusual,
        "unusualChange": unusual,
        "change_percent": change_percent,
        "changePercent": change_percent,
        "risk_title": risk_title,
        "riskTitle": risk_title,
        "risk_detail": risk_detail,
        "riskDetail": risk_detail,
    }


class Handler(BaseHTTPRequestHandler):
    def _send(self, code: int, payload: dict) -> None:
        body = json.dumps(payload).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self) -> None:  # noqa: N802
        if self.path.rstrip("/") in ("", "/health"):
            self._send(200, {"success": True, "service": "payroll-prediction"})
            return
        self._send(404, {"success": False, "message": "Not found."})

    def do_POST(self) -> None:  # noqa: N802
        if self.path.rstrip("/") != "/predict":
            self._send(404, {"success": False, "message": "Not found."})
            return
        length = int(self.headers.get("Content-Length", "0"))
        raw = self.rfile.read(length) if length else b"{}"
        try:
            payload = json.loads(raw.decode("utf-8") or "{}")
            if not isinstance(payload, dict):
                raise ValueError("JSON object required.")
        except (ValueError, json.JSONDecodeError) as exc:
            self._send(400, {"success": False, "message": str(exc)})
            return
        self._send(200, forecast(payload))

    def log_message(self, fmt: str, *args) -> None:
        print("[prediction-api] " + (fmt % args))


if __name__ == "__main__":
    server = ThreadingHTTPServer((HOST, PORT), Handler)
    print(f"Payroll prediction API listening on http://{HOST}:{PORT}")
    server.serve_forever()
