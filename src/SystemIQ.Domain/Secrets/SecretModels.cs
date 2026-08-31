namespace SystemIQ.Domain.Secrets;

public sealed record SecretReference(string Value);

public sealed class SecretValue : IDisposable
{
    private char[] _value;
    public SecretValue(string value) => _value = value?.ToCharArray() ?? throw new ArgumentNullException(nameof(value));
    public ReadOnlyMemory<char> Reveal() => _value;
    public override string ToString() => "[REDACTED]";
    public void Dispose() { Array.Clear(_value); _value = []; }
}
