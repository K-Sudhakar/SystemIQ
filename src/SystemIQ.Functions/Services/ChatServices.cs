using System.Data;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel.ChatCompletion;
using SystemIQ.Functions.Models;

namespace SystemIQ.Functions.Services;

public sealed class SqlQueryService
{
    private readonly ConnectionCatalog _connections;
    public SqlQueryService(ConnectionCatalog connections) => _connections = connections;

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ExecuteAsync(
        string connectionId, string sql, CancellationToken cancellationToken)
    {
        var builder = new SqlConnectionStringBuilder(_connections.GetConnectionString(connectionId))
        {
            ApplicationIntent = ApplicationIntent.ReadOnly,
            TrustServerCertificate = false
        };
        builder["Encrypt"] = true;
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = int.TryParse(Environment.GetEnvironmentVariable("SQL_COMMAND_TIMEOUT_SECONDS"), out var timeout) ? timeout : 30;
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        var maxRows = int.TryParse(Environment.GetEnvironmentVariable("SQL_MAX_ROWS"), out var maximum) ? maximum : 500;
        while (rows.Count < maxRows && await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = await reader.IsDBNullAsync(i, cancellationToken) ? null : reader.GetValue(i);
            }
            rows.Add(row);
        }
        return rows;
    }

    public async Task<IReadOnlyList<GlossaryEntry>> GetSchemaDefaultsAsync(
        string connectionId,
        CancellationToken cancellationToken)
    {
        var builder = new SqlConnectionStringBuilder(_connections.GetConnectionString(connectionId))
        {
            ApplicationIntent = ApplicationIntent.ReadOnly,
            TrustServerCertificate = false
        };
        builder["Encrypt"] = true;
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME
            FROM INFORMATION_SCHEMA.COLUMNS
            ORDER BY TABLE_SCHEMA, TABLE_NAME, ORDINAL_POSITION
            """;
        command.CommandTimeout = 30;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var tables = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(cancellationToken))
        {
            var table = $"{reader.GetString(0)}.{reader.GetString(1)}";
            if (!tables.TryGetValue(table, out var columns))
            {
                tables[table] = columns = [];
            }
            columns.Add(reader.GetString(2));
        }

        return tables.Select(pair => new GlossaryEntry(
                connectionId,
                pair.Key,
                Humanize(pair.Key[(pair.Key.LastIndexOf('.') + 1)..]),
                $"Business data stored in {pair.Key}.",
                [],
                pair.Value,
                []))
            .ToArray();
    }

    private static string Humanize(string value) =>
        Regex.Replace(value.Replace('_', ' '), "([a-z0-9])([A-Z])", "$1 $2");
}

public sealed class ChatOrchestrator
{
    private readonly IServiceProvider _services;
    private readonly BlobChatHistoryStore _history;
    private readonly GlossaryStore _glossary;
    private readonly AccessPolicyService _access;
    private readonly SqlQueryService _database;
    private readonly TimeProvider _clock;

    public ChatOrchestrator(
        IServiceProvider services,
        BlobChatHistoryStore history,
        GlossaryStore glossary,
        AccessPolicyService access,
        SqlQueryService database,
        TimeProvider clock)
    {
        _services = services;
        _history = history;
        _glossary = glossary;
        _access = access;
        _database = database;
        _clock = clock;
    }

    public async Task<ChatResult> AskStreamingAsync(
        string userObjectId,
        IEnumerable<string> roles,
        ChatRequest request,
        Func<string, CancellationToken, Task> onAnswerChunk,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Question) || request.Question.Length > 4000)
            throw new ArgumentException("Question is required and must not exceed 4000 characters.");

        var completion = _services.GetService<IChatCompletionService>()
            ?? throw new InvalidOperationException("The AI service is unavailable. Try again later.");
        var entries = await _glossary.LoadAsync(request.ConnectionId, cancellationToken);
        var matches = GlossaryStore.Match(request.Question, entries);
        var history = await _history.LoadAsync(userObjectId, request.ConnectionId, cancellationToken) ?? [];
        var modelHistory = history.Where(x => x.IncludeInModelContext).TakeLast(12);
        var prompt = BuildSqlPrompt(request.Question, matches, modelHistory);
        var generated = await completion.GetChatMessageContentAsync(new ChatHistory(prompt), cancellationToken: cancellationToken);
        var sql = ExtractSql(generated.Content ?? "");
        var policy = _access.GetPolicy(userObjectId, roles);
        await _access.EnsureGeneratedSqlAllowedAsync(userObjectId, policy, request.ConnectionId, request.Question, sql, cancellationToken);
        var rows = await _database.ExecuteAsync(request.ConnectionId, sql, cancellationToken);

        var answerBuilder = new StringBuilder();
        if (rows.Count == 0)
        {
            const string noResults = "No matching records were found.";
            answerBuilder.Append(noResults);
            await onAnswerChunk(noResults, cancellationToken);
        }
        else
        {
            var answerPrompt =
                $"Answer the user's question concisely using only this JSON data.\nQuestion: {request.Question}\nData: {JsonSerializer.Serialize(rows, JsonOptions.Default)}";
            await foreach (var chunk in completion.GetStreamingChatMessageContentsAsync(
                               new ChatHistory(answerPrompt),
                               cancellationToken: cancellationToken))
            {
                if (string.IsNullOrEmpty(chunk.Content))
                {
                    continue;
                }

                answerBuilder.Append(chunk.Content);
                await onAnswerChunk(chunk.Content, cancellationToken);
            }
        }
        if (answerBuilder.Length == 0)
        {
            const string fallback = "The query completed.";
            answerBuilder.Append(fallback);
            await onAnswerChunk(fallback, cancellationToken);
        }
        var answer = answerBuilder.ToString();

        var now = _clock.GetUtcNow();
        var messageId = Guid.NewGuid().ToString("N");
        history.Add(new(Guid.NewGuid().ToString("N"), "user", request.Question, now));
        var matchedTerms = matches.Select(x => x.BusinessTerm).ToArray();
        var matchedTables = matches.Select(x => x.Table).ToArray();
        history.Add(new(messageId, "assistant", answer, _clock.GetUtcNow(), rows, null,
            matchedTerms, matchedTables));
        await _history.SaveAsync(userObjectId, request.ConnectionId, history, cancellationToken);
        return new(messageId, answer, rows, matchedTerms, matchedTables);
    }

    private static string BuildSqlPrompt(string question, IEnumerable<GlossaryEntry> matches, IEnumerable<ChatMessage> history)
    {
        var builder = new StringBuilder("""
            Generate exactly one Azure SQL read-only SELECT statement.
            Never use INSERT, UPDATE, DELETE, MERGE, DDL, EXEC, temp tables, comments, or multiple statements.
            Return SQL only.

            """);
        foreach (var entry in matches)
        {
            builder.AppendLine($"Glossary: {entry.BusinessTerm}: {entry.Description}; table: {entry.Table}; columns: {string.Join(",", entry.RelatedColumns)}; joins: {string.Join(",", entry.JoinHints)}");
        }
        foreach (var item in history) builder.AppendLine($"{item.Role}: {item.Content}");
        builder.AppendLine($"user: {question}");
        return builder.ToString();
    }

    internal static string ExtractSql(string content)
    {
        var trimmed = content.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline >= 0 && lastFence > firstNewline) trimmed = trimmed[(firstNewline + 1)..lastFence].Trim();
        }
        return trimmed;
    }
}
