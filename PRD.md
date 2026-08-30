---
Document ID: PRD-2026-001
Label: prd-systemiq-nl-to-sql
Version: 1.0.4
Status: Implementation Ready
Date: 2026-08-28
Scale Depth: MEDIUM
Total Requirements: 23
Readiness Score: 4.88 (PASS — Implementation Ready)
---

# SystemIQ — Product Requirements Document

## PRD Health Summary

- **Requirements:** 23 — Must 20, Should 3; 81 acceptance criteria; 100% coverage
- **Risk coverage:** 23/23 requirements
- **Dependencies:** 21 dependent requirements, 64 edges, no cycle identified
- **Ambiguities:** 0 open
- **Readiness:** PASS and implementation-ready; D-01 through D-06 are closed or baselined for implementation, with remaining external inputs explicitly non-blocking for the portable provider architecture

## Product Summary

SystemIQ is a generic, domain-neutral Natural-Language-to-SQL platform. It enables an authorized user to select a configured relational database, ask a question in ordinary language, retrieve relevant schema and business-glossary context, generate dialect-correct read-only SQL through configured AI providers, execute it with a least-privilege database identity, and receive a natural-language answer plus actual result rows.

The product must not assume Demo Clinical Operations, MP3, BabyTrax, healthcare terminology, Azure SQL, or any other single domain/database. Those may exist only as optional sample configurations. Each connection/domain has a configurable glossary containing its own terms, synonyms, schema mappings, and join guidance.

Database, AI, storage, identity, secret, and hosting integrations must be explicitly configured providers rather than Azure assumptions. Azure Blob Storage, Azure OpenAI, Entra ID, Key Vault, and Azure-hosted databases may remain production options.

### Users

- Business users ask domain questions and inspect answers and rows.
- Data/domain curators maintain per-connection glossary content.
- Administrators configure providers, connections, policies, identity, and secrets.
- Developers/operators run SystemIQ locally or on a supported container platform.

## Key Journeys

### J1 — complete local flow

Start the documented local profile without an Azure subscription or Storage account → select a MySQL connection → ask a natural-language question → retrieve MySQL schema and glossary/RAG context → generate MySQL SQL → validate and execute it read-only → receive actual rows and an answer.

### J2 — configure a new domain

An administrator adds provider metadata and a secret reference without code changes. A curator creates that connection's glossary. Provider-specific schema discovery supplies grounding, and other domains remain isolated.

### J3 — governed query

An authenticated user sees only permitted connections. SystemIQ blocks denied objects and unsafe SQL before execution, durably audits the denial, rate-limits repeated denials, and does not disclose restricted schema.

### J4 — history and feedback

Per-user/per-connection history is restored without cross-connection leakage. Negative feedback creates an idempotent review tied to matched terms; an authorized curator can resolve it.

## Goals and Non-Goals

### Goals

- Generic NL-to-SQL with domain-configurable grounding.
- MySQL first; PostgreSQL next; future relational providers without redesign.
- Independently configurable chat-completion and embedding/RAG providers.
- Full local development without an Azure subscription or Azure Storage account.
- Safe local storage plus Azure Blob as an optional production provider.
- A staged move to container-friendly ASP.NET Core and Kubernetes-compatible scheduling.
- Read-only execution, authorization, durable audit, and secret hygiene across providers.

### Non-goals

- Data-modifying SQL is never permitted.
- Google Colab is not production inference, a secret store, or a location for production/regulated data.
- Kubernetes manifests, Helm charts, or application implementation are not part of this refinement.
- Self-hosting every dependency in Kubernetes is not required.
- Automatic AI-provider failover is not required initially.
- Selected Azure providers do not need to be removed from deployments that intentionally use them.

## Success Criteria

| ID | Outcome and decision rule | Coverage |
|---|---|---|
| SC-1 | A clean environment passes J1 at 100% without Azure login, subscription, or Storage account; the canned demo API does not count | REQ-001, 002, 005, 014–018, 020–023 |
| SC-2 | MySQL integration/evaluation tests cover schema, quoting, limits, joins, dates, cancellation, validation, and read-only execution; all safety cases pass | REQ-014, 017, 018 |
| SC-3 | A new provider can be added without changing domain orchestration or another provider; capability/conformance tests expose differences | REQ-016–021 |
| SC-4 | Executed statements violating policy or read-only rules remain zero | REQ-008, 009, 014, 017 |
| SC-5 | Connections maintain independent, non-healthcare-default glossary/RAG content | REQ-005–007, 021 |
| SC-6 | Quality reporting includes feedback participation, database/dialect, and AI provider/model; targets follow a representative baseline | REQ-013, 020, 021 |
| SC-7 | ASP.NET Core API and one-shot processor pass contract, health, shutdown, and container smoke tests before Kubernetes work | REQ-022, 023 |

## Architecture Direction

### Current

React/Vite → MSAL/Entra → .NET 9 Azure Functions (HTTP + timer) → Azure OpenAI, SQL Server/Azure SQL, Azure Blob for history/glossary/feedback/audit, Azure Table for denial state, and raw environment JSON for catalog/policy. `AzureWebJobsStorage` is needed by the Functions timer. Embedding configuration exists but is unused.

### Target

React static frontend → standard ASP.NET Core API → explicit database, chat, embedding, storage, identity, audit, and denial-state providers. API replicas are stateless. An idempotent one-shot processor runs through a Kubernetes CronJob or equivalent scheduler. Configuration is typed and startup-validated; secrets are externally injected; identity is generic OIDC/JWT; logs are structured with OpenTelemetry; liveness, readiness, and startup health are distinct.

### Decisions

1. **Host:** refactor toward ASP.NET Core; do not make Functions-on-Kubernetes the final target. Keeping Functions retains its runtime and `AzureWebJobsStorage` with little benefit for an HTTP/SSE API plus one timer.
2. **Storage:** use domain/provider abstractions. Filesystem is the single-process local application-data provider; Azure Blob remains supported. Azurite is transitional, chiefly for the existing Functions host. MinIO/S3 may be added where deployment needs justify it, but is not the immediate local replacement.
3. **Denial state:** use a separate time-window store, such as SQLite locally and Azure Table/Redis/relational storage elsewhere.
4. **Database:** isolate connection, schema discovery, dialect guidance, validation, limits, and execution. MySQL is first; PostgreSQL follows the same contracts.
5. **AI:** chat completion and text embeddings are two independently configurable services. They may use different providers, hosts, models, credentials, timeouts, and Colab runtime sessions. Colab-hosted endpoints are temporary development/test providers only.
6. **Compliance:** controls are deployment-specific. Deployments processing PHI must meet applicable HIPAA safeguards, but SystemIQ itself is not healthcare-specific. Local/Colab data must be synthetic, de-identified, or explicitly approved.

### Infrastructure-first delivery direction

Infrastructure portability is the first engineering workstream, but Kubernetes-compatible preparation and actual Kubernetes deployment are separate gates. From the beginning, the application must use portable hosting, external configuration, provider abstractions, container-compatible process behavior, and no mandatory Azure infrastructure. The final portable target remains React plus an ASP.NET Core API plus an idempotent one-shot worker; Azure Functions may remain only for temporary parity and migration verification.

Actual Kubernetes deployment must not begin until the portable application starts successfully, uses local/non-Azure durable storage, connects to MySQL, exposes the required APIs, and passes the base local smoke path. Kubernetes is not a way to deploy the existing broken Azure Functions dependency graph. The target eliminates `AzureWebJobsStorage` as a mandatory runtime dependency.

### Approved temporary development inference topology

```text
SystemIQ
  |
  +--> Colab Chat Service
  |      POST /v1/chat/completions
  |
  +--> Colab Embedding Service
         POST /v1/embeddings
```

The services may run in separate Colab sessions or accounts. Each provider, base URL, model, credential/token, timeout, and applicable dimension/version setting is external configuration. Tunnel URLs and credentials must never be hard-coded or committed. Colab is not a production inference platform, and only synthetic, de-identified, or explicitly approved data may be sent to it.

## Requirements

### Query, glossary, and governance

#### REQ-001 — Natural-language answer and actual data
- **Must | High | Risk:** valid-looking model SQL may be semantically wrong.
- AC-001-1: Given an authorized user selects a healthy configured connection, when a natural-language question is submitted, then SystemIQ retrieves grounded schema/glossary context, generates dialect-correct SQL, validates and executes it read-only, and returns actual rows plus an answer.
- AC-001-2: Given a valid query returns no rows, when it completes, then a clear no-results response is returned.
- AC-001-3: Given grounding, AI, validation, or execution fails, when SystemIQ responds, then it identifies the safe failure category without secrets or restricted schema.

#### REQ-002 — Authorized provider-configured connections
- **Must | Medium | Risk:** catalog/policy mistakes may expose or hide a connection.
- AC-002-1: Given multiple providers are configured, when a user opens the selector, then only permitted non-secret connection metadata appears.
- AC-002-2: Given no connection is permitted, when the app loads, then chat is disabled with an explanatory state.
- AC-002-3: Given invalid configuration or an unresolved secret, when validation runs, then the connection is unavailable without exposing the secret.
- AC-002-4: Given the local MySQL catalog and access policy are configured, when the real local application loads, then the permitted MySQL connection appears and can be selected without Azure identity or infrastructure.

#### REQ-003 — Incremental streaming
- **Should | Medium | Risk:** a broken stream can appear complete.
- AC-003-1: Given chunks arrive, when answering, then the client displays them through the documented stream contract.
- AC-003-2: Given the stream ends prematurely, when handled, then the answer is marked incomplete and not persisted as successful.

#### REQ-004 — Isolated durable chat history
- **Must | Medium | Risk:** races or key defects can leak or lose history.
- AC-004-1: Given prior successful messages, when that user reselects that connection, then the selected provider restores them.
- AC-004-2: Given a connection switch precedes a read completion, when the stale read resolves, then it cannot overwrite current state.
- AC-004-3: Given a failed turn, when later model context is built, then that failed output is excluded.

#### REQ-005 — Configurable glossary per connection/domain
- **Must | High | Risk:** stale or ambiguous mappings can select wrong objects.
- AC-005-1: Given any supported domain, when curated, then business terms, synonyms, descriptions, table mappings, column mappings, relationship hints, and join guidance are editable per connection without code.
- AC-005-2: Given the same term differs across connections, when queried, then only the selected connection's meaning is used.
- AC-005-3: Given no curated entry, when grounding runs, then provider schema may be used but no healthcare/demo default is injected.

#### REQ-006 — Authorized glossary administration
- **Must | Medium | Risk:** authorization or concurrent overwrite can damage grounding.
- AC-006-1: Given a missing entry, when a curator selects a schema object, then an editable provider-derived default appears.
- AC-006-2: Given no curator permission, when an admin endpoint is called, then it returns 403 and is audited.
- AC-006-3: Given concurrent edits, when a version conflict occurs, then no silent overwrite happens.

#### REQ-007 — Traceable feedback review
- **Must | Medium | Risk:** multi-step writes can duplicate or partially complete.
- AC-007-1: Given negative feedback, when accepted, then a review records connection, matched terms, provider/model, database provider, and comment without secrets.
- AC-007-2: Given processor retry, when repeated, then at most one active review exists per feedback ID.
- AC-007-3: Given resolution succeeds, when the inbox reloads, then the item is absent and the resolution remains auditable.

#### REQ-008 — Connection/table/column access policy
- **Must | High | Risk:** policy, claim, or SQL-analysis defects can expose data.
- AC-008-1: Given policy denies an object, when a question requires it, then execution is blocked.
- AC-008-2: Given indirect access via join, subquery, alias, view, or dialect syntax, when validated, then denied objects remain blocked.
- AC-008-3: Given policy changes, when the next request arrives, then current policy applies without redeployment.

#### REQ-009 — Durable fail-closed denial audit
- **Must | High | Risk:** a no-op local audit hides defects; fail-closed outages reduce availability.
- AC-009-1: Given a denial, when it occurs, then identity, connection, request/SQL reference, reason, timestamp, and correlation ID are durably recorded.
- AC-009-2: Given the mandatory audit write fails, when a request would proceed, then it is blocked.
- AC-009-3: Given filesystem audit is selected, when restarted, then records remain and duplicate names cannot overwrite them.

#### REQ-010 — Provider-neutral denial rate limiting
- **Should | High | Risk:** inconsistent state weakens limits; strict thresholds block valid users.
- AC-010-1: Given threshold denials occur in a rolling window, when another request arrives, then it is rate-limited as configured.
- AC-010-2: Given local use without Azure Table, when restarted, then the local implementation preserves window behavior.

#### REQ-011 — Failure isolation and bounded retries
- **Must | Medium | Risk:** provider errors can leak detail or poison context.
- AC-011-1: Given AI/embedding unavailability, when requested, then a safe degraded/retry response is returned.
- AC-011-2: Given prior failure, when context is assembled, then failed output is not trusted context.
- AC-011-3: Given bounded retries, when they occur, then SQL and successful turns are not duplicated.

#### REQ-012 — Deployment-specific data protection
- **Must | High | Risk:** regulated deployments may have inadequate controls or telemetry redaction.
- AC-012-1: Given declared controls, when data is stored/transmitted, then provider configuration meets encryption, retention, residency, backup, and access requirements.
- AC-012-2: Given prompts, SQL, rows, identity, or answers are processed, when telemetry emits, then credentials and protected data are redacted.
- AC-012-3: Given local/Colab work, when data is prepared, then only synthetic, de-identified, or explicitly approved data is used.

#### REQ-013 — Quality measured by provider and dialect
- **Must | Medium | Risk:** self-selected feedback is not correctness ground truth.
- AC-013-1: Given feedback exists, when reported, then participation and database/dialect/provider/model dimensions accompany the rating.
- AC-013-2: Given a representative baseline, when targets are approved, then dataset, target, cadence, and regression threshold are recorded.

#### REQ-014 — Cross-dialect read-only enforcement
- **Must | High | Risk:** regex-only validation can miss provider-specific mutation.
- AC-014-1: Given SQL for any provider, when validated, then only its allowed read-only form proceeds.
- AC-014-2: Given `INSERT`, `UPDATE`, `DELETE`, `DROP`, `ALTER`, `CREATE`, `TRUNCATE`, multiple statements, provider-specific mutation, or an unsafe procedure/execution path, when validated, then it is blocked and audited.
- AC-014-3: Given application validation fails, when mutation reaches the database, then the database identity lacks permission.
- AC-014-4: Given generated SQL, when safety validation runs, then dialect-aware parsing plus application policy, row limits, and execution timeout are enforced and regex is never the sole control.

### Local development and provider portability

#### REQ-015 — Azure-independent local profile
- **Must | High | Risk:** startup may look healthy while a request path still reaches Azure.
- AC-015-1: Given no Azure login/subscription, when the local profile starts, then frontend, backend, storage, denial state, MySQL, and AI are usable without an Azure Storage account.
- AC-015-2: Given J1 runs, when complete, then actual MySQL rows return with no requirement for an Azure subscription, Azure login, Azure Storage/Blob/Table, Key Vault, Azure SQL, Azure OpenAI, or Entra ID.
- AC-015-3: Given missing local configuration/dependency, when readiness runs, then an actionable redacted error appears before user traffic.
- AC-015-4: Given the documented local profile, when its smoke path runs, then the real frontend and backend complete the path; canned, static, or demo-endpoint answers do not satisfy it.

#### REQ-016 — Storage-provider abstraction
- **Must | High | Risk:** atomicity/concurrency differences can corrupt history, glossary, feedback, or audit.
- AC-016-1: Given `FileSystem` in single-process local mode, when domain storage runs, then safe keys and atomic writes persist data under a restricted configured root.
- AC-016-2: Given `AzureBlob`, when the same operations run, then Blob remains supported without Azure types in domain contracts.
- AC-016-3: Given conformance tests, when not-found, overwrite, conflict, prefix list, delete, malformed data, cancellation, and outage run, then documented outcomes match.
- AC-016-4: Given multiple replicas/Kubernetes, when pod-local filesystem is configured as durable storage, then validation rejects it.
- AC-016-5: Given a storage provider is selected, when chat history, glossary, feedback, audit, or persisted RAG/index state is accessed, then each uses the provider abstraction; Azurite is optional transitional compatibility tooling and MinIO is not required.

#### REQ-017 — Database capability/provider architecture
- **Must | High | Risk:** lowest-common-denominator design can hide unsafe capability gaps.
- AC-017-1: Given a provider catalog entry, when used, then connection handling, schema discovery, SQL dialect, validation, execution, limits, and provider capabilities use its registered boundaries.
- AC-017-2: Given a future relational provider implements contracts/tests, when registered, then chat, glossary, policy, and storage need no redesign.
- AC-017-3: Given a required capability is absent, when validated, then the connection is rejected rather than using SQL Server behavior.

#### REQ-018 — MySQL first-class support
- **Must | High | Risk:** version/mode/quoting/date/metadata differences affect correctness.
- AC-018-1: Given a configurable MySQL connection and least-privilege read-only credentials, when schema discovery runs, then tables, columns, primary keys, foreign keys, and relationships are returned without SQL Server/Azure SQL fallback logic.
- AC-018-2: Given representative natural-language questions, when SQL is generated, then MySQL dialect, identifier quoting, date/function guidance, joins, and query limits are applied and actual MySQL rows are returned.
- AC-018-3: Given mutation or a row/time-limit violation, when validation/execution runs, then the query is blocked or bounded and the outcome is auditable.
- AC-018-4: Given a MySQL query is cancelled or exceeds its configured timeout, when execution stops, then cancellation propagates safely and the connection remains usable.

#### REQ-019 — Additive PostgreSQL support
- **Should | High | Risk:** source PostgreSQL may be confused with a future internal-state database.
- AC-019-1: Given the provider seams, when PostgreSQL is added, then its schema, quoting, limits, functions, validation, and execution use the same boundaries.
- AC-019-2: Given PostgreSQL is both source and internal state, when configured, then connections, privileges, lifecycle, and health are separate.

#### REQ-020 — Configurable chat-completion provider
- **Must | High | Risk:** models differ in context, streaming, safety, accuracy, latency, and cost.
- AC-020-1: Given a chat provider, when starting, then provider, base URL, model/deployment, credential/auth mode, timeout, and limits are independently configured and validated without hard-coded Azure or Colab values.
- AC-020-2: Given Azure OpenAI, when selected, then its adapter and approved credential flow remain supported.
- AC-020-3: Given the development baseline, when the configured temporary Colab/OpenAI-compatible service is selected, then SystemIQ calls `POST /v1/chat/completions` without requiring Azure and classifies the endpoint as non-production.
- AC-020-4: Given provider/model change, when evaluation misses safety/accuracy thresholds, then it cannot be promoted.
- AC-020-5: Given chat and embedding services use different URLs, models, tokens, or Colab sessions/accounts, when a question is processed, then each service uses only its own configuration and credential.

#### REQ-021 — Independent embedding/RAG provider
- **Must | High | Risk:** dimension/model/index mismatch silently harms grounding.
- AC-021-1: Given an embedding provider, when indexing/retrieving, then provider, base URL, model, credential/token, dimensions, timeout, and index/model/content versions are configuration-driven and not embedded in domain logic.
- AC-021-2: Given the approved development baseline, when embeddings are requested, then the configurable Google Colab/OpenAI-compatible endpoint exposes `POST /v1/embeddings` using `Qwen/Qwen3-Embedding-8B`; another configured provider/model can replace it later without redesign.
- AC-021-3: Given chat and embeddings differ, when processing, then separate providers, hosts, models, credentials, and Colab runtime sessions work without a shared Azure account.
- AC-021-4: Given a natural-language question, when RAG runs, then it embeds the question; searches connection-specific schema/glossary embeddings; retrieves relevant tables, columns, relationships/join hints, business terms, and synonyms; filters results by selected connection and authorization policy; and grounds the chat model for dialect-correct SQL generation.
- AC-021-5: Given index content is stored, when inspected, then metadata records connection, source type, schema version/hash, glossary version, embedding model, embedding dimension, and content/index version.
- AC-021-6: Given the embedding model, dimension, schema, glossary, or indexed content becomes incompatible, when retrieval is requested, then the stale index is identified and re-indexing is required/reported; any configured lexical degradation is explicit.

#### REQ-022 — Typed configuration and secret hygiene
- **Must | High | Risk:** raw JSON/connection strings can leak or fail late.
- AC-022-1: Given startup, when binding storage provider, database provider/connections, catalog, policy, chat provider/URL/model, embedding provider/URL/model/dimensions, authentication, deployment mode, health, audit, schedule, and telemetry, then combinations are typed and validated before readiness.
- AC-022-2: Given credentials, API keys, tokens, passwords, or connection strings, when supplied, then they come from environment/configuration secret sources or a secret manager—not source, PRD examples, images, Kubernetes manifests, logs, or frontend configuration.
- AC-022-3: Given Kubernetes/container deployment, when secrets are delivered, then mounted/injected secrets work independently of Key Vault while Key Vault remains optional.
- AC-022-4: Given diagnostics, when configuration is reported, then secrets are redacted and errors actionable.
- AC-022-5: Given temporary Colab chat or embedding inference, when endpoint URLs or tokens change, then operators update external configuration independently without rebuilding the application or committing tunnel URLs/credentials.

#### REQ-023 — Container/Kubernetes-compatible runtime target
- **Must | High | Risk:** host conversion may change routes, SSE, auth, audit, or duplicate timers.
- AC-023-1: Given ASP.NET Core contract tests, when compared to approved Functions behavior, then routes, codes, SSE, cancellation, auth, and fail-closed audit are equivalent.
- AC-023-2: Given one-shot/CronJob feedback processing, when duplicate starts occur, then idempotency prevents duplicate active reviews.
- AC-023-3: Given API containers, when probes run, then liveness tests process health; readiness validates configuration/state without restart storms during external AI/source-DB outages.
- AC-023-4: Given telemetry, when emitted, then it is correlated, provider-neutral, and redacted; Application Insights is optional.
- AC-023-5: Given multiple replicas, when serving, then no durable state uses pod-local storage and concurrency budgets protect DB/AI providers.
- AC-023-6: Given Kubernetes deployment is proposed, when the delivery gate is evaluated, then the portable ASP.NET Core application must already start, use non-Azure/local storage, connect to MySQL, expose required APIs, and pass the base local smoke path before container/Kubernetes configuration, secret injection, persistent external state, CronJob scheduling, probes, networking, graceful shutdown, and scaling are accepted.

## Acceptance Criteria Summary

| REQ | ACs | REQ | ACs | REQ | ACs |
|---|---:|---|---:|---|---:|
| 001 | 3 | 009 | 3 | 017 | 3 |
| 002 | 4 | 010 | 2 | 018 | 4 |
| 003 | 2 | 011 | 3 | 019 | 2 |
| 004 | 3 | 012 | 3 | 020 | 5 |
| 005 | 3 | 013 | 2 | 021 | 6 |
| 006 | 3 | 014 | 4 | 022 | 5 |
| 007 | 3 | 015 | 4 | 023 | 6 |
| 008 | 3 | 016 | 5 | **Total** | **81** |

All 23 requirements have acceptance criteria; all 20 Must requirements have at least two. Total acceptance criteria: 81.

## Dependency Map

| Requirement | Depends on |
|---|---|
| 001 | 002, 005, 008, 014, 017, 020, 021 |
| 002 | 008, 017, 022 |
| 003 | 001, 020 |
| 004 | 001, 016 |
| 005 | 016, 017, 021 |
| 006 | 005, 008 |
| 007 | 004, 005, 006, 016, 023 |
| 008 | 002, 017, 022 |
| 009 | 008, 016 |
| 010 | 009, 016 |
| 011 | 001, 020, 021 |
| 012 | 016, 017, 020, 021, 022, 023 |
| 013 | 001, 007, 017, 020, 021 |
| 014 | 008, 009, 017 |
| 015 | 016, 018, 020, 021, 022, 023 |
| 016 | 022 |
| 018 | 017, 022 |
| 019 | 017, 022 |
| 020 | 022 |
| 021 | 005, 022 |
| 023 | 016, 022 |

No circular dependency is identified. Local critical path: REQ-022 → 016/017/020/021/023 → 018 → 015 → 001.

## Delivery Gates

### P0 — portable infrastructure foundation and working local application

Remove mandatory Azure infrastructure dependencies; establish portable hosting/configuration, filesystem durable storage, SQLite denial-window state, provider-neutral database seams, MySQL connectivity and schema discovery, local catalog/policy/glossary, and a working frontend/backend with the base NL-to-SQL smoke path. Azurite may be used only for transitional Functions compatibility testing. The real application—not a canned demo endpoint—must start and exercise the smoke path before Kubernetes deployment work begins.

### P1 — complete local AI/RAG integration

Complete the independently configured OpenAI-compatible chat-completion and embedding integrations, use the Qwen3 embedding development baseline, build and version the connection-specific schema/glossary index, enforce authorization-filtered RAG, generate dialect-correct MySQL SQL, validate and execute it read-only, and return actual MySQL rows. Capture API/security parity and advance the ASP.NET Core API plus one-shot-worker migration sufficiently to prove the portable local path.

### P2 — Kubernetes deployment and production portability

Containerize and deploy the proven React frontend, ASP.NET Core API, and idempotent one-shot processor/CronJob with external configuration, injected secrets, persistent external state, probes, networking/Gateway/SSE behavior, graceful shutdown, backup/recovery, concurrency budgets, and scaling controls. Pod-local filesystem is not durable state for multi-replica deployments. PostgreSQL is the next provider/extensibility proof after MySQL; S3/MinIO is added only if a future deployment needs it.

## First Demonstrable Working Milestone

The first stakeholder demonstration is complete only when the real application performs this sequence without mandatory Azure infrastructure:

1. The application starts successfully and the frontend communicates with the backend.
2. A configured MySQL connection is visible and selectable.
3. SystemIQ discovers its tables, columns, primary keys, foreign keys, and relationships.
4. Chat and embedding providers are configured independently.
5. A user asks a simple natural-language question.
6. The query is embedded and relevant authorized schema/glossary context is retrieved.
7. The chat service generates valid MySQL `SELECT` SQL from that context.
8. Dialect-aware safety and access-policy validation succeeds.
9. SQL executes through a least-privilege read-only MySQL account with limits and cancellation.
10. Actual database rows and the grounded natural-language answer are returned.

Static, canned, or demo-only answers do not satisfy this milestone.

## Risks and Dependencies

| ID | Risk / mitigation |
|---|---|
| R-01 Critical | Auth/audit regression during refactor → host-independent contract and failure-injection tests |
| R-02 Critical | Unsafe/wrong cross-dialect SQL → parser/validator, DB read-only roles, limits, evaluation sets |
| R-03 Critical | State migration loss/duplication → versioned migration, checksums, concurrency, rollback |
| R-04 Critical | Dev bypass in production → environment guardrails and deployment tests |
| R-05 Critical | Secret/data leakage → secret references, redaction tests, scanning, least privilege |
| R-06 High | Transport parity mistaken for model quality → promotion thresholds per model/dialect |
| R-07 High | Embedding/index incompatibility → persist model/dimension/content version and re-index |
| R-08 High | Filesystem used with replicas → capability validation rejects it |
| R-09 High | Duplicate background work → atomic claims, idempotency, overlap prevention |
| R-10 High | Kubernetes cost exceeds value → platform-ownership gate and proof first |
| R-11 High | SSE/proxy/rollout failure → real-controller tests and drain settings |
| R-12 High | Scaling overwhelms DB/AI → pooling, budgets, backpressure, saturation metrics |
| R-13 High | Colab instability/data exposure → non-production, synthetic data, artifact handoff |

| ID | Status | Baselined decision / remaining input | Gate |
|---|---|---|---|
| D-01 | **BASELINED FOR IMPLEMENTATION** | MySQL is the first local provider. Supported local version, synthetic test schema/data, connection details, and least-privilege read-only credentials remain environment configuration unless implementation evidence establishes them. | P0/SC-1 |
| D-02 | **BASELINED FOR IMPLEMENTATION; exact model ID EXTERNAL/PENDING** | Development chat uses a temporary Google Colab OpenAI-compatible `POST /v1/chat/completions` endpoint with an independently configurable model. The exact Qwen chat model ID awaits stakeholder/repository evidence but does not block the provider contract. | P0/P1 |
| D-03 | **BASELINED FOR IMPLEMENTATION** | The embedding-model/provider decision is closed: `Qwen/Qwen3-Embedding-8B` through a configurable Colab/OpenAI-compatible `POST /v1/embeddings` endpoint. Vector/index parameters and evaluation evidence remain implementation outputs. | P1 |
| D-04 | **CLOSED (product decision)** | Filesystem is the local durable application-storage baseline; SQLite is the local denial-window-state baseline. Lifecycle details are implementation/configuration concerns. | P0 |
| D-05 | **BASELINED FOR IMPLEMENTATION** | Generic connection catalog and access policy remain configuration-driven and must be implemented and tested for the local MySQL connection. | P0 |
| D-06 | **BASELINED FOR IMPLEMENTATION** | Target host is ASP.NET Core API plus one-shot worker. Azure Functions remains temporary only for contract parity/migration; cutover/rollback details belong in the implementation plan. | P0/P1 |
| D-07 | **EXTERNAL/PENDING** | Kubernetes platform ownership and standards are required only before P2 deployment. | P2 only |
| D-08 | **EXTERNAL/PENDING** | Per-deployment compliance, retention, residency, backup, and review ownership are required before production use. | Production only |
| D-09 | **EXTERNAL/PENDING** | PostgreSQL versions, priority, and evaluation data are selected after the MySQL baseline. | After MySQL/P2 |
| D-10 | **EXTERNAL/PENDING** | Colab access and artifact-handoff rules are required when that approved temporary development path is used. Only synthetic, de-identified, or explicitly approved data is permitted. | Colab use only |

## Implementation Gaps and Blocker Status

There are no unresolved product-decision blockers for TRD synchronization or implementation planning. The following are prioritized implementation gaps, not reasons to keep the PRD in Draft:

1. Build the complete real-backend local MySQL NL-to-SQL path.
2. Decouple Blob/Table and Functions-host storage concerns; retain Azurite only for transitional compatibility.
3. Replace SQL Server-specific discovery, dialect, validation, and execution behavior with provider boundaries and first-class MySQL support.
4. Implement independent chat and embedding registrations plus complete RAG.
5. Supply a working local MySQL catalog, access policy, and connection-specific generic glossary.
6. Consolidate typed configuration, secret injection, validation, and redaction.
7. Add dialect-aware parsing, policy validation, limits, timeout, cancellation, and read-only database credentials.
8. Stage the ASP.NET Core API and one-shot-worker extraction with Functions parity verification.
9. Make feedback processing portable and idempotent.
10. Complete generic authentication, health, telemetry, graceful shutdown, networking, and scaling boundaries before P2.

The exact chat model ID (D-02) remains a genuine external confirmation item, but it does not block implementation of the independently configurable chat-provider architecture. D-07 and D-08 block only Kubernetes/production promotion, not P0/P1 implementation.

## Ensemble Refinement Findings

The stakeholder request explicitly selected and resolved all findings, so no additional interview was necessary.

| # | Baseline finding | v1.0.3 disposition |
|---:|---|---|
| 1 | Product tied to Clinical Operations/named healthcare DBs | Domain-neutral SystemIQ; demos are configuration only |
| 2 | Azure services described as fixed | Current and optional-provider target separated |
| 3 | No Azure-independent end-to-end criterion | J1, SC-1, REQ-015, P0 |
| 4 | No local storage/provider contract | REQ-016; filesystem + Azure Blob; denial state separate |
| 5 | SQL Server-only despite MySQL/PostgreSQL direction | REQ-017–019 |
| 6 | Azure OpenAI-only; unused embedding config | REQ-020–021 |
| 7 | Colab role/risks undefined | Development-only with data and handoff rules |
| 8 | No explicit no-hardcoded-secret/startup validation | REQ-022 |
| 9 | PRD contradicted Kubernetes host recommendation | ASP.NET Core/CronJob target in REQ-023 |
| 10 | HIPAA wording made product healthcare-specific | Deployment-specific controls retained |

## Ensemble PRD Readiness Review

**Baseline:** v1.0.3, 23 requirements, 71 ACs, 4.75/5. The generic direction was established, but delivery sequencing, independent development AI baselines, RAG details, and D-01 through D-06 dispositions were not sufficiently resolved for implementation approval.

| Dimension | Score | Rationale |
|---|---:|---|
| Completeness | 5.00 | Generic NL-to-SQL, Azure-independent local operation, storage, MySQL-first extensibility, independent AI services, complete RAG, safety, portable host, Kubernetes gates, risks, and dependencies are covered |
| Testability | 5.00 | All 23 requirements have Given/When/Then criteria, including the real MySQL/RAG milestone and explicit P0/P1/P2 gates |
| Clarity | 5.00 | Provider responsibilities, Colab development topology, Qwen embedding baseline, delivery sequence, and dependency dispositions are explicit |
| Feasibility | 4.50 | Product choices are achievable and implementation-ready; the work remains substantial and the exact chat model ID plus later platform/production inputs remain external |
| **Overall** | **4.88 PASS** | Implementation Ready; ready for TRD synchronization and P0/P1 implementation |

D-01 through D-06 are now closed or baselined for implementation. No remaining product decision blocks TRD synchronization or P0/P1 implementation. The exact chat model ID remains pending stakeholder confirmation but is configuration, not an architectural blocker. Kubernetes/production promotion remains correctly gated by P0/P1 evidence and D-07/D-08.

## Changelog

- **v1.0.0 (2026-07-29):** initial DataIQ clinical/Azure PRD with 14 requirements.
- **v1.0.1 (2026-07-29):** resolved curator, schedule, audit-retention, rate-limit, and accuracy clarifications.
- **v1.0.2 (2026-07-29):** expanded edge cases, risks, constraints, blockers, and observability.
- **v1.0.3 (2026-08-27):** Ensemble refinement using the local-infrastructure and Kubernetes analyses. Repositioned the product as generic SystemIQ; generalized REQ-001–014; added REQ-015–023 for local portability, storage, database providers, MySQL, PostgreSQL, chat, embeddings/RAG, secrets/configuration, and ASP.NET Core/Kubernetes compatibility; added delivery gates, risks, dependencies, blockers, and readiness review. Scale depth increased from LIGHT to MEDIUM. Readiness is 4.75 because required provider and host capabilities are not implemented.
- **v1.0.4 (2026-08-28):** targeted stakeholder refinement preserving all 23 requirements. Made infrastructure portability the first workstream while gating actual Kubernetes deployment behind a working portable local application; strengthened Azure-independent local execution, filesystem/SQLite baselines, MySQL-first provider behavior, independent Colab chat/embedding services, `Qwen/Qwen3-Embedding-8B`, complete connection/policy-scoped RAG, generic glossary, dialect-aware SQL safety, typed configuration/secrets, ASP.NET Core/one-shot-worker direction, the real MySQL end-to-end milestone, and P0/P1/P2 delivery gates. D-01 through D-06 were closed or baselined; readiness increased from 4.75 to 4.88 and status changed from Draft to Implementation Ready.
