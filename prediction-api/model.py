"""Payroll prediction model used by the Python API.

Next-month payroll is a scikit-learn linear regression over the last two
generated months. Anomalies are rule-based: budget exceedance and a sharp
month-to-month change.
"""

from __future__ import annotations

from typing import Any

import numpy as np
from sklearn.linear_model import LinearRegression

DEFAULT_ANOMALY_PERCENT = 25.0


def _round_money(value: float) -> float:
    return round(float(value), 2)


def predict_next_month(previous_payroll_1: float, previous_payroll_2: float) -> float:
    """Fit a line through month 1 and month 2, then predict month 3."""
    month1 = _round_money(previous_payroll_1)
    month2 = _round_money(previous_payroll_2)
    model = LinearRegression()
    model.fit(np.array([[0.0], [1.0]]), np.array([month1, month2], dtype=float))
    predicted = float(model.predict(np.array([[2.0]]))[0])
    return _round_money(max(predicted, 0.0))


def change_percent(previous_payroll_1: float, previous_payroll_2: float) -> float:
    month1 = _round_money(previous_payroll_1)
    month2 = _round_money(previous_payroll_2)
    if month1 == 0:
        return 0.0 if month2 == 0 else 100.0
    return _round_money(abs(month2 - month1) / month1 * 100.0)


def forecast(
    previous_payroll_1: float,
    previous_payroll_2: float,
    allocated_budget: float,
    anomaly_percent: float | None = None,
) -> dict[str, Any]:
    threshold = float(anomaly_percent) if anomaly_percent and anomaly_percent > 0 else DEFAULT_ANOMALY_PERCENT
    predicted = predict_next_month(previous_payroll_1, previous_payroll_2)
    budget = _round_money(allocated_budget)
    difference = _round_money(predicted - budget)
    exceeds = predicted > budget
    percent = change_percent(previous_payroll_1, previous_payroll_2)
    unusual = percent >= threshold

    risk_title = None
    risk_detail = None
    if exceeds:
        risk_title = "Budget Exceeding Risk"
        risk_detail = "The predicted amount for next month exceeds the allocated budget."
    elif unusual:
        risk_title = "Unusual Payroll Change"
        risk_detail = (
            "Payroll rose sharply between the last two months."
            if previous_payroll_2 > previous_payroll_1
            else "Payroll dropped sharply between the last two months."
        )

    return {
        "predicted_payroll": predicted,
        "allocated_budget": budget,
        "budget_difference": difference,
        "exceeds_budget": exceeds,
        "unusual_change": unusual,
        "change_percent": percent,
        "risk_title": risk_title,
        "risk_detail": risk_detail,
        "engine": "python",
        "model": "sklearn.linear_model.LinearRegression",
    }
