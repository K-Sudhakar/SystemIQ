using System.Text.Json;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Data.Tables;
using Microsoft.Extensions.Hosting;
using SystemIQ.Functions.Models;

namespace SystemIQ.Functions.Services;

public sealed class StorageClientFactory
{
    private readonly TokenCredential _credential;

    public StorageClientFactory(TokenCredential credential) => _credential = credential;

    public string? LocalConnectionString =>
        Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING") is { Length: > 0 } configured
            ? configured
            : Environment.GetEnvironmentVariable("AzureWebJobsStorage")
                ?.Equals("UseDevelopmentStorage=true", StringComparison.OrdinalIgnoreCase) == true
                ? "UseDevelopmentStorage=true"
                : null;

    public BlobContainerClient BlobContainer(string endpointSetting, string localContainerName)
    {
        if (LocalConnectionString is { } connectionString)
        {
            return new BlobContainerClient(connectionString, localContainerName);
        }

        var uri = Environment.GetEnvironmentVariable(endpointSetting);
        if (string.IsNullOrWhiteSpace(uri)) throw new InvalidOperationException($"{endpointSetting} is required.");
        return new BlobContainerClient(new Uri(uri), _credential);
    }

    public TableClient Table(string endpointSetting, string tableName)
    {
        if (LocalConnectionString is { } connectionString)
        {
            return new TableClient(connectionString, tableName);
        }

        var endpoint = Environment.GetEnvironmentVariable(endpointSetting);
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new InvalidOperationException($"{endpointSetting} is required; rate limiting fails closed.");
        }
        return new TableClient(new Uri(endpoint), tableName, _credential);
    }
}

public sealed class LocalStorageInitializer : IHostedService
{
    private readonly StorageClientFactory _clients;

    public LocalStorageInitializer(StorageClientFactory clients) => _clients = clients;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_clients.LocalConnectionString is null)
        {
            return;
        }

        foreach (var (setting, name) in new[]
        {
            ("CHAT_HISTORY_BLOB_CONTAINER_URI", "chat-history"),
            ("GLOSSARY_BLOB_CONTAINER_URI", "glossary"),
            ("FEEDBACK_BLOB_CONTAINER_URI", "feedback"),
            ("AUDIT_LOG_BLOB_CONTAINER_URI", "audit-log")
        })
        {
            await _clients.BlobContainer(setting, name).CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        }

        var tableName = Environment.GetEnvironmentVariable("RATE_LIMIT_TABLE_NAME") ?? "AccessDenials";
        await _clients.Table("RATE_LIMIT_TABLE_ENDPOINT", tableName).CreateIfNotExistsAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class BlobJsonStore
{
    private readonly StorageClientFactory _clients;
    private readonly string _setting;
    private readonly string _localContainerName;

    public BlobJsonStore(StorageClientFactory clients, string setting, string localContainerName)
    {
        _clients = clients;
        _setting = setting;
        _localContainerName = localContainerName;
    }

    public BlobContainerClient Container() => _clients.BlobContainer(_setting, _localContainerName);

    public async Task<T?> ReadAsync<T>(string name, CancellationToken cancellationToken)
    {
        var blob = Container().GetBlobClient(name);
        try
        {
            var download = await blob.DownloadContentAsync(cancellationToken);
            return download.Value.Content.ToObjectFromJson<T>(JsonOptions.Default);
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            return default;
        }
    }

    public async Task WriteAsync<T>(string name, T value, CancellationToken cancellationToken) =>
        _ = await Container().GetBlobClient(name).UploadAsync(
            BinaryData.FromObjectAsJson(value, JsonOptions.Default),
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" } },
            cancellationToken);

    public async Task DeleteIfExistsAsync(string name, CancellationToken cancellationToken) =>
        _ = await Container().GetBlobClient(name).DeleteIfExistsAsync(
            DeleteSnapshotsOption.IncludeSnapshots,
            cancellationToken: cancellationToken);

    public async IAsyncEnumerable<T> ReadAllAsync<T>(
        string prefix,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in Container().GetBlobsAsync(prefix: prefix, cancellationToken: cancellationToken))
        {
            var value = await ReadAsync<T>(item.Name, cancellationToken);
            if (value is not null) yield return value;
        }
    }
}

public sealed class BlobChatHistoryStore
{
    private readonly BlobJsonStore _store;
    public BlobChatHistoryStore(StorageClientFactory clients) =>
        _store = new(clients, "CHAT_HISTORY_BLOB_CONTAINER_URI", "chat-history");

    public Task<List<ChatMessage>?> LoadAsync(string userObjectId, string connectionId, CancellationToken cancellationToken) =>
        _store.ReadAsync<List<ChatMessage>>(Path(userObjectId, connectionId), cancellationToken);

    public Task SaveAsync(string userObjectId, string connectionId, IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken) =>
        _store.WriteAsync(Path(userObjectId, connectionId), messages, cancellationToken);

    public IAsyncEnumerable<List<ChatMessage>> ReadAllAsync(CancellationToken cancellationToken) =>
        _store.ReadAllAsync<List<ChatMessage>>("history/", cancellationToken);

    private static string Path(string user, string connection) =>
        $"history/{Encode(user)}/{Encode(connection)}.json";
    private static string Encode(string value) => Uri.EscapeDataString(value);
}

public sealed class GlossaryStore
{
    private readonly BlobJsonStore _store;
    public GlossaryStore(StorageClientFactory clients) =>
        _store = new(clients, "GLOSSARY_BLOB_CONTAINER_URI", "glossary");

    public async Task<List<GlossaryEntry>> LoadAsync(string connectionId, CancellationToken cancellationToken) =>
        await _store.ReadAsync<List<GlossaryEntry>>($"glossary/{Uri.EscapeDataString(connectionId)}.json", cancellationToken) ?? [];

    public async Task UpsertAsync(GlossaryEntry entry, CancellationToken cancellationToken)
    {
        var entries = await LoadAsync(entry.ConnectionId, cancellationToken);
        entries.RemoveAll(x => x.Table.Equals(entry.Table, StringComparison.OrdinalIgnoreCase));
        entries.Add(entry);
        await _store.WriteAsync($"glossary/{Uri.EscapeDataString(entry.ConnectionId)}.json", entries, cancellationToken);
    }

    public static IReadOnlyList<GlossaryEntry> Match(string question, IEnumerable<GlossaryEntry> entries)
    {
        var matches = entries.Where(entry =>
                ContainsPhrase(question, entry.BusinessTerm) ||
                entry.Synonyms.Any(s => ContainsPhrase(question, s)))
            .OrderByDescending(entry => entry.RelatedColumns.Count)
            .ToList();
        if (matches.Count < 2) return matches;

        var coveredColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<GlossaryEntry>();
        foreach (var match in matches)
        {
            var addsCoverage = match.RelatedColumns.Count == 0 ||
                match.RelatedColumns.Any(column => !coveredColumns.Contains(column));
            if (addsCoverage)
            {
                result.Add(match);
                coveredColumns.UnionWith(match.RelatedColumns);
            }
        }
        return result;
    }

    private static bool ContainsPhrase(string text, string phrase) =>
        !string.IsNullOrWhiteSpace(phrase) &&
        text.Contains(phrase, StringComparison.OrdinalIgnoreCase);
}

public sealed class FeedbackService
{
    private readonly BlobJsonStore _store;
    private readonly BlobChatHistoryStore _history;
    private readonly TimeProvider _clock;

    public FeedbackService(StorageClientFactory clients, BlobChatHistoryStore history, TimeProvider clock)
    {
        _store = new(clients, "FEEDBACK_BLOB_CONTAINER_URI", "feedback");
        _history = history;
        _clock = clock;
    }

    public async Task SubmitAsync(string user, FeedbackRequest request, CancellationToken cancellationToken)
    {
        if (request.Rating is not ("up" or "down")) throw new ArgumentException("Rating must be 'up' or 'down'.");
        var messages = await _history.LoadAsync(user, request.ConnectionId, cancellationToken) ?? [];
        var index = messages.FindIndex(x => x.Id == request.MessageId && x.Role == "assistant");
        if (index < 0) throw new KeyNotFoundException("Message was not found.");
        messages[index] = messages[index] with
        {
            Feedback = new(request.Rating, request.Reason, request.Comment, _clock.GetUtcNow())
        };
        await _history.SaveAsync(user, request.ConnectionId, messages, cancellationToken);

        if (request.Rating == "down")
        {
            var answer = messages[index];
            var question = messages.Take(index).LastOrDefault(x => x.Role == "user")?.Content ?? "";
            var item = new FeedbackReviewItem(
                Guid.NewGuid().ToString("N"), request.ConnectionId, request.MessageId, question,
                answer.MatchedTerms ?? [], answer.MatchedTables ?? [],
                request.Reason, request.Comment, _clock.GetUtcNow(), false);
            await _store.WriteAsync($"queue/{item.Id}.json", item, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<FeedbackReviewItem>> PendingAsync(string? connectionId, CancellationToken cancellationToken)
    {
        var result = new List<FeedbackReviewItem>();
        await foreach (var item in _store.ReadAllAsync<FeedbackReviewItem>("reviews/", cancellationToken))
        {
            if (!item.Resolved &&
                (string.IsNullOrWhiteSpace(connectionId) || item.ConnectionId == connectionId))
            {
                result.Add(item);
            }
        }
        return result.OrderBy(x => x.CreatedAt).ToArray();
    }

    public async Task<int> ProcessAsync(CancellationToken cancellationToken)
    {
        var count = 0;
        await foreach (var item in _store.ReadAllAsync<FeedbackReviewItem>("queue/", cancellationToken))
        {
            var reviewPath = $"reviews/{item.Id}.json";
            var existing = await _store.ReadAsync<FeedbackReviewItem>(reviewPath, cancellationToken);
            if (existing?.Resolved == true)
            {
                await _store.DeleteIfExistsAsync($"queue/{item.Id}.json", cancellationToken);
                continue;
            }
            await _store.WriteAsync(reviewPath, item, cancellationToken);
            await _store.DeleteIfExistsAsync($"queue/{item.Id}.json", cancellationToken);
            count++;
        }
        return count;
    }

    public async Task ResolveAsync(string id, CancellationToken cancellationToken)
    {
        var path = $"reviews/{id}.json";
        var item = await _store.ReadAsync<FeedbackReviewItem>(path, cancellationToken)
            ?? throw new KeyNotFoundException("Review item was not found.");
        await _store.WriteAsync(path, item with { Resolved = true }, cancellationToken);
    }
}

public sealed class AccuracyReportingService
{
    private readonly BlobChatHistoryStore _history;
    public AccuracyReportingService(BlobChatHistoryStore history) => _history = history;

    public async Task<AccuracyReport> CreateAsync(
        CancellationToken cancellationToken,
        DateTimeOffset? since = null)
    {
        var messages = new List<ChatMessage>();
        await foreach (var history in _history.ReadAllAsync(cancellationToken)) messages.AddRange(history);
        if (since is not null)
        {
            messages.RemoveAll(message => message.CreatedAt < since);
        }
        return Calculate(messages);
    }

    public static AccuracyReport Calculate(IEnumerable<ChatMessage> messages)
    {
        var answers = messages.Where(x => x.Role == "assistant").ToArray();
        var rated = answers.Where(x => x.Feedback is not null).ToArray();
        var up = rated.Count(x => x.Feedback!.Rating == "up");
        var down = rated.Count(x => x.Feedback!.Rating == "down");
        static double Percentage(int numerator, int denominator) =>
            denominator == 0 ? 0 : Math.Round(numerator * 100d / denominator, 2);
        return new(
            Percentage(up, rated.Length),
            Percentage(down, rated.Length),
            Percentage(rated.Length, answers.Length),
            answers.Length,
            rated.Length,
            answers.Length == 0 ? null : answers.Min(x => x.CreatedAt),
            answers.Length == 0 ? null : answers.Max(x => x.CreatedAt));
    }
}
