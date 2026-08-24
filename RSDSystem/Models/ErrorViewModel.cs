namespace RSDSystem.Models
{
    /// <summary>Shown by Home/Error when something crashes.</summary>
    public class ErrorViewModel
    {
        /// <summary>ASP.NET request id from Activity/HttpContext; shown only when present.</summary>
        public string? RequestId { get; set; }

        /// <summary>True when RequestId is set so the Error view can print the tracing id.</summary>
        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
    }
}
