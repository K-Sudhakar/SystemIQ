using SystemIQ.Application.Databases;
using SystemIQ.Domain.Databases;

namespace SystemIQ.Infrastructure.Databases;

public sealed class MySqlSqlValidator : ISqlValidator
{
    public Task<SqlValidationResult> ValidateAsync(string sql, SqlValidationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (sql.Contains("--", StringComparison.Ordinal) || sql.Contains("/*", StringComparison.Ordinal) || sql.Contains('#'))
            return Task.FromResult(SqlValidationResult.Reject("SQL comments and directives are not allowed."));
        SqlParserCsAstParser.SqlInspection inspection;
        try { inspection = SqlParserCsAstParser.Inspect(sql, "mysql"); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        { return Task.FromResult(SqlValidationResult.Reject("SQL could not be parsed safely.")); }

        var parsed = inspection.Parsed;
        if (parsed.HasMultipleStatements) return Task.FromResult(SqlValidationResult.Reject("Exactly one SQL statement is required."));
        if (parsed.Kind != SqlStatementKind.Query) return Task.FromResult(SqlValidationResult.Reject("Only read-only query statements are allowed."));
        if (parsed.HasLockingClause) return Task.FromResult(SqlValidationResult.Reject("Locking clauses are not allowed."));
        if (parsed.HasOutputClause) return Task.FromResult(SqlValidationResult.Reject("SQL output/write clauses are not allowed."));
        var normalized = inspection.NormalizedSql;
        if (new[] { "SLEEP(", "BENCHMARK(", "LOAD_FILE(" }.Any(unsafeFunction => normalized.Contains(unsafeFunction, StringComparison.OrdinalIgnoreCase)))
            return Task.FromResult(SqlValidationResult.Reject("The query uses an unsafe function."));
        foreach (var reference in parsed.ReferencedObjects)
        {
            var qualified = string.Join('.', new[] { reference.Catalog, reference.Schema, reference.Name }.Where(x => !string.IsNullOrWhiteSpace(x)));
            if (context.AllowedCatalog is not null &&
                ((!string.IsNullOrWhiteSpace(reference.Catalog) && !reference.Catalog.Equals(context.AllowedCatalog, StringComparison.OrdinalIgnoreCase)) ||
                 (!string.IsNullOrWhiteSpace(reference.Schema) && !reference.Schema.Equals(context.AllowedCatalog, StringComparison.OrdinalIgnoreCase))))
                return Task.FromResult(SqlValidationResult.Reject("The query references an object outside the configured catalog."));
            if (!context.AuthorizedObjects.Contains("*") &&
                ((reference.Catalog is not null || reference.Schema is not null)
                    ? !context.AuthorizedObjects.Contains(qualified)
                    : !context.AuthorizedObjects.Contains(reference.Name)))
                return Task.FromResult(SqlValidationResult.Reject("The query references an unauthorized object."));
        }
        if (context.MaxRows <= 0) return Task.FromResult(SqlValidationResult.Reject("The configured row limit is invalid."));
        if (inspection.HasLimit && (inspection.NumericLimit is null || inspection.NumericLimit > context.MaxRows))
            return Task.FromResult(SqlValidationResult.Reject("The query row limit is absent, non-literal, or exceeds policy."));
        var boundedSql = inspection.HasLimit ? inspection.NormalizedSql : $"{inspection.NormalizedSql} LIMIT {context.MaxRows}";
        return Task.FromResult(SqlValidationResult.Allow(boundedSql, parsed.ReferencedObjects));
    }
}
