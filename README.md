# SystemIQ Natural Language SQL

SystemIQ is a generic, read-only natural-language-to-SQL platform built from
`PRD.md` and `TRD.md`. Its portable runtime is a .NET 9 ASP.NET Core API plus a
React/Vite single-page application. Azure Functions remains temporarily as a
compatibility host; local development does not require Azure.

## Projects

- `src/SystemIQ.Domain` - provider-independent models and rules.
- `src/SystemIQ.Application` - provider contracts, RAG, and NL-to-SQL orchestration.
- `src/SystemIQ.Infrastructure` - MySQL, AI, filesystem, SQLite, and secret adapters.
- `src/SystemIQ.Api` - portable ASP.NET Core HTTP host.
- `src/SystemIQ.Worker` - one-shot command host; workflow implementation is partial.
- `src/SystemIQ.Functions` - temporary compatibility host.
- `tests` - Functions regression and portable project tests.
- `client` - React/Vite SPA.
- `infra` - existing Azure infrastructure and operational scripts.

## Local prerequisites

- .NET 9 SDK
- Node.js 20 or newer
- MySQL for live query execution
- Independently hosted OpenAI-compatible chat and embedding services for the live AI/RAG path

Azure login, Azure Storage, Azure SQL, Azure OpenAI, Key Vault, and Entra ID are
not required for the local profile.

## Run locally

Copy `src/SystemIQ.Api/appsettings.Local.json.example` to the ignored file
`src/SystemIQ.Api/appsettings.Local.json`, replace placeholders through local
configuration or environment variables, then run:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project src/SystemIQ.Api
```

In another terminal:

```powershell
Set-Location client
npm install
npm run dev
```

The Vite proxy target defaults to `http://127.0.0.1:5080` and can be changed with
`SYSTEMIQ_API_PROXY_TARGET`. Browser runtime settings are in `client/public/config.js`;
never place database or AI secrets there.

## Required live-path configuration

.NET environment variables use double underscores for nesting. Chat and embeddings
are deliberately independent:

```text
AI__Chat__Provider=OpenAICompatible
AI__Chat__Profile=default
AI__Chat__BaseUrl=<chat-service-base-url>
AI__Chat__Model=Qwen/Qwen2.5-Coder-7B-Instruct
AI__Chat__CredentialRef=config:Secrets:Chat
AI__Chat__TimeoutSeconds=30

AI__Embeddings__Provider=OpenAICompatible
AI__Embeddings__Profile=default
AI__Embeddings__BaseUrl=<embedding-service-base-url>
AI__Embeddings__Model=Qwen/Qwen3-Embedding-8B
AI__Embeddings__CredentialRef=config:Secrets:Embeddings
AI__Embeddings__Dimensions=4096
AI__Embeddings__Version=1
AI__Embeddings__TimeoutSeconds=30
```

`CredentialRef` is optional when a service requires no token. Configure a MySQL
catalog entry and deny-by-default access policy with indexed keys:

```text
ConnectionCatalog__Connections__0__Id=local-mysql
ConnectionCatalog__Connections__0__DisplayName=Local MySQL
ConnectionCatalog__Connections__0__Provider=MySql
ConnectionCatalog__Connections__0__CredentialRef=config:Secrets:MySql

AccessPolicy__Subjects__0__Subject=local-curator
AccessPolicy__Subjects__0__Connections__0__Id=local-mysql
AccessPolicy__Subjects__0__Connections__0__Objects__0=*
```

Supply referenced values only in local secret configuration or the process environment:

```text
Secrets__MySql=<MySQL read-only connection string>
Secrets__Chat=<optional chat token>
Secrets__Embeddings=<optional embedding token>
```

The MySQL identity must be least-privilege and read-only. Temporary Colab/ngrok
URLs and credentials must stay in local external configuration and must never be
committed. Automated tests use fakes and mock HTTP handlers; they do not require
live MySQL, AI, Azure, Colab, or ngrok services.

## Validation

```powershell
dotnet build SystemIQ.sln
dotnet test SystemIQ.sln
Set-Location client
npm test
npm run build
```
