using Microsoft.AspNetCore.Mvc;
using RSDSystem.Helpers;
using RSDSystem.Services;

namespace RSDSystem.Controllers
{
    [Route("api/payroll")]
    [ApiController]
    public class PayrollPredictionApiController : ControllerBase
    {
        private readonly PayrollPredictionService _predictions;

        public PayrollPredictionApiController(PayrollPredictionService predictions)
        {
            _predictions = predictions;
        }

        [HttpGet("health")]
        public IActionResult Health() =>
            Ok(new { success = true, service = "payroll-prediction" });

        /// <summary>
        /// Proposed prediction API: previous two payroll months in, next-month estimate and risk flags out.
        /// </summary>
        [HttpPost("predict")]
        public async Task<IActionResult> Predict([FromBody] PayrollForecastInput? body)
        {
            if (body == null)
                return BadRequest(new { success = false, message = "Send previousPayroll1, previousPayroll2, and allocatedBudget." });

            var result = await _predictions.ForecastAsync(
                body.PreviousPayroll1,
                body.PreviousPayroll2,
                body.AllocatedBudget,
                body.AnomalyPercent,
                HttpContext.RequestAborted);

            return Ok(new
            {
                success = true,
                predictedPayroll = result.PredictedPayroll,
                allocatedBudget = result.AllocatedBudget,
                budgetDifference = result.BudgetDifference,
                exceedsBudget = result.ExceedsBudget,
                unusualChange = result.UnusualChange,
                changePercent = result.ChangePercent,
                riskTitle = result.RiskTitle,
                riskDetail = result.RiskDetail
            });
        }
    }
}
