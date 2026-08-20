using Azure.Core;
using Azure.Identity;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.SemanticKernel;
using SystemIQ.Functions.Security;
using SystemIQ.Functions.Services;

var builder = FunctionsApplication.CreateBuilder(args);
builder.ConfigureFunctionsWebApplication();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<TokenCredential, DefaultAzureCredential>();
builder.Services.AddSingleton<BearerTokenValidator>();
builder.Services.AddSingleton<ConnectionCatalog>();
builder.Services.AddSingleton<SqlSafetyValidator>();
builder.Services.AddSingleton<AccessPolicyService>();
builder.Services.AddSingleton<AuditLogService>();
builder.Services.AddSingleton<AccessDenialRateLimiter>();
builder.Services.AddSingleton<BlobChatHistoryStore>();
builder.Services.AddSingleton<GlossaryStore>();
builder.Services.AddSingleton<FeedbackService>();
builder.Services.AddSingleton<AccuracyReportingService>();
builder.Services.AddSingleton<SqlQueryService>();
builder.Services.AddSingleton<ChatOrchestrator>();

var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
var deployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_CHAT_DEPLOYMENT");
if (!string.IsNullOrWhiteSpace(endpoint) && !string.IsNullOrWhiteSpace(deployment))
{
    builder.Services.AddAzureOpenAIChatCompletion(deployment, endpoint, new DefaultAzureCredential());
}

builder.Build().Run();
