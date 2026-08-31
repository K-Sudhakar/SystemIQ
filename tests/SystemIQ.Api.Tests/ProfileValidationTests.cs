using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using SystemIQ.Api.Options;

namespace SystemIQ.Api.Tests;

public sealed class ProfileValidationTests
{
    [Fact]
    public void Production_accumulates_unsafe_local_profile_errors()
    {
        var validator = new SystemIqProfileValidator(new StubEnvironment("Production"),
            Microsoft.Extensions.Options.Options.Create(new SystemIqOptions()), Microsoft.Extensions.Options.Options.Create(new StorageOptions()),
            Microsoft.Extensions.Options.Options.Create(new DenialStoreOptions()), Microsoft.Extensions.Options.Options.Create(new AuthOptions()));
        var result = validator.Validate(null, new AiOptions());
        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, x => x.Contains("DevelopmentHeader", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, x => x.Contains("FileSystem", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, x => x.Contains("SQLite", StringComparison.Ordinal));
    }

    [Fact]
    public void Diagnostics_do_not_contain_credentials_or_urls()
    {
        var ai = new AiOptions { Chat = new() { Provider = "OpenAICompatible", BaseUrl = new Uri("https://secret-host.invalid"), CredentialRef = "config:secret" } };
        var text = System.Text.Json.JsonSerializer.Serialize(ConfigurationDiagnostics.Create(new(), new(), new(), ai, new()));
        Assert.DoesNotContain("secret-host", text, StringComparison.Ordinal);
        Assert.DoesNotContain("config:secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Unsupported_storage_and_denial_providers_fail_closed()
    {
        var validator = new SystemIqProfileValidator(new StubEnvironment("Development"),
            Microsoft.Extensions.Options.Options.Create(new SystemIqOptions()),
            Microsoft.Extensions.Options.Options.Create(new StorageOptions { Provider = "Unknown" }),
            Microsoft.Extensions.Options.Options.Create(new DenialStoreOptions { Provider = "Unknown" }),
            Microsoft.Extensions.Options.Options.Create(new AuthOptions()));

        var result = validator.Validate(null, new AiOptions());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, x => x.Contains("not implemented", StringComparison.Ordinal));
    }

    private sealed class StubEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
