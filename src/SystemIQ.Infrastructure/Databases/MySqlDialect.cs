using System.Text;
using SystemIQ.Application.Databases;
using SystemIQ.Domain.Databases;
using SystemIQ.Domain.Rag;

namespace SystemIQ.Infrastructure.Databases;

public sealed class MySqlDialect : ISqlDialect
{
    public string IdentifierQuote => "`";
    public string QuoteIdentifier(string identifier) => $"`{identifier.Replace("`", "``", StringComparison.Ordinal)}`";
    public string BuildSqlGenerationGuidance(SchemaSnapshot schema, IReadOnlyList<RagContextChunk> context)
    {
        var builder = new StringBuilder("Generate exactly one read-only MySQL SELECT query. Use backticks for identifiers and LIMIT for row bounds. Never use INTO, OUTFILE, DUMPFILE, locks, procedures, or mutations.\nAuthorized schema context:\n");
        foreach (var chunk in context) builder.Append("- ").Append(chunk.Text).AppendLine();
        return builder.ToString();
    }
}
