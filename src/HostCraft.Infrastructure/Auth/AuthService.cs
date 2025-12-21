using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using HostCraft.Core.Entities;
using HostCraft.Core.Interfaces;
using HostCraft.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using OtpNet;

namespace HostCraft.Infrastructure.Auth;

/// <summary>
/// JWT-based authentication service with enhanced security features.
/// </summary>
public class AuthService : IAuthService
{
    private readonly HostCraftDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthService> _logger;

    private readonly string _jwtSecret;
    private readonly string _jwtIssuer;
    private readonly string _jwtAudience;
    private readonly int _tokenExpirationMinutes;
    private readonly int _refreshTokenExpirationDays;
    private readonly int _maxFailedAccessAttempts;
    private readonly TimeSpan _defaultLockoutTimeSpan;
    private readonly int _minPasswordLength;

    public AuthService(
        HostCraftDbContext context,
        IConfiguration configuration,
        ILogger<AuthService> logger)
    {
        _context = context;
        _configuration = configuration;
        _logger = logger;

        // Load JWT settings from configuration
        _jwtSecret = configuration["Jwt:Secret"] ?? GenerateDefaultSecret();
        _jwtIssuer = configuration["Jwt:Issuer"] ?? "HostCraft";
        _jwtAudience = configuration["Jwt:Audience"] ?? "HostCraft";
        _tokenExpirationMinutes = int.TryParse(configuration["Jwt:ExpirationMinutes"], out var exp) ? exp : 60;
        _refreshTokenExpirationDays = int.TryParse(configuration["Jwt:RefreshExpirationDays"], out var refExp) ? refExp : 7;
        _maxFailedAccessAttempts = int.TryParse(configuration["Security:MaxFailedAccessAttempts"], out var maxAttempts) ? maxAttempts : 5;
        _defaultLockoutTimeSpan = TimeSpan.FromMinutes(int.TryParse(configuration["Security:DefaultLockoutTimeSpanMinutes"], out var lockoutMinutes) ? lockoutMinutes : 15);
        _minPasswordLength = int.TryParse(configuration["Security:MinPasswordLength"], out var minLength) ? minLength : 8;
    }

    public async Task<AuthResult> LoginAsync(string email, string password, string? ipAddress = null, string? userAgent = null)
    {
        try
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null)
            {
                await LogAuditEventAsync("LoginFailed", $"Login attempt for non-existent user: {email}", null, ipAddress, userAgent, false);
                _logger.LogWarning("Login attempt for non-existent user: {Email}", email);
                return AuthResult.Failed("Invalid email or password");
            }

            // Check if account is locked out
            if (user.IsLockedOut && user.LockoutEnd > DateTime.UtcNow)
            {
                await LogAuditEventAsync("LoginFailed", $"Login attempt for locked out user: {email}", user.Id.ToString(), ipAddress, userAgent, false);
                _logger.LogWarning("Login attempt for locked out user: {Email}", email);
                return AuthResult.Failed("Account is locked out");
            }

            if (!VerifyPassword(password, user.PasswordHash))
            {
                // Increment failed access count
                user.AccessFailedCount++;
                if (user.AccessFailedCount >= _maxFailedAccessAttempts)
                {
                    user.IsLockedOut = true;
                    user.LockoutEnd = DateTime.UtcNow.Add(_defaultLockoutTimeSpan);
                    await LogAuditEventAsync("AccountLocked", $"Account locked due to too many failed attempts: {email}", user.Id.ToString(), ipAddress, userAgent, false);
                    _logger.LogWarning("Account locked due to too many failed attempts: {Email}", email);
                }
                await _context.SaveChangesAsync();

                await LogAuditEventAsync("LoginFailed", $"Invalid password for user: {email}", user.Id.ToString(), ipAddress, userAgent, false);
                _logger.LogWarning("Failed login attempt for user: {Email}", email);
                return AuthResult.Failed("Invalid email or password");
            }

            // Check if two-factor authentication is required
            if (user.TwoFactorEnabled)
            {
                // For now, return a special result indicating 2FA is required
                // In a full implementation, you'd return a token that allows 2FA verification
                await LogAuditEventAsync("LoginRequiresTwoFactor", $"Login requires 2FA for user: {email}", user.Id.ToString(), ipAddress, userAgent, true);
                return AuthResult.Failed("Two-factor authentication required");
            }

            // Reset failed access count on successful login
            user.AccessFailedCount = 0;
            user.IsLockedOut = false;
            user.LockoutEnd = null;
            user.LastLoginAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            var token = GenerateJwtToken(user);
            var refreshToken = await GenerateRefreshTokenAsync(user.Id, ipAddress);
            var expiresAt = DateTime.UtcNow.AddMinutes(_tokenExpirationMinutes);

            await LogAuditEventAsync("LoginSuccess", $"User logged in: {email}", user.Id.ToString(), ipAddress, userAgent, true);
            _logger.LogInformation("User logged in: {Email}", email);

            return AuthResult.Succeeded(token, refreshToken.Token, expiresAt, user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login for {Email}", email);
            await LogAuditEventAsync("LoginError", $"Error during login for {email}: {ex.Message}", null, ipAddress, userAgent, false);
            return AuthResult.Failed("An error occurred during login");
        }
    }

    public async Task<AuthResult> RegisterAsync(string email, string password, string? name = null, bool isAdmin = false)
    {
        try
        {
            // Check if user already exists
            if (await _context.Users.AnyAsync(u => u.Email == email))
            {
                await LogAuditEventAsync("RegistrationFailed", $"Email already registered: {email}", null, null, null, false);
                return AuthResult.Failed("Email already registered");
            }

            // Validate password strength
            if (password.Length < _minPasswordLength)
            {
                return AuthResult.Failed($"Password must be at least {_minPasswordLength} characters");
            }

            // Additional password validation
            if (!IsValidPassword(password))
            {
                return AuthResult.Failed("Password must contain at least one uppercase letter, one lowercase letter, and one number");
            }

            // Create new user
            var user = new User
            {
                Uuid = Guid.NewGuid(),
                Email = email,
                PasswordHash = HashPassword(password),
                Name = name,
                IsAdmin = isAdmin,
                CreatedAt = DateTime.UtcNow,
                SecurityStamp = GenerateSecurityStamp(),
                EmailConfirmed = false // Require email confirmation in production
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            await LogAuditEventAsync("RegistrationSuccess", $"New user registered: {email} (Admin: {user.IsAdmin})", user.Id.ToString(), null, null, true);
            _logger.LogInformation("New user registered: {Email} (Admin: {IsAdmin})", email, user.IsAdmin);

            // For initial setup, auto-login admin users
            if (user.IsAdmin)
            {
                var token = GenerateJwtToken(user);
                var refreshToken = await GenerateRefreshTokenAsync(user.Id, null);
                var expiresAt = DateTime.UtcNow.AddMinutes(_tokenExpirationMinutes);

                return AuthResult.Succeeded(token, refreshToken.Token, expiresAt, user);
            }

            return AuthResult.RegistrationSucceeded(user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during registration for {Email}", email);
            await LogAuditEventAsync("RegistrationError", $"Error during registration for {email}: {ex.Message}", null, null, null, false);
            return AuthResult.Failed("An error occurred during registration");
        }
    }

    public async Task<User?> ValidateTokenAsync(string token)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_jwtSecret);

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidIssuer = _jwtIssuer,
                ValidateAudience = true,
                ValidAudience = _jwtAudience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };

            var principal = tokenHandler.ValidateToken(token, validationParameters, out var validatedToken);
            var userIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
            {
                return null;
            }

            return await _context.Users.FindAsync(userId);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Token validation failed");
            return null;
        }
    }

    public async Task<bool> ChangePasswordAsync(int userId, string currentPassword, string newPassword)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
        {
            return false;
        }

        if (!VerifyPassword(currentPassword, user.PasswordHash))
        {
            return false;
        }

        if (newPassword.Length < 8)
        {
            return false;
        }

        user.PasswordHash = HashPassword(newPassword);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Password changed for user: {UserId}", userId);
        return true;
    }

    public async Task<User?> GetUserByIdAsync(int userId)
    {
        return await _context.Users.FindAsync(userId);
    }

    public async Task<User?> GetUserByEmailAsync(string email)
    {
        return await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
    }

    public async Task<bool> HasAnyUsersAsync()
    {
        try
        {
            return await _context.Users.AnyAsync();
        }
        catch (Exception ex)
        {
            // If we can't query the database (table doesn't exist, connection issue, etc.)
            // assume no users exist so setup can proceed
            _logger.LogWarning(ex, "Failed to check if users exist, assuming setup is required");
            return false;
        }
    }

    public async Task<bool> RevokeRefreshTokenAsync(string refreshToken, string? ipAddress = null)
    {
        var token = await _context.RefreshTokens.FirstOrDefaultAsync(rt => rt.Token == refreshToken);
        if (token == null) return false;

        token.RevokedAt = DateTime.UtcNow;
        token.RevokedByIp = ipAddress;
        await _context.SaveChangesAsync();

        await LogAuditEventAsync("RefreshTokenRevoked", "Refresh token revoked", token.UserId.ToString(), ipAddress, null, true);
        return true;
    }

    public async Task<bool> RevokeAllRefreshTokensAsync(int userId, string? ipAddress = null)
    {
        var tokens = await _context.RefreshTokens
            .Where(rt => rt.UserId == userId && rt.IsActive)
            .ToListAsync();

        foreach (var token in tokens)
        {
            token.RevokedAt = DateTime.UtcNow;
            token.RevokedByIp = ipAddress;
        }

        // Update security stamp to invalidate all JWT tokens
        var user = await _context.Users.FindAsync(userId);
        if (user != null)
        {
            user.SecurityStamp = GenerateSecurityStamp();
        }

        await _context.SaveChangesAsync();

        await LogAuditEventAsync("AllRefreshTokensRevoked", "All refresh tokens revoked for user", userId.ToString(), ipAddress, null, true);
        return true;
    }

    public async Task<bool> EnableTwoFactorAsync(int userId, string verificationCode)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null) return false;

        if (string.IsNullOrEmpty(user.TwoFactorSecret))
        {
            user.TwoFactorSecret = GenerateTwoFactorSecret();
        }

        var isValid = ValidateTwoFactorCode(user.TwoFactorSecret, verificationCode);
        if (!isValid) return false;

        user.TwoFactorEnabled = true;
        await _context.SaveChangesAsync();

        await LogAuditEventAsync("TwoFactorEnabled", "Two-factor authentication enabled", userId.ToString(), null, null, true);
        return true;
    }

    public async Task<bool> DisableTwoFactorAsync(int userId, string verificationCode)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null) return false;

        if (!string.IsNullOrEmpty(user.TwoFactorSecret))
        {
            var isValid = ValidateTwoFactorCode(user.TwoFactorSecret, verificationCode);
            if (!isValid) return false;
        }

        user.TwoFactorEnabled = false;
        user.TwoFactorSecret = null;
        user.RecoveryCodes = null;
        await _context.SaveChangesAsync();

        await LogAuditEventAsync("TwoFactorDisabled", "Two-factor authentication disabled", userId.ToString(), null, null, true);
        return true;
    }

    public async Task<TwoFactorSetupResult> GetTwoFactorSetupAsync(int userId)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
        {
            return TwoFactorSetupResult.Failed("User not found");
        }

        if (user.TwoFactorEnabled)
        {
            return TwoFactorSetupResult.Succeeded(true);
        }

        // Generate secret if not exists
        if (string.IsNullOrEmpty(user.TwoFactorSecret))
        {
            user.TwoFactorSecret = GenerateTwoFactorSecret();
            await _context.SaveChangesAsync();
        }

        var key = Base32Encoding.ToBytes(user.TwoFactorSecret);
        var totp = new Totp(key);
        var qrCodeUri = $"otpauth://totp/HostCraft:{user.Email}?secret={user.TwoFactorSecret}&issuer=HostCraft";

        return TwoFactorSetupResult.Succeeded(false, qrCodeUri, user.TwoFactorSecret);
    }

    public async Task<AuthResult> VerifyTwoFactorLoginAsync(string email, string code)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user == null)
        {
            return AuthResult.Failed("User not found");
        }

        if (!user.TwoFactorEnabled || string.IsNullOrEmpty(user.TwoFactorSecret))
        {
            return AuthResult.Failed("Two-factor authentication not enabled");
        }

        var isValid = ValidateTwoFactorCode(user.TwoFactorSecret, code);
        if (!isValid)
        {
            await LogAuditEventAsync("TwoFactorLoginFailed", $"Invalid 2FA code for: {email}", user.Id.ToString(), null, null, false);
            return AuthResult.Failed("Invalid verification code");
        }

        // Generate tokens
        var token = GenerateJwtToken(user);
        var refreshToken = await GenerateRefreshTokenAsync(user.Id, null);
        var expiresAt = DateTime.UtcNow.AddMinutes(_tokenExpirationMinutes);

        await LogAuditEventAsync("TwoFactorLoginSuccess", $"2FA login successful for: {email}", user.Id.ToString(), null, null, true);

        return AuthResult.Succeeded(token, refreshToken.Token, expiresAt, user);
    }

    public async Task<bool> ResetPasswordAsync(string email, string newPassword)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user == null) return false;

        if (!IsValidPassword(newPassword))
        {
            return false;
        }

        user.PasswordHash = HashPassword(newPassword);
        user.LastPasswordChangeAt = DateTime.UtcNow;
        user.SecurityStamp = GenerateSecurityStamp();

        // Revoke all refresh tokens
        await RevokeAllRefreshTokensAsync(user.Id, null);

        await _context.SaveChangesAsync();

        await LogAuditEventAsync("PasswordResetByAdmin", $"Password reset by admin for: {email}", user.Id.ToString(), null, null, true);
        return true;
    }

    public async Task<IEnumerable<AuditLog>> GetAuditLogsAsync(int page = 1, int pageSize = 50)
    {
        return await _context.AuditLogs
            .OrderByDescending(a => a.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<bool> VerifyTwoFactorCodeAsync(int userId, string code)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null || string.IsNullOrEmpty(user.TwoFactorSecret)) return false;

        var isValid = ValidateTwoFactorCode(user.TwoFactorSecret, code);
        if (isValid && !user.TwoFactorEnabled)
        {
            user.TwoFactorEnabled = true;
            await _context.SaveChangesAsync();
        }

        await LogAuditEventAsync("TwoFactorVerification", $"Two-factor code verification: {isValid}", userId.ToString(), null, null, isValid);
        return isValid;
    }

    public async Task<string[]> GenerateRecoveryCodesAsync(int userId)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null) return Array.Empty<string>();

        var codes = new string[10];
        for (int i = 0; i < codes.Length; i++)
        {
            codes[i] = GenerateRecoveryCode();
        }

        user.RecoveryCodes = string.Join(",", codes);
        await _context.SaveChangesAsync();

        await LogAuditEventAsync("RecoveryCodesGenerated", "Recovery codes generated", userId.ToString(), null, null, true);
        return codes;
    }

    public async Task<bool> RedeemRecoveryCodeAsync(int userId, string code)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null || string.IsNullOrEmpty(user.RecoveryCodes)) return false;

        var codes = user.RecoveryCodes.Split(',');
        var index = Array.IndexOf(codes, code.Trim());
        if (index == -1) return false;

        // Remove the used code
        var updatedCodes = codes.Where((c, i) => i != index).ToArray();
        user.RecoveryCodes = updatedCodes.Length > 0 ? string.Join(",", updatedCodes) : null;

        // Disable 2FA if no recovery codes left
        if (updatedCodes.Length == 0)
        {
            user.TwoFactorEnabled = false;
            user.TwoFactorSecret = null;
        }

        await _context.SaveChangesAsync();

        await LogAuditEventAsync("RecoveryCodeRedeemed", "Recovery code redeemed", userId.ToString(), null, null, true);
        return true;
    }

    public async Task<bool> ConfirmEmailAsync(int userId, string token)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null) return false;

        // In a real implementation, you'd validate the token properly
        // For now, just mark as confirmed
        user.EmailConfirmed = true;
        user.EmailConfirmationToken = null;
        user.EmailConfirmationTokenExpiresAt = null;
        await _context.SaveChangesAsync();

        await LogAuditEventAsync("EmailConfirmed", "Email address confirmed", userId.ToString(), null, null, true);
        return true;
    }

    public async Task<bool> InitiatePasswordResetAsync(string email)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user == null) return true; // Don't reveal if email exists

        user.PasswordResetToken = GeneratePasswordResetToken();
        user.PasswordResetTokenExpiresAt = DateTime.UtcNow.AddHours(1);
        await _context.SaveChangesAsync();

        // In a real implementation, send email here
        await LogAuditEventAsync("PasswordResetInitiated", $"Password reset initiated for: {email}", user.Id.ToString(), null, null, true);
        return true;
    }

    public async Task<bool> ResetPasswordAsync(string email, string token, string newPassword)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user == null) return false;

        if (user.PasswordResetToken != token ||
            user.PasswordResetTokenExpiresAt < DateTime.UtcNow)
        {
            await LogAuditEventAsync("PasswordResetFailed", $"Invalid or expired reset token for: {email}", user.Id.ToString(), null, null, false);
            return false;
        }

        if (!IsValidPassword(newPassword))
        {
            return false;
        }

        user.PasswordHash = HashPassword(newPassword);
        user.PasswordResetToken = null;
        user.PasswordResetTokenExpiresAt = null;
        user.LastPasswordChangeAt = DateTime.UtcNow;
        user.SecurityStamp = GenerateSecurityStamp();

        // Revoke all refresh tokens
        await RevokeAllRefreshTokensAsync(user.Id, null);

        await _context.SaveChangesAsync();

        await LogAuditEventAsync("PasswordResetSuccess", $"Password reset successful for: {email}", user.Id.ToString(), null, null, true);
        return true;
    }

    public async Task<bool> LockoutUserAsync(int userId, TimeSpan? duration = null)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null) return false;

        user.IsLockedOut = true;
        user.LockoutEnd = DateTime.UtcNow.Add(duration ?? _defaultLockoutTimeSpan);
        await _context.SaveChangesAsync();

        await LogAuditEventAsync("AccountLocked", "Account manually locked", userId.ToString(), null, null, true);
        return true;
    }

    public async Task<bool> UnlockUserAsync(int userId)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null) return false;

        user.IsLockedOut = false;
        user.LockoutEnd = null;
        user.AccessFailedCount = 0;
        await _context.SaveChangesAsync();

        await LogAuditEventAsync("AccountUnlocked", "Account manually unlocked", userId.ToString(), null, null, true);
        return true;
    }

    public async Task LogAuditEventAsync(string eventType, string description, string? userId = null, string? ipAddress = null, string? userAgent = null, bool isSuccess = true, string? additionalData = null)
    {
        var auditLog = new AuditLog
        {
            UserId = userId,
            Username = !string.IsNullOrEmpty(userId) ? (await _context.Users.FindAsync(int.Parse(userId)))?.Email : null,
            EventType = eventType,
            Description = description,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            IsSuccess = isSuccess,
            AdditionalData = additionalData,
            Timestamp = DateTime.UtcNow
        };

        _context.AuditLogs.Add(auditLog);
        await _context.SaveChangesAsync();
    }

    public async Task<AuthResult> RefreshTokenAsync(string refreshToken, string? ipAddress = null)
    {
        try
        {
            var storedToken = await _context.RefreshTokens
                .Include(rt => rt.User)
                .FirstOrDefaultAsync(rt => rt.Token == refreshToken);

            if (storedToken == null || !storedToken.IsActive)
            {
                await LogAuditEventAsync("RefreshTokenFailed", "Invalid or expired refresh token", null, ipAddress, null, false);
                return AuthResult.Failed("Invalid refresh token");
            }

            // Revoke the current refresh token
            storedToken.RevokedAt = DateTime.UtcNow;
            storedToken.RevokedByIp = ipAddress;

            // Generate new tokens
            var newToken = GenerateJwtToken(storedToken.User!);
            var newRefreshToken = await GenerateRefreshTokenAsync(storedToken.UserId, ipAddress);
            var expiresAt = DateTime.UtcNow.AddMinutes(_tokenExpirationMinutes);

            // Update security stamp to invalidate other tokens if needed
            storedToken.User!.SecurityStamp = GenerateSecurityStamp();

            await _context.SaveChangesAsync();

            await LogAuditEventAsync("RefreshTokenSuccess", $"Token refreshed for user: {storedToken.User.Email}", storedToken.UserId.ToString(), ipAddress, null, true);

            return AuthResult.Succeeded(newToken, newRefreshToken.Token, expiresAt, storedToken.User);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during token refresh");
            await LogAuditEventAsync("RefreshTokenError", $"Error during token refresh: {ex.Message}", null, ipAddress, null, false);
            return AuthResult.Failed("An error occurred during token refresh");
        }
    }

    private string GenerateJwtToken(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.Name ?? user.Email),
            new Claim("sub", user.Id.ToString()),
            new Claim("isAdmin", user.IsAdmin.ToString().ToLower()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _jwtIssuer,
            audience: _jwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_tokenExpirationMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private async Task<RefreshToken> GenerateRefreshTokenAsync(int userId, string? ipAddress)
    {
        var token = new RefreshToken
        {
            Token = GenerateRefreshTokenString(),
            UserId = userId,
            ExpiresAt = DateTime.UtcNow.AddDays(_refreshTokenExpirationDays),
            CreatedAt = DateTime.UtcNow,
            CreatedByIp = ipAddress
        };

        _context.RefreshTokens.Add(token);
        await _context.SaveChangesAsync();

        return token;
    }

    private static string GenerateRefreshTokenString()
    {
        var randomNumber = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomNumber);
        return Convert.ToBase64String(randomNumber);
    }

    private static string HashPassword(string password)
    {
        // Use BCrypt-style hashing with PBKDF2
        var salt = new byte[16];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(salt);
        }

        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            100000,
            HashAlgorithmName.SHA256,
            32);

        // Combine salt + hash
        var result = new byte[48];
        Array.Copy(salt, 0, result, 0, 16);
        Array.Copy(hash, 0, result, 16, 32);

        return Convert.ToBase64String(result);
    }

    private static bool VerifyPassword(string password, string storedHash)
    {
        try
        {
            var hashBytes = Convert.FromBase64String(storedHash);
            if (hashBytes.Length != 48)
            {
                return false;
            }

            var salt = new byte[16];
            Array.Copy(hashBytes, 0, salt, 0, 16);

            var storedPasswordHash = new byte[32];
            Array.Copy(hashBytes, 16, storedPasswordHash, 0, 32);

            var computedHash = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                100000,
                HashAlgorithmName.SHA256,
                32);

            return CryptographicOperations.FixedTimeEquals(computedHash, storedPasswordHash);
        }
        catch
        {
            return false;
        }
    }

    private static string GenerateDefaultSecret()
    {
        // Generate a secure random secret if not configured
        var bytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }

    private static bool IsValidPassword(string password)
    {
        // At least one uppercase, one lowercase, one digit
        return password.Length >= 8 &&
               password.Any(char.IsUpper) &&
               password.Any(char.IsLower) &&
               password.Any(char.IsDigit);
    }

    private static string GenerateSecurityStamp()
    {
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }

    private static string GenerateTwoFactorSecret()
    {
        var key = KeyGeneration.GenerateRandomKey(20);
        return Base32Encoding.ToString(key);
    }

    private static bool ValidateTwoFactorCode(string secret, string code)
    {
        var key = Base32Encoding.ToBytes(secret);
        var totp = new Totp(key);
        return totp.VerifyTotp(code, out _, new VerificationWindow(2, 2));
    }

    private static string GenerateRecoveryCode()
    {
        var bytes = new byte[4];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return BitConverter.ToString(bytes).Replace("-", "").ToUpper();
    }

    private static string GeneratePasswordResetToken()
    {
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }
}
