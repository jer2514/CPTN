// Add inside the AccountController class (temporary, remove after debugging)
using Microsoft.Extensions.Hosting; // at top if not present

[HttpPost]
[Route("Account/DebugVerify")]
public async Task<IActionResult> DebugVerify([FromServices] IHostEnvironment env, string username, string password)
{
    if (!env.IsDevelopment())
        return Forbid(); // only allow in Development

    var u = await _db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Username == username);
    if (u == null)
        return Json(new { found = false });

    bool verify = false;
    string verifyError = null;
    try
    {
        verify = BCrypt.Net.BCrypt.Verify(password ?? string.Empty, u.PasswordHash ?? string.Empty);
    }
    catch (Exception ex)
    {
        verifyError = ex.Message;
    }

    return Json(new
    {
        found = true,
        dbUsername = u.Username,
        hash = u.PasswordHash,          // only for local dev debugging
        hashLength = (u.PasswordHash ?? string.Empty).Length,
        verify,
        verifyError
    });
}