using Microsoft.Extensions.Options;

namespace SystemIQ.Api.Options;

public sealed class SystemIqProfileValidator(
    IHostEnvironment environment,
    IOptions<SystemIqOptions> systemIq,
    IOptions<StorageOptions> storage,
    IOptions<DenialStoreOptions> denial,
    IOptions<AuthOptions> auth) : IValidateOptions<AiOptions>
{
    public ValidateOptionsResult Validate(string? name, AiOptions options)
    {
        var errors = new List<string>();
        var isSingleProcessDevelopment = environment.IsDevelopment() &&
            systemIq.Value.DeploymentMode.Equals("SingleProcessDevelopment", StringComparison.OrdinalIgnoreCase);

        if (auth.Value.Mode.Equals("DevelopmentHeader", StringComparison.OrdinalIgnoreCase) && !isSingleProcessDevelopment)
            errors.Add("Auth:Mode DevelopmentHeader is permitted only in Development with SystemIQ:DeploymentMode=SingleProcessDevelopment.");
        if (!auth.Value.Mode.Equals("DevelopmentHeader", StringComparison.OrdinalIgnoreCase) &&
            !auth.Value.Mode.Equals("Oidc", StringComparison.OrdinalIgnoreCase) &&
            !auth.Value.Mode.Equals("Jwt", StringComparison.OrdinalIgnoreCase))
            errors.Add("Auth:Mode must be DevelopmentHeader, Oidc, or Jwt.");
        if (!auth.Value.Mode.Equals("DevelopmentHeader", StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(auth.Value.Authority) || string.IsNullOrWhiteSpace(auth.Value.Audience)))
            errors.Add("Auth:Authority and Auth:Audience are required for OIDC/JWT authentication.");
        if (!storage.Value.Provider.Equals("FileSystem", StringComparison.OrdinalIgnoreCase))
            errors.Add($"Storage provider '{storage.Value.Provider}' is not implemented by the portable host.");
        if (storage.Value.Provider.Equals("FileSystem", StringComparison.OrdinalIgnoreCase) && !isSingleProcessDevelopment)
            errors.Add("Storage provider FileSystem is permitted only in the single-process development profile.");
        if (!denial.Value.Provider.Equals("SQLite", StringComparison.OrdinalIgnoreCase))
            errors.Add($"DenialStore provider '{denial.Value.Provider}' is not implemented by the portable host.");
        if (denial.Value.Provider.Equals("SQLite", StringComparison.OrdinalIgnoreCase) && !isSingleProcessDevelopment)
            errors.Add("DenialStore provider SQLite is permitted only in the single-process development profile.");

        ValidateProvider("AI:Chat", options.Chat, errors);
        ValidateProvider("AI:Embeddings", options.Embeddings, errors);
        if (options.Embeddings.Provider != "Disabled" && options.Embeddings.Dimensions <= 0)
            errors.Add("AI:Embeddings:Dimensions must be positive.");

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private static void ValidateProvider(string section, AiProviderOptions options, ICollection<string> errors)
    {
        if (options.Provider.Equals("Disabled", StringComparison.OrdinalIgnoreCase)) return;
        if (options.BaseUrl is null || !options.BaseUrl.IsAbsoluteUri)
            errors.Add($"{section}:BaseUrl must be an absolute URI when the provider is enabled.");
        else if (options.BaseUrl.Scheme != Uri.UriSchemeHttps && !options.BaseUrl.IsLoopback)
            errors.Add($"{section}:BaseUrl must use HTTPS except for a loopback development service.");
        if (string.IsNullOrWhiteSpace(options.Model)) errors.Add($"{section}:Model is required when the provider is enabled.");
    }
}

public sealed record ConfigurationDiagnostic(string Section, string Provider, string Status);

public static class ConfigurationDiagnostics
{
    public static IReadOnlyList<ConfigurationDiagnostic> Create(
        StorageOptions storage, DenialStoreOptions denial, DatabaseOptions database,
        AiOptions ai, AuthOptions auth) =>
        [
            new("Storage", storage.Provider, "configured"),
            new("DenialStore", denial.Provider, "configured"),
            new("DatabaseProviders", database.DefaultProvider, "configured"),
            new("AI:Chat", ai.Chat.Provider, ai.Chat.Provider == "Disabled" ? "disabled" : "configured"),
            new("AI:Embeddings", ai.Embeddings.Provider, ai.Embeddings.Provider == "Disabled" ? "disabled" : "configured"),
            new("Auth", auth.Mode, "configured")
        ];
}
