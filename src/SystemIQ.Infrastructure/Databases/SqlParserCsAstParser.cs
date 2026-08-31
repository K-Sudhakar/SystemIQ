using SqlParser;
using SqlParser.Ast;
using SqlParser.Dialects;
using SystemIQ.Application.Databases;
using SystemIQ.Domain.Databases;

namespace SystemIQ.Infrastructure.Databases;

public sealed class SqlParserCsAstParser : ISqlAstParser
{
    public Task<ParsedSqlStatement> ParseAsync(string sql, string dialect, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var inspection = Inspect(sql, dialect);
        return Task.FromResult(inspection.Parsed);
    }

    internal static SqlInspection Inspect(string sql, string dialect)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        var selectedDialect = dialect.Equals("mysql", StringComparison.OrdinalIgnoreCase)
            ? new SqlParser.Dialects.MySqlDialect()
            : throw new NotSupportedException($"SQL dialect '{dialect}' is not supported by this parser adapter.");
        var statements = new SqlQueryParser().Parse(sql, selectedDialect);
        var visitor = new ReferenceVisitor();
        statements.Visit(visitor);
        var root = statements.Count == 1 ? statements[0] : null;
        var kind = root switch
        {
            Statement.Select => SqlStatementKind.Query,
            Statement.Insert or Statement.Update or Statement.Delete or Statement.Merge => SqlStatementKind.Mutation,
            Statement.CreateTable or Statement.CreateView or Statement.AlterTable or Statement.Drop => SqlStatementKind.Definition,
            Statement.Execute or Statement.Call => SqlStatementKind.Procedure,
            _ => SqlStatementKind.Unknown
        };
        var normalized = root?.ToSql() ?? string.Empty;
        var select = root as Statement.Select;
        var hasLocks = select?.Query.Locks?.Count > 0;
        var hasOutput = normalized.Contains(" INTO ", StringComparison.OrdinalIgnoreCase) || normalized.Contains(" OUTFILE ", StringComparison.OrdinalIgnoreCase) || normalized.Contains(" DUMPFILE ", StringComparison.OrdinalIgnoreCase);
        var references = visitor.Relations.Select(ToReference).Distinct().ToArray();
        int? numericLimit = select?.Query.Limit is Expression.LiteralValue { Value: Value.Number number } && int.TryParse(number.Value, out var parsedLimit)
            ? parsedLimit : null;
        return new SqlInspection(new ParsedSqlStatement(kind, references, hasLocks, hasOutput, statements.Count != 1), normalized,
            select?.Query.Limit is not null || select?.Query.Fetch is not null, numericLimit);
    }

    private static SqlObjectReference ToReference(ObjectName name)
    {
        var values = name.Values.Select(x => x.Value).ToArray();
        return values.Length switch
        {
            >= 3 => new(values[^3], values[^2], values[^1]),
            2 => new(null, values[0], values[1]),
            _ => new(null, null, values[0])
        };
    }
    private sealed class ReferenceVisitor : Visitor
    {
        public List<ObjectName> Relations { get; } = [];
        public override ControlFlow PreVisitRelation(ObjectName relation) { Relations.Add(relation); return ControlFlow.Continue; }
    }
    internal sealed record SqlInspection(ParsedSqlStatement Parsed, string NormalizedSql, bool HasLimit, int? NumericLimit);
}
