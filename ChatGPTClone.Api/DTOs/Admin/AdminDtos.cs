namespace ChatGPTClone.Api.DTOs.Admin;

/// <summary>One account in the admin user list, with today's (UTC) usage.</summary>
public sealed record AdminUserDto(
    int UserId,
    string FullName,
    string Email,
    string Role,
    bool IsBlocked,
    DateTime CreatedAt,
    int MessagesToday,
    long TokensToday,
    int UploadsToday,
    DateTime? LastActiveOn);

public sealed record AdminUserPageDto(IReadOnlyList<AdminUserDto> Items, int Total, int Page, int PageSize);

/// <summary>All users' usage on one UTC day.</summary>
public sealed record UsageDayDto(
    DateTime Date,
    int ActiveUsers,
    int Messages,
    long InputTokens,
    long OutputTokens,
    int Uploads,
    long UploadBytes,
    decimal EstimatedCost);

public sealed record UsageTotalsDto(
    int ActiveUsers,
    int Messages,
    long InputTokens,
    long OutputTokens,
    int Uploads,
    long UploadBytes,
    decimal EstimatedCost);

public sealed record TopUserDto(int UserId, string FullName, string Email, int Messages, long Tokens, decimal EstimatedCost);

public sealed record UsageLimitsDto(
    long DailyTokenQuota,
    int DailyUploadQuota,
    decimal InputTokenPricePerMillion,
    decimal OutputTokenPricePerMillion);

/// <summary>GET /api/admin/usage: one row per day (oldest first, days without usage included as zeros).</summary>
public sealed record AdminUsageDto(
    DateTime From,
    DateTime To,
    IReadOnlyList<UsageDayDto> Days,
    UsageTotalsDto Totals,
    IReadOnlyList<TopUserDto> TopUsers,
    UsageLimitsDto Limits);

/// <summary>A rating on an AI reply, with the question and reply it was about (shortened).</summary>
public sealed record AdminFeedbackDto(
    int FeedbackId,
    short Rating,
    string? Comment,
    DateTime CreatedAt,
    int UserId,
    string UserEmail,
    long MessageId,
    string? ModelId,
    string Question,
    string Reply);

public sealed record AdminFeedbackPageDto(
    IReadOnlyList<AdminFeedbackDto> Items,
    int Total,
    int Page,
    int PageSize,
    int Helpful,
    int NotHelpful);
