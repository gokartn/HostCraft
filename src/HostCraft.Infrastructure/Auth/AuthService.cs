using System.IdentityModel.Tokens.Jwt;
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

namespace HostCraft.Infrastructure.Auth;

/// <summary>
/// JWT-based authentication service.
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
    }

    public async Task<AuthResult> LoginAsync(string email, string password)
    {
        try
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null)
            {
                _logger.LogWarning("Login attempt for non-existent user: {Email}", email);
                return AuthResult.Failed("Invalid email or password");
            }

            if (!VerifyPassword(password, user.PasswordHash))
            {
                _logger.LogWarning("Failed login attempt for user: {Email}", email);
                return AuthResult.Failed("Invalid email or password");
            }

            // Update last login
            user.LastLoginAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            var token = GenerateJwtToken(user);
            var refreshToken = GenerateRefreshToken();
            var expiresAt = DateTime.UtcNow.AddMinutes(_tokenExpirationMinutes);

            _logger.LogInformation("User logged in: {Email}", email);

            return AuthResult.Succeeded(token, refreshToken, expiresAt, user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login for {Email}", email);
            return AuthResult.Failed("An error occurred during login");
        }
    }

    public async Task<AuthResult> RegisterAsync(string email, string password, string? name = null)
    {
        try
        {
            // Check if user already exists
            if (await _context.Users.AnyAsync(u => u.Email == email))
            {
                return AuthResult.Failed("Email already registered");
            }

            // Check password strength
            if (password.Length < 8)
            {
                return AuthResult.Failed("Password must be at least 8 characters");
            }

            // Create new user
            var user = new User
            {
                Uuid = Guid.NewGuid(),
                Email = email,
                PasswordHash = HashPassword(password),
                Name = name,
                IsAdmin = !await _context.Users.AnyAsync(), // First user is admin
                CreatedAt = DateTime.UtcNow
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            var token = GenerateJwtToken(user);
            var refreshToken = GenerateRefreshToken();
            var expiresAt = DateTime.UtcNow.AddMinutes(_tokenExpirationMinutes);

            _logger.LogInformation("New user registered: {Email} (Admin: {IsAdmin})", email, user.IsAdmin);

            return AuthResult.Succeeded(token, refreshToken, expiresAt, user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during registration for {Email}", email);
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
        return await _context.Users.AnyAsync();
    }

    public async Task<AuthResult> RefreshTokenAsync(string refreshToken)
    {
        // For simplicity, we're not storing refresh tokens in DB
        // In production, you'd want to validate against stored refresh tokens
        // For now, this is a placeholder that requires re-login
        await Task.CompletedTask;
        return AuthResult.Failed("Refresh token expired. Please login again.");
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

    private static string GenerateRefreshToken()
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
}
