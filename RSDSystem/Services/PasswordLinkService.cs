using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RSDSystem.Models;

namespace RSDSystem.Services
{
    public class PasswordLinkService
    {
        public static readonly TimeSpan InviteLifetime = TimeSpan.FromHours(48);
        public static readonly TimeSpan ResetLifetime = TimeSpan.FromHours(2);

        private readonly PayrollDbContext _db;

        public PasswordLinkService(PayrollDbContext db)
        {
            _db = db;
        }

        public string Issue(User user, TimeSpan lifetime)
        {
            var raw = CreateRawToken();
            user.PasswordResetTokenHash = HashToken(raw);
            user.PasswordResetExpiry = DateTime.UtcNow.Add(lifetime);
            return raw;
        }

        public async Task<User?> FindValidAsync(string? rawToken)
        {
            if (string.IsNullOrWhiteSpace(rawToken))
                return null;

            var hash = HashToken(rawToken.Trim());
            var now = DateTime.UtcNow;
            return await _db.Users.FirstOrDefaultAsync(u =>
                u.IsActive
                && u.PasswordResetTokenHash == hash
                && u.PasswordResetExpiry != null
                && u.PasswordResetExpiry > now);
        }

        public void Clear(User user)
        {
            user.PasswordResetTokenHash = null;
            user.PasswordResetExpiry = null;
        }

        private static string CreateRawToken()
        {
            var bytes = RandomNumberGenerator.GetBytes(32);
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static string HashToken(string rawToken)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
            return Convert.ToHexString(bytes);
        }
    }
}
