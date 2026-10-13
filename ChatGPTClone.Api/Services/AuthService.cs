using ChatGPTClone.Api.Authentication;
using ChatGPTClone.Api.DTOs.Auth;
using ChatGPTClone.Api.DTOs.Users;
using ChatGPTClone.Api.Interfaces;
using ChatGPTClone.Api.Middleware;
using ChatGPTClone.Api.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ChatGPTClone.Api.Services;

/// <summary>
/// Google sign-in followed by mandatory email OTP. The email used for the OTP always comes from the
/// validated Google token (stored on the user), never from client input.
/// </summary>
public sealed class AuthService(
    IGoogleTokenValidator googleTokenValidator,
    IUserRepository userRepository,
    IOtpService otpService,
    IOtpChallengeProtector challengeProtector,
    ILogger<AuthService> logger) : IAuthService
{
    public async Task<OtpChallengeResponse> SignInWithGoogleAsync(string idToken, CancellationToken cancellationToken)
    {
        var identity = await googleTokenValidator.ValidateAsync(idToken)
            ?? throw new ApiException(StatusCodes.Status401Unauthorized, "Google sign-in failed",
                "We could not verify your Google sign-in. Please try again.");

        var user = await UpsertUserAsync(identity, cancellationToken);
        var issue = await otpService.IssueAsync(user, explicitResend: false, cancellationToken);

        return ToChallengeResponse(user, issue);
    }

    public async Task<OtpChallengeResponse> ResendOtpAsync(string challengeToken, CancellationToken cancellationToken)
    {
        var user = await GetChallengedUserAsync(challengeToken, cancellationToken);
        var issue = await otpService.IssueAsync(user, explicitResend: true, cancellationToken);

        return ToChallengeResponse(user, issue);
    }

    public async Task<VerifyOtpResponse> VerifyOtpAsync(string challengeToken, string code, CancellationToken cancellationToken)
    {
        var user = await GetChallengedUserAsync(challengeToken, cancellationToken);
        await otpService.VerifyAsync(user, code, cancellationToken);

        if (!user.IsEmailVerified)
        {
            user.IsEmailVerified = true;
            await userRepository.SaveChangesAsync(cancellationToken);
        }

        // Phase 5: issue the access token + refresh-token cookie here, and only here.
        return new VerifyOtpResponse(true, UserDto.From(user));
    }

    private async Task<User> UpsertUserAsync(GoogleIdentity identity, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByGoogleIdAsync(identity.GoogleId, cancellationToken);

        if (user is null)
        {
            var existingByEmail = await userRepository.GetByEmailAsync(identity.Email, cancellationToken);
            if (existingByEmail is not null)
            {
                // Same email, different Google account: never merge silently.
                logger.LogWarning("Google account {GoogleId} tried to sign in with an email owned by user {UserId}",
                    identity.GoogleId, existingByEmail.UserId);
                throw EmailConflict();
            }

            user = new User
            {
                GoogleId = identity.GoogleId,
                Email = identity.Email,
                FullName = identity.FullName,
                ProfileImageUrl = identity.PictureUrl,
            };
            userRepository.Add(user);
        }
        else
        {
            // Keep the profile in sync with Google. A changed email must not collide with another account.
            if (user.Email != identity.Email)
            {
                var owner = await userRepository.GetByEmailAsync(identity.Email, cancellationToken);
                if (owner is not null && owner.UserId != user.UserId)
                {
                    throw EmailConflict();
                }

                user.Email = identity.Email;
                user.IsEmailVerified = false;
            }

            user.FullName = identity.FullName;
            user.ProfileImageUrl = identity.PictureUrl;
        }

        try
        {
            await userRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Two first-time sign-ins raced; the unique index on Email/GoogleId stopped the duplicate.
            throw new ApiException(StatusCodes.Status409Conflict, "Please try again",
                "Your account is being set up. Please sign in again.");
        }

        return user;
    }

    private async Task<User> GetChallengedUserAsync(string challengeToken, CancellationToken cancellationToken)
    {
        var userId = challengeProtector.TryRead(challengeToken);
        var user = userId is null ? null : await userRepository.GetByIdAsync(userId.Value, cancellationToken);

        return user ?? throw new ApiException(StatusCodes.Status401Unauthorized, "Verification session expired",
            "Your verification session has expired. Please sign in with Google again.");
    }

    private OtpChallengeResponse ToChallengeResponse(User user, OtpIssueResult issue) =>
        new(challengeProtector.Create(user.UserId), MaskEmail(user.Email), issue.CodeSent,
            issue.ExpiresInSeconds, issue.ResendAvailableInSeconds);

    private static ApiException EmailConflict() =>
        new(StatusCodes.Status409Conflict, "Email already in use",
            "This email address is already linked to a different Google account.");

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
