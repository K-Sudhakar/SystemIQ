namespace SystemIQ.Infrastructure.AI;

public sealed record OpenAiCompatibleOptions(
    string ProviderId,
    Uri BaseUrl,
    string Model,
    TimeSpan Timeout,
    string? ApiKey = null,
    int? Dimensions = null);
