# SystemIQ DataIQ SQL Assistant

SystemIQ is a read-only natural-language SQL assistant built from `PRD.md` and
`TRD.md`. It uses a .NET 9 Azure Functions isolated-worker API and a React/Vite
single-page application.

## Projects

- `src/SystemIQ.Functions` — authenticated API, SQL generation/execution,
  RBAC enforcement, glossary, chat history, audit logging, denial rate limiting,
  feedback processing, and accuracy reporting.
- `tests/SystemIQ.Functions.Tests` — backend unit tests.
- `client` — React/Vite SPA.
- `infra` — Azure Bicep and operational scripts.

## Local prerequisites

- .NET 9 SDK
- Node.js 20 or newer
- Azurite or an Azure Storage account
- Azure SQL and Azure OpenAI resources for live query execution

Copy the example settings in each project, supply development-only values, then
run the API and SPA independently. Never commit credentials or PHI.

