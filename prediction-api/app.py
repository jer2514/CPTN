"""FastAPI payroll prediction service for RSDSystem.

The ASP.NET app posts the last two generated payroll months and the allocated
budget. This service returns the next-month estimate and anomaly flags.
"""

from __future__ import annotations

import os

from fastapi import FastAPI, Header, HTTPException
from pydantic import BaseModel, ConfigDict

from model import DEFAULT_ANOMALY_PERCENT, forecast

API_KEY = (os.environ.get("PREDICTION_API_KEY") or "").strip()

app = FastAPI(
    title="RSD Payroll Prediction API",
    description="Python AI component: next-month payroll forecast and anomaly flags.",
    version="1.0.0",
)


class PredictRequest(BaseModel):
    model_config = ConfigDict(extra="ignore")

    previousPayroll1: float | None = None
    previousPayroll2: float | None = None
    allocatedBudget: float | None = None
    anomalyPercent: float | None = None
    previous_payroll_1: float | None = None
    previous_payroll_2: float | None = None
    allocated_budget: float | None = None
    anomaly_percent: float | None = None

    def month1(self) -> float:
        value = self.previous_payroll_1 if self.previous_payroll_1 is not None else self.previousPayroll1
        return float(value or 0)

    def month2(self) -> float:
        value = self.previous_payroll_2 if self.previous_payroll_2 is not None else self.previousPayroll2
        return float(value or 0)

    def budget(self) -> float:
        value = self.allocated_budget if self.allocated_budget is not None else self.allocatedBudget
        return float(value or 0)

    def threshold(self) -> float:
        value = self.anomaly_percent if self.anomaly_percent is not None else self.anomalyPercent
        return float(value) if value is not None else DEFAULT_ANOMALY_PERCENT


def _authorize(x_api_key: str | None) -> None:
    if not API_KEY:
        return
    if (x_api_key or "").strip() != API_KEY:
        raise HTTPException(status_code=401, detail="Invalid prediction API key.")


@app.get("/")
@app.get("/health")
def health() -> dict[str, object]:
    return {
        "success": True,
        "service": "payroll-prediction",
        "engine": "python",
        "model": "sklearn.linear_model.LinearRegression",
    }


@app.post("/predict")
def predict(body: PredictRequest, x_api_key: str | None = Header(default=None, alias="X-Api-Key")) -> dict[str, object]:
    _authorize(x_api_key)
    result = forecast(body.month1(), body.month2(), body.budget(), body.threshold())
    return {
        "success": True,
        **result,
        "predictedPayroll": result["predicted_payroll"],
        "allocatedBudget": result["allocated_budget"],
        "budgetDifference": result["budget_difference"],
        "exceedsBudget": result["exceeds_budget"],
        "unusualChange": result["unusual_change"],
        "changePercent": result["change_percent"],
        "riskTitle": result["risk_title"],
        "riskDetail": result["risk_detail"],
    }
