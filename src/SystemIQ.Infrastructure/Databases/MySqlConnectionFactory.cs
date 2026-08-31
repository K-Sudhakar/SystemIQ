using MySqlConnector;
using SystemIQ.Application.Secrets;
using SystemIQ.Domain.Databases;
using SystemIQ.Domain.Secrets;

namespace SystemIQ.Infrastructure.Databases;

public sealed class MySqlConnectionFactory(ISecretResolver secretResolver)
{
    public async Task<MySqlConnection> OpenAsync(DatabaseConnection connection, CancellationToken cancellationToken)
    {
        using var secret = await secretResolver.ResolveAsync(new SecretReference(connection.CredentialReference), cancellationToken);
        var builder = new MySqlConnectionStringBuilder(new string(secret.Reveal().Span));
        var result = new MySqlConnection(builder.ConnectionString);
        try { await result.OpenAsync(cancellationToken); return result; }
        catch { await result.DisposeAsync(); throw; }
    }

    public async Task ValidateAsync(DatabaseConnection connection, CancellationToken cancellationToken)
    {
        await using var db = await OpenAsync(connection, cancellationToken);
        await db.PingAsync(cancellationToken);
    }
}
