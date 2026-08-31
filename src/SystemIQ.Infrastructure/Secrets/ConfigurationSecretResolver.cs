using Microsoft.Extensions.Configuration;
using SystemIQ.Application.Secrets;
using SystemIQ.Domain.Secrets;

namespace SystemIQ.Infrastructure.Secrets;

public sealed class ConfigurationSecretResolver(IConfiguration configuration) : ISecretResolver
{
    public async Task<SecretValue> ResolveAsync(SecretReference secretReference, CancellationToken cancellationToken = default)
    {
        var reference = secretReference.Value;
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);

        if (reference.StartsWith("config:", StringComparison.OrdinalIgnoreCase))
        {
            var key = reference[7..];
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new SecretResolutionException("The configuration secret reference has no key.");
            }

            var value = configuration[key];
            return string.IsNullOrWhiteSpace(value)
                ? throw new SecretResolutionException($"Secret reference '{reference}' could not be resolved.")
                : new SecretValue(value);
        }

        if (reference.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            var path = reference[5..];
            if (!Path.IsPathFullyQualified(path))
            {
                throw new SecretResolutionException("Secret file paths must be absolute.");
            }

            try
            {
                var value = (await File.ReadAllTextAsync(path, cancellationToken)).TrimEnd('\r', '\n');
                return string.IsNullOrWhiteSpace(value)
                    ? throw new SecretResolutionException("The secret file is empty.")
                    : new SecretValue(value);
            }
            catch (SecretResolutionException)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new SecretResolutionException($"Secret reference '{reference}' could not be resolved.", exception);
            }
        }

        throw new SecretResolutionException("Only config: and file: secret references are supported.");
    }
}

public sealed class SecretResolutionException(string message, Exception? innerException = null)
    : Exception(message, innerException);
