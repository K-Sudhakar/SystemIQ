using SystemIQ.Functions.Models;
using SystemIQ.Functions.Services;

namespace SystemIQ.Functions.Tests;

public sealed class AccuracyReportingServiceTests
{
    [Fact]
    public void Calculates_rates_and_feedback_coverage()
    {
        var now = DateTimeOffset.Parse("2026-07-29T10:00:00Z");
        var messages = new[]
        {
            new ChatMessage("q1", "user", "question", now),
            new ChatMessage("a1", "assistant", "answer", now.AddMinutes(1), Feedback: new("up", null, null, now)),
            new ChatMessage("a2", "assistant", "answer", now.AddMinutes(2), Feedback: new("down", null, null, now)),
            new ChatMessage("a3", "assistant", "answer", now.AddMinutes(3))
        };

        var report = AccuracyReportingService.Calculate(messages);

        Assert.Equal(50, report.ThumbsUpRate);
        Assert.Equal(50, report.ThumbsDownRate);
        Assert.Equal(66.67, report.FeedbackCoverage);
        Assert.Equal(3, report.AnswerCount);
        Assert.Equal(2, report.RatedCount);
    }

    [Fact]
    public void Returns_defined_empty_state()
    {
        var report = AccuracyReportingService.Calculate([]);
        Assert.Equal(0, report.ThumbsUpRate);
        Assert.Equal(0, report.FeedbackCoverage);
        Assert.Null(report.From);
        Assert.Null(report.To);
    }
}
