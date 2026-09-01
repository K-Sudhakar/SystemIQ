using SystemIQ.Domain.Feedback;

namespace SystemIQ.Application.Feedback;

public interface IFeedbackSink
{
    Task SaveAsync(FeedbackEntry entry, CancellationToken cancellationToken);
}

public static class FeedbackRequestRules
{
    public const int MaximumConnectionIdLength = 128;
    public const int MaximumMessageIdLength = 256;
    public const int MaximumReasonLength = 100;
    public const int MaximumCommentLength = 1000;

    public static string? Validate(
        string? connectionId, string? messageId, string? rating, string? reason, string? comment)
    {
        if (IsInvalidIdentifier(connectionId, MaximumConnectionIdLength))
            return "A valid connectionId is required.";
        if (IsInvalidIdentifier(messageId, MaximumMessageIdLength))
            return "A valid messageId is required.";
        if (rating is not ("up" or "down"))
            return "Rating must be 'up' or 'down'.";
        if (reason?.Length > MaximumReasonLength)
            return $"Reason cannot exceed {MaximumReasonLength} characters.";
        if (comment?.Length > MaximumCommentLength)
            return $"Comment cannot exceed {MaximumCommentLength} characters.";
        return null;
    }

    public static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsInvalidIdentifier(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value) || value.Length > maximumLength || value.Any(char.IsControl);
}
