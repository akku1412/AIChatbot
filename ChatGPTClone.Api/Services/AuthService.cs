using System.Globalization;
using ChatGPTClone.Api.Authentication;
using ChatGPTClone.Api.Configuration;
using ChatGPTClone.Api.DTOs.Auth;
using ChatGPTClone.Api.DTOs.Users;
using ChatGPTClone.Api.Interfaces;
using ChatGPTClone.Api.Middleware;
using ChatGPTClone.Api.Models;
using ChatGPTClone.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ChatGPTClone.Api.Services;

/// <summary>
/// Two ways to sign in, one account per email address:
/// - Email OTP: a code is emailed; verifying it proves the user controls the mailbox. The account is
///   created only after a successful verification.
/// - Google: the ID token is validated server-side (signature, audience, expiry, verified email); no OTP.
/// Signing in with Google using the email of an existing email-OTP account links Google to that account.
/// </summary>
public sealed class AuthService(
    IGoogleTokenValidator googleTokenValidator,
    IUserRepository userRepository,
    IOtpService otpService,
    ITokenService tokenService,
    IRefreshTokenService refreshTokenService,
    IUserAccessCache accessCache,
    IOptions<AdminOptions> adminOptions,
    ILogger<AuthService> logger) : IAuthService
{
    // ---------------- Email OTP ----------------

    public Task<OtpSentResponse> SendOtpAsync(string email, CancellationToken cancellationToken) =>
        IssueOtpAsync(NormalizeEmail(email), explicitResend: false, cancellationToken);

    public Task<OtpSentResponse> ResendOtpAsync(string email, CancellationToken cancellationToken) =>
        IssueOtpAsync(NormalizeEmail(email), explicitResend: true, cancellationToken);

    public async Task<AuthSession> VerifyOtpAsync(string email, string code, CancellationToken cancellationToken)
    {
        var normalized = NormalizeEmail(email);
        await otpService.VerifyAsync(normalized, code, cancellationToken);

        // The mailbox is proven: sign in to the existing account, or register a new one.
        var user = await userRepository.GetByEmailAsync(normalized, cancellationToken);
        if (user is null)
        {
            user = new User { Email = normalized, FullName = NameFromEmail(normalized), IsEmailVerified = true };
            userRepository.Add(user);
            if (!await TrySaveAsync(cancellationToken))
            {
                // A parallel sign-in for the same email created the account first: use that one.
                user = await userRepository.GetByEmailAsync(normalized, cancellationToken) ?? throw RetryLater();
            }
            else
            {
                logger.LogInformation("Registered user {UserId} via email OTP", user.UserId);
            }
        }
        else if (!user.IsEmailVerified)
        {
            user.IsEmailVerified = true;
            await userRepository.SaveChangesAsync(cancellationToken);
        }

        return await StartSessionAsync(user, "email OTP", cancellationToken);
    }

    // ---------------- Google ----------------

    public async Task<AuthSession> SignInWithGoogleAsync(string idToken, CancellationToken cancellationToken)
    {
        // Never trust an email or Google id sent by the client: only what the validated token says.
        var identity = await googleTokenValidator.ValidateAsync(idToken)
            ?? throw new ApiException(StatusCodes.Status401Unauthorized, "Google sign-in failed",
                "We could not verify your Google sign-in. Please try again.");

        var user = await FindOrCreateGoogleUserAsync(identity, cancellationToken);
        return await StartSessionAsync(user, "Google", cancellationToken);
    }

    // ---------------- Session ----------------

    public async Task<AuthSession> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var rotated = await refreshTokenService.RotateAsync(refreshToken, cancellationToken)
            ?? throw new ApiException(StatusCodes.Status401Unauthorized, "Session expired",
                "Your session has expired. Please sign in again.");

        // Defence in depth: a session only ever exists for a proven email address.
        if (!rotated.User.IsEmailVerified)
        {
            await refreshTokenService.RevokeAsync(rotated.Next.Token, cancellationToken);
            throw new ApiException(StatusCodes.Status401Unauthorized, "Session expired", "Please sign in again.");
        }
        if (rotated.User.IsBlocked)
        {
            await refreshTokenService.RevokeAsync(rotated.Next.Token, cancellationToken);
            throw Blocked();
        }

        return CreateSession(rotated.User, rotated.Next);
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            await refreshTokenService.RevokeAsync(refreshToken, cancellationToken);
        }
    }

    // ---------------- Helpers ----------------

    private async Task<OtpSentResponse> IssueOtpAsync(string email, bool explicitResend, CancellationToken cancellationToken)
    {
        // The name only personalises the email; the response is identical whether or not an account exists.
        var existing = await userRepository.GetByEmailAsync(email, cancellationToken);
        var issue = await otpService.IssueAsync(email, existing?.FullName, explicitResend, cancellationToken);

        return new OtpSentResponse(email, issue.CodeSent, issue.ExpiresInSeconds, issue.ResendAvailableInSeconds,
            issue.CodeSent
                ? $"We sent a 6-digit code to {email}. It expires in {issue.ExpiresInSeconds / 60} minutes."
                : $"A code was already sent to {email}. Check your inbox and spam folder.");
    }

    private async Task<User> FindOrCreateGoogleUserAsync(GoogleIdentity identity, CancellationToken cancellationToken)
    {
        // 1. Returning Google user.
        var user = await userRepository.GetByGoogleIdAsync(identity.GoogleId, cancellationToken);
        if (user is not null)
        {
            if (user.Email != identity.Email)
            {
                // The Google account's address changed; it must not take over another account's email.
                var owner = await userRepository.GetByEmailAsync(identity.Email, cancellationToken);
                if (owner is not null && owner.UserId != user.UserId)
                {
                    throw EmailConflict();
                }
                user.Email = identity.Email;
            }
            user.IsEmailVerified = true;
            user.ProfileImageUrl = identity.PictureUrl ?? user.ProfileImageUrl;
            await userRepository.SaveChangesAsync(cancellationToken);
            return user;
        }

        // 2. Existing account with this (Google-verified) email.
        var byEmail = await userRepository.GetByEmailAsync(identity.Email, cancellationToken);
        if (byEmail is not null)
        {
            if (byEmail.GoogleId is not null)
            {
                // Same email but a different Google account: never merge silently.
                logger.LogWarning("Google sign-in rejected: email of user {UserId} belongs to another Google account", byEmail.UserId);
                throw EmailConflict();
            }

            // Google has verified that this person owns the mailbox, so link it to the email-OTP account.
            byEmail.GoogleId = identity.GoogleId;
            byEmail.IsEmailVerified = true;
            byEmail.ProfileImageUrl ??= identity.PictureUrl;
            await userRepository.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Linked Google account to user {UserId}", byEmail.UserId);
            return byEmail;
        }

        // 3. New user.
        user = new User
        {
            GoogleId = identity.GoogleId,
            Email = identity.Email,
            FullName = identity.FullName,
            ProfileImageUrl = identity.PictureUrl,
            IsEmailVerified = true,
        };
        userRepository.Add(user);
        if (await TrySaveAsync(cancellationToken))
        {
            logger.LogInformation("Registered user {UserId} via Google", user.UserId);
            return user;
        }

        // A parallel first sign-in created it a moment ago.
        return await userRepository.GetByGoogleIdAsync(identity.GoogleId, cancellationToken) ?? throw RetryLater();
    }

    private async Task<AuthSession> StartSessionAsync(User user, string method, CancellationToken cancellationToken)
    {
        if (user.IsBlocked)
        {
            logger.LogInformation("Blocked user {UserId} tried to sign in with {Method}", user.UserId, method);
            throw Blocked();
        }

        // Admin:Emails is how the first admin is made: the account becomes an admin when it signs in.
        if (user.Role != UserRoles.Admin
            && adminOptions.Value.Emails.Any(e => string.Equals(e.Trim(), user.Email, StringComparison.OrdinalIgnoreCase)))
        {
            user.Role = UserRoles.Admin;
            await userRepository.SaveChangesAsync(cancellationToken);
            accessCache.Invalidate(user.UserId);
            logger.LogInformation("User {UserId} made an admin (listed in Admin:Emails)", user.UserId);
        }

        var refreshToken = await refreshTokenService.IssueAsync(user.UserId, cancellationToken);
        logger.LogInformation("User {UserId} signed in with {Method}", user.UserId, method);
        return CreateSession(user, refreshToken);
    }

    private static ApiException Blocked() =>
        new(StatusCodes.Status403Forbidden, "Account blocked", BlockedUserMiddleware.Message,
            new Dictionary<string, object?> { ["reason"] = "account_blocked" });

    private AuthSession CreateSession(User user, IssuedRefreshToken refreshToken)
    {
        var accessToken = tokenService.CreateAccessToken(user);
        return new AuthSession(
            new AuthResponse(accessToken.Token, accessToken.ExpiresAt, UserDto.From(user)),
            refreshToken);
    }

    /// <summary>False when a unique index (Email / GoogleId) rejected a duplicate created concurrently.</summary>
    private async Task<bool> TrySaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await userRepository.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (DatabaseProviders.IsUniqueViolation(ex))
        {
            userRepository.DiscardPendingChanges();
            return false;
        }
    }

    private static ApiException EmailConflict() =>
        new(StatusCodes.Status409Conflict, "Email already in use",
            "This email address is already linked to a different Google account.");

    private static ApiException RetryLater() =>
        new(StatusCodes.Status409Conflict, "Please try again", "Your account is being set up. Please sign in again.");

    internal static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    /// <summary>A friendly starting name from the address ("akansha.jha99@gmail.com" -> "Akansha Jha"); editable in Profile.</summary>
    internal static string NameFromEmail(string email)
    {
        var local = email.Split('@')[0];
        var words = local
            .Split(['.', '_', '-', '+'], StringSplitOptions.RemoveEmptyEntries)
            .Select(w => new string(w.Where(char.IsLetter).ToArray()))
            .Where(w => w.Length > 0)
            .Select(w => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(w.ToLowerInvariant()));
        var name = string.Join(' ', words);
        return name.Length > 0 ? name[..Math.Min(name.Length, 200)] : local[..Math.Min(local.Length, 200)];
    }

    /// <summary>"akansha@gmail.com" -> "ak*****@gmail.com"</summary>
    internal static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 0)
        {
            return "***";
        }

        var local = email[..at];
        var visible = local.Length <= 2 ? local[..1] : local[..2];
        return $"{visible}{new string('*', Math.Max(3, local.Length - visible.Length))}{email[at..]}";
    }
}
