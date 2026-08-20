---
Document ID: TRD-2026-001
Label: trd-dataiq-sql-assistant
PRD Reference: PRD-2026-001
Version: 1.0.0
Status: Draft
Date: 2026-07-29
Kind: trd
Design Readiness Score: 4.63 (PASS)
---

# DataIQ SQL Assistant — Technical Requirements Document

Source PRD: `docs/PRD/PRD-2026-001-dataiq-sql-assistant.md` (v1.0.0, Draft, Readiness 4.88, PASS)

## PRD Validation Summary

- All required PRD sections present (Product Summary, User Analysis, Key User Journeys, Goals/Non-Goals, Product Success Criteria, Requirements by Feature Area, Acceptance Criteria, Technical Constraints, Dependency Map, Rollout and Product Observability, Readiness Scorecard, Open External Dependencies & Blockers).
- REQ-001 through REQ-014 sequential, no gaps. All ACs in Given/When/Then format. Every Must requirement has ≥2 ACs.
- PRD Readiness Score 4.88 ≥ 4.0 → proceeded without a gate warning.
- Source PRD status is **Draft**. This TRD may be reviewed in parallel, but implementation requires explicit approval of both documents or direct user authorization.
- Capability reuse check: `docs/TRD/` did not exist in this repository prior to this document; no foundational TRDs were available to cross-reference.
- Repository validation confirmed the referenced Azure Functions, React, Semantic Kernel, Blob Storage, glossary, RBAC, and feedback components exist in this checkout. Audit logging, repeated-denial rate limiting, and accuracy reporting components were not found.

## Framing: Brownfield With Most Requirements Already Implemented

This is not a from-scratch build. Per the PRD's own Feasibility rationale, REQ-001 through REQ-008, REQ-011, REQ-012 (partially), and REQ-014 are already implemented and running in the live codebase. Architecture alternatives were therefore **not** re-litigated for the whole product — that would mean redesigning working code. Alternatives were presented and decided only for the three requirements with no implementation today: **REQ-009** (audit logging), **REQ-010** (rate-limiting), **REQ-013** (accuracy baseline reporting).

A second, related finding from this pass: two already-"complete" requirements have real, previously-undocumented test gaps. `DataIqAccessPolicyService` — the single highest-risk requirement in the PRD (REQ-008) — has no dedicated test file. Read-only SQL enforcement (REQ-014) doesn't either. Both are addressed as real tasks below, not waved off as pre-existing.

## Architecture Decision

### Existing Architecture (documented, not redesigned)

| Concern | Component | Requirements |
|---|---|---|
| NL question → SQL → answer | `SemanticKernelChatResponder`, `DatabasePlugin` (Semantic Kernel + Azure OpenAI) | REQ-001, REQ-011 |
| Connection selection | `DatabaseConnectionCatalog`, `ConnectionsFunctions` | REQ-002 |
| Streaming | `ChatStreamFunctions` (SSE-style frames), client `streamChat()` reader | REQ-003 |
| Chat history | `BlobChatHistoryStore`, `ChatHistoryFunctions`, `useChatController.selectConnection` (staleness-guarded) | REQ-004 |
| Business glossary | `BusinessGlossaryMatcher`, `BusinessGlossaryLoader`, `BlobBusinessGlossaryStore` | REQ-005 |
| Glossary admin editor | `GlossaryAdminFunctions`, `GlossaryAdmin.tsx`, `GlossaryEntityHelper` | REQ-006 (blocked by B-1) |
| Feedback review queue | `GlossaryFeedbackProcessingService`, `GlossaryFeedbackQueueService`, Feedback Inbox tab | REQ-007 (deep-link gap) |
| RBAC | `DataIqAccessPolicyService`, `DataIqAccessPolicyProvider` | REQ-008 |
| Read-only enforcement | SQL validation in `DatabasePlugin` / `DataIqAccessPolicyService` | REQ-014 |
| Encryption at rest/transit | Inherited by default from Azure Storage, Key Vault, Azure SQL | REQ-012 (partial) |

### New Components — Alternatives Considered and Chosen

**REQ-009 — Audit logging of denied access**

| Option | Description | Verdict |
|---|---|---|
| App Insights only | Reuse the existing OpenTelemetry/App Insights pipeline entirely | Rejected — cannot synchronously confirm durability before the request completes, so it does not actually satisfy the fail-closed AC as written. |
| App Insights + Blob backstop | Structured events via App Insights for KQL querying, plus a synchronous Blob write purely to satisfy fail-closed | Rejected for v1 — two write paths to maintain for a LIGHT-depth effort; deferred as a possible fast-follow once real audit volume exists. |
| **Blob Storage, synchronous write (chosen)** | New `audit-log/` prefix in a dedicated container, same `BlobContainerClientFactory` pattern as chat history/glossary/feedback. Write is awaited before the denial response returns. | **Chosen.** Minimal new infrastructure, genuinely satisfies fail-closed by construction (unhandled write exception blocks the request), consistent with every other storage integration already in this codebase. |

**REQ-010 — Rate-limiting of repeated denials**

| Option | Description | Verdict |
|---|---|---|
| In-memory (`ConcurrentDictionary`) | Track denial timestamps per user in process memory | Rejected — incorrect once scaled beyond one Function instance or after a cold restart; a real correctness gap, not just a simplification. |
| Azure Cache for Redis | Purpose-built sliding-window counters, sub-millisecond reads | Rejected for v1 — introduces a wholly new Azure service for a LIGHT-depth solo effort; correctness benefit doesn't yet justify the new dependency. |
| **Azure Table Storage (chosen)** | New table in the storage account already used for Blob. Denial timestamps recorded per user; rolling-window count checked before processing. | **Chosen.** No new Azure service, correct across instances and restarts — the actual requirement (correctness) without the actual cost (new dependency). |

**REQ-013 — Accuracy baseline reporting**

| Option | Description | Verdict |
|---|---|---|
| Scheduled daily snapshot | Timer-triggered function computing and persisting a daily accuracy snapshot, building a trend automatically | Rejected for v1 — automates a trend before it's established that a trend (vs. a one-time baseline) is what's needed; the PRD itself frames this as "establish baseline first." |
| **On-demand admin endpoint (chosen)** | Curator-triggered endpoint computes thumbs-up rate + feedback coverage from existing chat-history blobs on request | **Chosen.** Matches the PRD's own framing exactly; avoids building recurring-metric infrastructure before there's a demonstrated need. |

## System Architecture

**Data flow for the three new components:**
- **Audit logging:** `DataIqAccessPolicyService` denial path → `AuditLogService.LogDeniedAccessAsync` (synchronous, awaited) → Blob container `audit-log/{userOid}/{timestamp}_{guid}.json` → on write failure, exception propagates → denial response replaced with a distinct system-error message.
- **Rate limiting:** Same denial path → `AccessDenialRateLimiter` records a timestamp row in Azure Table Storage → before processing any new request, `IsRateLimitedAsync(userOid)` counts rows in the rolling window → if ≥ N, request is rejected with a rate-limit message before reaching SQL generation.
- **Accuracy reporting:** Curator calls `GET /api/admin/accuracy-report` → `AccuracyReportingService` enumerates chat-history session blobs (reusing the existing `GetSessionsModifiedSinceAsync`-style enumeration, but unbounded by watermark for a full report) → computes and returns thumbs-up rate, thumbs-down rate, feedback coverage.

**Configuration additions:** `AUDIT_LOG_BLOB_CONTAINER_URI`, `RATE_LIMIT_DENIAL_COUNT` (default 5), `RATE_LIMIT_WINDOW_MINUTES` (default 10) — all follow the existing env-var + Key Vault secret pattern already used for `CHAT_HISTORY_BLOB_CONTAINER_URI`/`GLOSSARY_BLOB_CONTAINER_URI`.

**New package dependency:** `Azure.Data.Tables` (rate limiter only — audit logging reuses the existing `Azure.Storage.Blobs` dependency already present).

## Master Task List

### PR 1: Close known implementation gaps + backfill critical test coverage
**Shippable State:** Once the `DataIqGlossaryEditor` role is provisioned in Azure AD (external blocker B-1, tracked separately in the PRD), curators can navigate directly from a Feedback Inbox item to the exact glossary term it references, instead of only switching connections. Independently of that external blocker, the highest-risk requirement in the product (RBAC enforcement) gains its first automated test coverage.

#### TRD-001: Add unit tests for `DataIqAccessPolicyService` RBAC allow/deny paths [satisfies REQ-008]
- Estimate: 3h
- Implementation AC:
  - Given a connection/table/column denied by policy, when access is evaluated, then the request is denied.
  - Given a connection/table/column allowed by policy, when access is evaluated, then the request proceeds.
  - Given generated SQL references a denied table or column, when `EnsureGeneratedSqlAllowedAsync` runs, then an `UnauthorizedAccessException` is thrown.

#### TRD-002: Add unit tests for read-only SQL enforcement [satisfies REQ-014]
- Estimate: 2h
- Implementation AC:
  - Given generated SQL is a `SELECT` statement, when validated, then it is permitted.
  - Given generated SQL contains `INSERT`, `UPDATE`, `DELETE`, or DDL, when validated, then execution is blocked.

#### TRD-003: Extend `infra/grant-deployment-permissions.ps1` to create/assign the `DataIqGlossaryEditor` Azure AD app role [satisfies REQ-006]
- Estimate: 2h
- Implementation AC:
  - Given the script is run by an Owner/User Access Administrator, when it completes, then the `DataIqGlossaryEditor` app role exists in the Azure AD app registration and is assignable.

#### TRD-003-TEST: Manual verification of the extended provisioning script [verifies TRD-003] [satisfies REQ-006] [depends: TRD-003]
- Estimate: 1h
- Implementation AC:
  - Given the extended script is run against a test tenant, when it completes, then the role is visible and assignable in the Azure Portal.

#### TRD-004: Add a business-term-to-table resolution helper for Feedback Inbox deep-linking [satisfies REQ-007]
- Estimate: 3h
- Implementation AC:
  - Given a feedback item names matched business term(s), when resolved against the connection's glossary, then the specific table(s) whose curated entry uses that term are returned.
  - Given no curated entry matches the term, then the resolver returns an empty result rather than throwing.

#### TRD-004-TEST: Unit test for the resolution helper [verifies TRD-004] [satisfies REQ-007] [depends: TRD-004]
- Estimate: 1h
- Implementation AC:
  - Given a glossary with a curated entry for a known business term, when resolved, then the correct table identifier is returned.

#### TRD-005: Wire the Feedback Inbox "Edit terms" action to the resolution helper [satisfies REQ-007] [depends: TRD-004]
- Estimate: 2h
- Implementation AC:
  - Given a curator clicks "Edit terms" on a feedback item, when the resolver returns a specific table, then the Tables tab opens with that table pre-selected, not just the connection switched.

#### TRD-005-TEST: Manual verification of end-to-end deep-linking [verifies TRD-005] [satisfies REQ-007] [depends: TRD-005]
- Estimate: 1h
- Implementation AC:
  - Given negative feedback tied to a known glossary term, when a curator opens Feedback Inbox and clicks "Edit terms," then the correct table's editor opens directly.

**PR 1 total: 8 tasks, 15h**

### PR 2: Audit logging of denied access
**Shippable State:** Every RBAC-denied query attempt is durably recorded with user, question, connection, and timestamp. If that record can't be written, the request fails with a distinct system-error message instead of the denial silently proceeding unaudited.

#### TRD-006: Add `AUDIT_LOG_BLOB_CONTAINER_URI` configuration [satisfies ARCH]
- Estimate: 1h
- Implementation AC:
  - Given the setting is present, when the service starts, then a real `AuditLogService` is registered; given it's absent, then the service still registers but is configured to fail closed (see TRD-007).

#### TRD-007: Create `AuditLogEntry` record and `AuditLogService` [satisfies REQ-009] [depends: TRD-006]
- Estimate: 3h
- Implementation AC:
  - Given a denial occurs, when `LogDeniedAccessAsync` is called, then a blob is written synchronously containing user, question, connection, denial reason, and timestamp.
  - Given the container is unconfigured, when the service is invoked, then it throws rather than silently succeeding — no safe no-op mode, by design, to preserve fail-closed semantics even in misconfigured environments.

#### TRD-007-TEST: Unit tests for `AuditLogService` [verifies TRD-007] [satisfies REQ-009] [depends: TRD-007]
- Estimate: 2h
- Implementation AC:
  - Given a successful write, when read back, then the entry's fields match what was logged.
  - Given a simulated write failure, when `LogDeniedAccessAsync` is called, then the exception propagates rather than being swallowed.
  - Given the service is constructed unconfigured, when called, then it throws immediately.

#### TRD-008: Wire `AuditLogService` into `DataIqAccessPolicyService` denial paths [satisfies REQ-009] [depends: TRD-007]
- Estimate: 2h
- Implementation AC:
  - Given a query is denied, when the denial is returned to the user, then the audit entry has already been written (awaited, not fire-and-forget).
  - Given the audit write fails, when the request completes, then the user sees a distinct "system error, try again shortly" message — not the same text as a normal access-denied message, so the two failure modes aren't confused.

#### TRD-008-TEST: Integration test for the wired audit path [verifies TRD-008] [satisfies REQ-009] [depends: TRD-008]
- Estimate: 2h
- Implementation AC:
  - Given a denied query, when processed, then exactly one correctly-shaped audit entry exists.
  - Given a forced audit-write failure, when the same denial occurs, then the request is blocked with the distinct system-error message, not the normal denial message.

**PR 2 total: 5 tasks, 10h**

### PR 3: Rate-limiting of repeated denials
**Shippable State:** A user who receives 5 access-denials within a rolling 10-minute window is rate-limited from further requests until the window elapses, instead of being able to keep probing indefinitely.

#### TRD-009: Add `Azure.Data.Tables` dependency and rate-limit configuration [satisfies ARCH]
- Estimate: 1h
- Implementation AC:
  - Given `RATE_LIMIT_DENIAL_COUNT`/`RATE_LIMIT_WINDOW_MINUTES` are unset, when the service starts, then defaults of 5/10 apply.

#### TRD-010: Create `AccessDenialRateLimiter` backed by Azure Table Storage [satisfies REQ-010] [depends: TRD-008, TRD-009]
- Estimate: 4h
- Implementation AC:
  - Given a denial occurs, when recorded, then a timestamped row is written for that user.
  - Given a user has fewer than N denial rows within the rolling window, when checked, then they are not rate-limited.
  - Given a user has ≥ N denial rows within the rolling window, when checked, then they are rate-limited.

#### TRD-010-TEST: Unit tests for the rate limiter [verifies TRD-010] [satisfies REQ-010] [depends: TRD-010]
- Estimate: 2h
- Implementation AC:
  - Given 4 denials in the window (N=5), when checked, then not rate-limited.
  - Given 5 denials in the window, when checked, then rate-limited.
  - Given denials outside the rolling window, when checked, then they do not count toward the threshold.

#### TRD-011: Wire the rate-limit check into the request pipeline [satisfies REQ-010] [depends: TRD-010]
- Estimate: 3h
- Implementation AC:
  - Given a user is rate-limited, when they submit any chat or admin request, then it is rejected before SQL generation begins, with a message stating when they can retry.

#### TRD-011-TEST: Integration test for end-to-end rate-limiting [verifies TRD-011] [satisfies REQ-010] [depends: TRD-011]
- Estimate: 2h
- Implementation AC:
  - Given 5 denials occur in quick succession, when a 6th request is submitted within the window, then it is rejected with the rate-limit message rather than processed.

**PR 3 total: 5 tasks, 12h**

### PR 4: Accuracy baseline reporting
**Shippable State:** A curator can request an on-demand accuracy report and see the current thumbs-up rate and feedback coverage across all chat history, instead of accuracy being an unmeasured, unverifiable claim.

#### TRD-012: Create `AccuracyReportingService` [satisfies REQ-013]
- Estimate: 4h
- Implementation AC:
  - Given chat history sessions with a mix of rated/unrated messages, when computed, then thumbs-up rate, thumbs-down rate, and feedback-coverage percentage are all correct.
  - Given no chat history exists, when computed, then a well-defined zero/empty state is returned, not an error.

#### TRD-012-TEST: Unit tests for the reporting service [verifies TRD-012] [satisfies REQ-013] [depends: TRD-012]
- Estimate: 2h
- Implementation AC:
  - Given a constructed set of rated/unrated messages, when the report is computed, then rate and coverage match hand-calculated expected values.

#### TRD-013: Add `GET /api/admin/accuracy-report` endpoint [satisfies REQ-013] [depends: TRD-012]
- Estimate: 2h
- Implementation AC:
  - Given an authorized curator calls the endpoint, when it returns, then the response contains rate, coverage, and the date range covered.
  - Given an unauthorized user calls the endpoint, when it returns, then the response is 403.

#### TRD-013-TEST: Integration test for the endpoint [verifies TRD-013] [satisfies REQ-013] [depends: TRD-013]
- Estimate: 1h
- Implementation AC:
  - Given a curator with the editor role, when calling the endpoint, then a 200 with a correctly-shaped report is returned.
  - Given a user without the role, when calling the endpoint, then a 403 is returned.

**PR 4 total: 4 tasks, 9h**

### PR 5: Defense-in-depth hardening
**Shippable State:** If the SQL-generation validator is ever bypassed, a data-modifying statement now fails at the database level with a clear permission error instead of silently succeeding. Encryption settings across storage, secrets, and database connections are confirmed and documented against the HIPAA mandate.

#### TRD-014: Provision a read-only SQL login/role for app database connections [satisfies REQ-014]
- Estimate: 2h
- Implementation AC:
  - Given the app's configured database credential, when a write statement is attempted directly against it, then the database itself rejects it independent of app-level validation.

#### TRD-014-TEST: Manual verification of database-level enforcement [verifies TRD-014] [satisfies REQ-014] [depends: TRD-014]
- Estimate: 1h
- Implementation AC:
  - Given the app's read-only credential, when a manual `INSERT`/`UPDATE`/`DELETE` is attempted using it, then the database returns a permission error.

#### TRD-015: Document and verify HIPAA encryption-at-rest/in-transit configuration [satisfies REQ-012]
- Estimate: 2h
- Implementation AC:
  - Given Blob Storage, Key Vault, and Azure SQL connections, when reviewed, then encryption-at-rest is confirmed enabled and connection strings enforce TLS.

#### TRD-015-TEST: Manual verification checklist recorded as a compliance note [verifies TRD-015] [satisfies REQ-012] [depends: TRD-015]
- Estimate: 1h
- Implementation AC:
  - Given each service's settings, when checked against the checklist, then each item is confirmed and recorded with evidence (screenshot or config reference).

**PR 5 total: 4 tasks, 6h**

**Grand total: 26 tasks, 52h. No task exceeds 4h — no oversized-task flags.**

## Sprint Planning

*(Informational grouping only — not parsed by implement-trd-beads.)*

### Sprint 1
PR 1 (gap closure + REQ-008/014 test backfill) + PR 2 (audit logging). ~25h.

### Sprint 2
PR 3 (rate-limiting) + PR 4 (accuracy reporting). ~21h.

### Sprint 3
PR 5 (hardening). ~6h. Can be pulled earlier if compliance review requires it sooner than the feature work.

## Quality Requirements

- **Security:** All new Blob/Table Storage access uses the existing managed-identity/SAS credential pattern — no new credential types introduced. Rate-limiter and audit-log failures must never silently degrade to "allow" — both fail closed by design.
- **Performance:** No formal latency target exists yet (per the PRD's Technical Constraints section) — new components should not introduce a *second* synchronous Storage round-trip per request beyond what's already awaited for RBAC evaluation, to avoid materially increasing per-request latency, but no numeric SLA is being invented here.
- **Testing:** Every implementation task has a paired verification task (automated where feasible, manual where it isn't — e.g., Azure AD role provisioning). Minimum: happy path + one failure/edge case per task, per the PRD's own AC standard.
- **Observability:** Audit-write failures and rate-limit triggers should be logged via the existing `ILogger`/OpenTelemetry pipeline (already conditionally wired) so they're visible in Application Insights when configured, even though it isn't the fail-closed system of record.

## Acceptance Criteria Traceability

| REQ-NNN | Description | Implementation | Test |
|---|---|---|---|
| REQ-001 | NL question → answer | `SemanticKernelChatResponder.cs`, `DatabasePlugin.cs` (existing) | `SystemPromptTemplateTests.cs`, `EmbeddingSchemaSelectorTests.cs`, `ConversationQuestionContextTests.cs` (existing, partial — no automated end-to-end test of the full answer path; relies on manual verification) |
| REQ-002 | Connection selection | `DatabaseConnectionCatalog.cs`, `ConnectionsFunctions.cs` (existing) | **Gap: no automated test identified. Not addressed in this TRD pass** (Low complexity/risk per PRD) |
| REQ-003 | Streaming | `ChatStreamFunctions.cs`, client `streamChat()` (existing) | **Gap: no automated test identified. Not addressed in this TRD pass** |
| REQ-004 | Chat history persistence | `BlobChatHistoryStore.cs`, `useChatController.selectConnection` (existing, incl. 2 stale-response fixes) | `ConversationQuestionContextTests.cs` (existing, partial) |
| REQ-005 | Curated business glossary | `BusinessGlossaryMatcher.cs`, `BusinessGlossaryLoader.cs`, `BlobBusinessGlossaryStore.cs` (existing) | `BusinessGlossaryTests.cs`, `BusinessGlossaryCollisionAuditTests.cs`, `BusinessGlossaryLoaderStoreTests.cs` (existing, strong) |
| REQ-006 | Glossary admin editor | `GlossaryAdminFunctions.cs`, `GlossaryAdmin.tsx` (existing, blocked by B-1) + TRD-003, TRD-004, TRD-005 | `GlossaryEntityHelperTests.cs` (existing) + TRD-003-TEST, TRD-004-TEST, TRD-005-TEST |
| REQ-007 | Feedback review queue | `GlossaryFeedbackProcessingService.cs`, `GlossaryFeedbackQueueService.cs` (existing) + TRD-004, TRD-005 | `GlossaryFeedbackProcessingTests.cs` (existing) + TRD-004-TEST, TRD-005-TEST |
| REQ-008 | RBAC enforcement | `DataIqAccessPolicyService.cs` (existing) + TRD-001 | TRD-001 (**new — closes a previously undocumented gap on the highest-risk requirement**) |
| REQ-009 | Audit logging | TRD-006, TRD-007, TRD-008 (new) | TRD-007-TEST, TRD-008-TEST |
| REQ-010 | Rate-limiting | TRD-009, TRD-010, TRD-011 (new) | TRD-010-TEST, TRD-011-TEST |
| REQ-011 | Failure recovery | `ChatContextFilter.cs` (existing) | `ChatContextFilterTests.cs` (existing, strong — 12 tests incl. the exact production regression) |
| REQ-012 | HIPAA/PHI compliance | Azure default encryption (existing, inherited) + TRD-015 | TRD-015-TEST |
| REQ-013 | Accuracy measurement | TRD-012, TRD-013 (new) | TRD-012-TEST, TRD-013-TEST |
| REQ-014 | Read-only enforcement | `DatabasePlugin.cs` SQL validation (existing) + TRD-002, TRD-014 | TRD-002, TRD-014-TEST |

**Traceability check: 14 requirements covered, 0 uncovered, 0 orphaned `[satisfies]` annotations.**

## Adversarial Review

### Architecture Self-Critique

1. **Availability coupling:** Fail-closed audit logging (TRD-008) makes every denial path dependent on Blob Storage availability. A regional Storage outage would turn clean 403s into 500s across the whole app, not just the audit trail. **Resolution:** the distinct system-error message (already specified in TRD-008's AC) prevents users from confusing this with an actual permissions problem; recommend alerting on audit-write failures via the existing OpenTelemetry pipeline so this is caught operationally before it's discovered via user complaints.
2. **Storage transaction volume:** Both the audit logger and rate limiter write to Azure Storage on every denial. A UI bug causing repeated denied calls from one user could generate meaningful transaction volume/cost. **Resolution:** flagged as a future optimization (debounce identical repeated denials within a short window) — not blocking for this LIGHT-depth pass, since the rate limiter itself will cap the blast radius after 5 denials regardless.

### Task Coverage Analysis

1. **Initial gap:** REQ-001 through REQ-005, REQ-008, and REQ-011 had zero `[satisfies]` references in the first draft of this TRD, since no new code is needed for them. A naive coverage check would have read this as "uncovered." **Resolution:** the Acceptance Criteria Traceability matrix above references existing implementation/test files directly for these, rather than fabricating no-op tasks or forcing a non-shippable PR purely for traceability annotations.
2. **PR 5 shippability tension:** A defense-in-depth backstop (read-only DB credential) is, in the success case, invisible to any user — arguably infrastructure-only. **Resolution:** the Shippable State was worded to describe the actual observable behavior change (a bypassed validator now fails at the database level with a permission error, rather than succeeding) rather than the absence of change in the normal case — this is a legitimate, if edge-case, user-observable outcome, not pure scaffolding.

### Dependency and Estimate Review

- **Chain depth:** TRD-009 → TRD-010 → TRD-011 → TRD-011-TEST is a 4-hop sequential chain within PR 3, at the "flag for review" threshold. **Resolution:** TRD-010-TEST and TRD-011 both depend only on TRD-010 (not on each other), so they can be worked concurrently by two people or interleaved — the estimate-hours chain looks fully serial, but the actual wall-clock critical path is shorter if parallelized.
- **Estimate consistency:** New-service-creation tasks (TRD-007, TRD-010, TRD-012) are estimated 3-4h; endpoint-wiring tasks (TRD-008, TRD-011, TRD-013) are 2-3h. Consistent with each other and grounded in the size of directly analogous existing services (`GlossaryFeedbackProcessingService`, `GlossaryFeedbackQueueService`) already built in this codebase.

### Testability Review

No testability issues found. Every Implementation AC above states a specific, checkable condition (exact counts, specific field presence, specific status codes) rather than subjective language ("fast," "user-friendly"). No AC in this TRD relies on unmeasurable judgment calls.

## Design Readiness Gate

| Dimension | Score | Rationale |
|---|---|---|
| Architecture completeness | 4/5 | All three new components have defined interfaces, data flows, and integration points. Existing-architecture documentation is summary-level (appropriate for LIGHT depth) rather than exhaustively diagrammed. |
| Task coverage | 5/5 | All 14 REQ-NNN IDs covered (0 uncovered, 0 orphaned annotations) after correcting the initial gap identified in adversarial review; every user-facing task has a paired test task. |
| Dependency clarity | 4.5/5 | Dependencies explicit and acyclic; one 4-hop chain identified with a documented parallelization mitigation. |
| Estimate confidence | 5/5 | No task exceeds 4h; estimates for similar task shapes are consistent and grounded in directly comparable existing code in this repository. |
| **Overall** | **4.63** | **PASS** |

## Changelog

### v1.0.0 — 2026-07-29

Initial TRD generated from `PRD-2026-001-dataiq-sql-assistant.md` v1.0.0 using the supplied prior TRD only as read-only reference material.

- Revalidated the reference design against the current repository and documented the existing architecture for REQ-001–008, 011, 012 (partial), and 014 without redesign.
- Presented and resolved 3 architecture-alternative decisions for genuinely new components: audit logging (Blob, synchronous, fail-closed), rate-limiting (Azure Table Storage), accuracy reporting (on-demand endpoint).
- Identified and closed a real test-coverage gap: `DataIqAccessPolicyService` (REQ-008, highest-risk requirement in the PRD) and read-only SQL enforcement (REQ-014) had no dedicated automated tests; both now have real tasks (TRD-001, TRD-002).
- Generated 26 tasks (15 implementation + 11 test/verification) across 5 shippable PRs, 52h total. (4 implementation tasks — TRD-001, TRD-002, TRD-006, TRD-009 — are themselves test-writing or config-only tasks with no further pairing needed.)
- Design Readiness Gate: 4.63 (PASS).
