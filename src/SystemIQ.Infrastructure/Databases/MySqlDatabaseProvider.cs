using SystemIQ.Application.Databases;
using SystemIQ.Domain.Databases;

namespace SystemIQ.Infrastructure.Databases;

public sealed class MySqlDatabaseProvider : IDatabaseProvider
{
    public MySqlDatabaseProvider(MySqlConnectionFactory connections)
    {
        Schema = new MySqlSchemaIntrospector(connections);
        Dialect = new MySqlDialect();
        Validator = new MySqlSqlValidator();
        Executor = new MySqlReadOnlyQueryExecutor(connections);
    }
    public string ProviderId => "mysql";
    public DatabaseCapabilities Capabilities => DatabaseCapabilities.All;
    public ISchemaIntrospector Schema { get; }
    public ISqlDialect Dialect { get; }
    public ISqlValidator Validator { get; }
    public IReadOnlyQueryExecutor Executor { get; }
}
