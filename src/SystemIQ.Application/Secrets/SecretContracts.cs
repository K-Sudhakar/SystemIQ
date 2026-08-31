using SystemIQ.Domain.Secrets;

namespace SystemIQ.Application.Secrets;

public interface ISecretResolver { Task<SecretValue> ResolveAsync(SecretReference reference, CancellationToken cancellationToken); }
