namespace RSDSystem.Models
{
    /// <summary>Shown by Home/Error when something crashes.</summary>
    public class ErrorViewModel
    {
        public string? RequestId { get; set; }

        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
    }
}
