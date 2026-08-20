---
Document ID: PRD-2026-001
Label: prd-dataiq-sql-assistant
Version: 1.0.0
Status: Draft
Date: 2026-07-29
Scale Depth: LIGHT
Total Requirements: 14
Readiness Score: 4.88 (PASS)
---

# DataIQ SQL Assistant — Product Requirements Document

## PRD Health Summary

- **Requirements by priority:** Must (12), Should (2), Could (0), Won't (0)
- **AC coverage:** 14/14 requirements have acceptance criteria (100%) — 27 ACs total; every Must has ≥2 ACs
- **Risk flags:** 12 of 14 requirements flagged (all Medium and High complexity items; the 2 Low-complexity items are unflagged by design)
- **Dependency count:** 10 dependent requirements comprising 12 cross-requirement dependency edges
- **Ambiguity markers:** 0 open — all 5 `[NEEDS CLARIFICATION]` items resolved in v1.0.1
- **Open external blockers:** 2 (see Open External Dependencies & Blockers)

## Product Summary

**Problem:** Non-technical business users — Clinical Operations, Care Management, and Business Analysts — need timely access to data held in the MP3 and BabyTrax databases, but getting answers today requires SQL syntax, internal schema knowledge, table relationships, joins, and filter logic they don't have. This leaves them dependent on developers/analysts to write ad hoc queries, or on a previously-tried self-service tool that had its own limitations. A prior architecture (Blazor Server monolith with a CLI fallback mode) has already been retired in favor of the current Azure Functions + React design this PRD describes.

**Solution:** DataIQ SQL Assistant lets these users ask questions in plain English against a selected database connection, translates the question into SQL using an LLM (Azure OpenAI via Semantic Kernel) grounded in a curated per-connection business glossary, executes it read-only against Azure SQL, and streams back a natural-language answer plus the underlying data — all under RBAC enforcement consistent with a hard HIPAA/PHI compliance mandate.

**Value proposition:** Removes the schema/SQL literacy barrier for self-service data access, while keeping access control, auditability, and answer quality (via a continuously-curated glossary) as first-class, not bolted on.

**Target users:** One combined persona — the **non-technical business user** (Clinical Operations, Care Management, Business Analysts) — treated uniformly for this PRD, since their core need (ask in plain English, get a trustworthy answer) is shared even though the specific questions they ask differ.

## User Analysis

**Persona: Non-technical business user**
- **Pain today:** Cannot write SQL; doesn't know table/column names or how tables relate; a prior self-service tool didn't fully solve this (specific limitation not captured in detail — deprioritized during elicitation).
- **Goal:** Ask a question in ordinary language and get a correct, understandable answer without waiting on a developer.
- **Success metric:** Percentage of questions answered correctly without developer intervention, measured via the existing thumbs-up/thumbs-down feedback signal (see REQ-013).

## Key User Journeys

### Journey 1: Ask a permitted data question
1. The user signs in and sees only database connections allowed by their current access policy.
2. The user selects a connection and asks a question in ordinary language.
3. DataIQ grounds the request using the connection's glossary, generates and validates read-only SQL, and executes it.
4. The user sees a streaming natural-language answer and the underlying rows, or an explicit no-results response.
5. The user optionally provides thumbs-up or thumbs-down feedback.

### Journey 2: Resume work on a connection
1. The user returns to DataIQ and selects a previously used connection.
2. DataIQ restores that user's conversation for the selected connection without leaking or displaying history from another connection.
3. The user asks a follow-up question using the restored conversation as context.

### Journey 3: Encounter restricted or unavailable data
1. The user asks for data outside their effective policy, directly or through an indirect join.
2. DataIQ blocks the generated query before execution, records the denied attempt, and explains that the requested data is unavailable under the user's policy.
3. Repeated denials trigger the configured rate limit without exposing restricted schema details.

### Journey 4: Curate terminology after poor feedback
1. A user gives an answer thumbs-down and optionally supplies a reason or comment.
2. The daily or on-demand processor creates a review item tied to the matched glossary terms.
3. An authorized curator reviews the item, updates the relevant glossary entry when needed, and resolves the item.

## Goals and Non-Goals

**Goals:**
- Let non-technical users self-serve data questions in plain English.
- Keep answer quality high and improving over time via a curated, feedback-driven business glossary.
- Enforce RBAC and HIPAA/PHI-consistent access control on every query, with audit logging of denied attempts.
- Recover gracefully from AI-service unavailability and SQL-generation failures without compounding errors across retries.

**Non-Goals (this release):**
- Data-modifying operations (INSERT/UPDATE/DELETE/DDL) — the assistant is read-only by design (REQ-014).
- Automatic multi-attempt retry of a failed query without user action — the accepted safety net is a user-initiated retry (per elicitation), not system auto-retry.
- Alternate AI-service fallback/failover — "service unavailable, try later" messaging is the accepted behavior; no secondary model or queue-and-retry path is in scope.
- **Deployment, operations, and monitoring requirements** — out of scope for this document. **Rationale:** this PRD is deliberately scoped to product behavior (what the system does for users) at LIGHT depth for a solo effort; infrastructure provisioning, CI/CD, alerting, and runbooks are a separate concern with a different audience and lifecycle. **Tracking home:** deployment readiness is tracked operationally through the repository's `infra/` assets (Bicep template, `grant-deployment-permissions.ps1`) and the Azure DevOps pipeline definition, not through requirements in this PRD. **Known open blocker:** production is currently non-functional pending a Key Vault permission grant — see Open External Dependencies & Blockers.

## Product Success Criteria

The baseline period begins only after B-1 and B-3 are resolved and the product is available to a representative pilot group. Unless the product owner approves a different interval, report the first complete 30-day period and use it to set numeric targets rather than inventing pre-launch thresholds.

| ID | Outcome | Measure | Initial decision rule | Requirement coverage |
|---|---|---|---|---|
| SC-1 | Users can self-serve useful answers | Rated-answer thumbs-up rate, reported with feedback participation rate | Establish a 30-day baseline, then obtain product-owner approval for a numeric target and review cadence | REQ-001, REQ-013 |
| SC-2 | Users are less dependent on developers for routine questions | Percentage of pilot questions completed without developer/analyst intervention | Instrument or sample during the baseline period; set a target after the current intervention rate is known | REQ-001, REQ-002, REQ-005 |
| SC-3 | Access controls prevent unauthorized retrieval | Count of executed queries that reference denied connections, tables, or columns | Must remain zero; denied attempts are expected and measured separately | REQ-008, REQ-009, REQ-014 |
| SC-4 | Glossary curation closes the quality loop | Pending feedback age and percentage of negative-feedback items resolved | Establish baseline volume and curator capacity; approve a service-level target after ownership is assigned | REQ-006, REQ-007 |
| SC-5 | Recoverable failures do not poison subsequent attempts | Percentage of user-initiated retries whose model context excludes the failed turn | 100% for recognized failure fallbacks | REQ-011 |

### Goal Traceability

| Goal | Requirements | Success criteria |
|---|---|---|
| Plain-English self-service | REQ-001–REQ-004 | SC-1, SC-2 |
| High and improving answer quality | REQ-005–REQ-007, REQ-013 | SC-1, SC-4 |
| RBAC and HIPAA/PHI-consistent access | REQ-008–REQ-010, REQ-012, REQ-014 | SC-3 |
| Graceful recovery without compounded failures | REQ-011 | SC-5 |

## Technical Constraints & Dependencies

**Platform and stack (fixed):**
- Backend: .NET 9, Azure Functions isolated worker with ASP.NET Core integration, hosted on an EP1 Premium plan
- Frontend: React + Vite single-page application, hosted on Azure Static Web Apps
- AI: Azure OpenAI (chat completion deployment for SQL generation and answering; `text-embedding-3-small` for schema-relevance selection) orchestrated via Microsoft Semantic Kernel
- Identity: Azure AD — MSAL.js in the SPA, JWT bearer validation in the API (paired SPA + API app registrations)
- Storage: Azure Blob Storage for chat history, the writable business glossary, and the feedback queue
- Secrets: Azure Key Vault for connection strings, AI credentials, and the RBAC access policy
- Data: Azure SQL — the MP3 and BabyTrax databases, accessed **read-only** (see REQ-014)

**Compliance constraint (hard mandate):**
- HIPAA/PHI compliance is mandatory, not optional. This constrains data residency, encryption, access control, audit logging, and retention across every requirement in this document (see REQ-008, REQ-009, REQ-012).

**Constraints that apply but are not yet quantified:**
These are known to matter for this product, but no formal target has been set. Each should be established during technical design rather than assumed:
- **Data residency / region** — PHI is expected to remain within an approved Azure region/geography. *Specific region constraint not yet documented.*
- **Query latency** — users have an implicit tolerance ceiling for response time, particularly for complex multi-join questions. *No target defined.*
- **Concurrent user scale** — affects Functions plan sizing and Azure OpenAI token/request quota. *Expected concurrency not defined.*

## Requirements by Feature Area

### Natural Language Query & Data Access

#### REQ-001: Users can ask questions about a connected database in plain English and receive a natural-language answer plus the underlying data
- **Priority:** Must | **Complexity:** Medium
- **[RISK: LLM SQL generation is not deterministic — the same question has been observed to succeed on one attempt and fail on identical retries. Answer availability is therefore variable, not guaranteed.]**
- AC-001-1: Given a user has selected a database connection, when they submit a plain-English question, then the system returns a natural-language answer and, where applicable, the underlying result rows.
- AC-001-2: Given a question that matches no data in the connected database, when the query executes successfully but returns zero rows, then the user sees a clear "no matching records" message rather than an empty or broken response.

#### REQ-002: Users can select which database connection to query against
- **Priority:** Must | **Complexity:** Low
- AC-002-1: Given a user has access to one or more connections, when they open the connection selector, then only the connections their RBAC policy permits are listed (see REQ-008).
- AC-002-2: Given a signed-in user has zero permitted connections under their access policy, when they open the application, then they see an explanatory empty state stating that no connections are available to them and indicating who to contact for access, with the chat input disabled rather than appearing usable.

#### REQ-003: Responses stream incrementally rather than blocking until complete
- **Priority:** Should | **Complexity:** Medium
- **[RISK: If a stream breaks mid-answer, the user may be left with a partial or truncated response that reads as complete — potentially misleading for data-driven decisions.]**
- AC-003-1: Given a question is being answered, when the AI generates the response, then chunks are displayed to the user as they arrive rather than only after the full answer is ready.

#### REQ-004: Chat history persists per user, per connection, so users can resume a prior session
- **Priority:** Must | **Complexity:** Medium
- **[RISK: Asynchronous loads racing against connection switches can show one connection's data under another. Two such stale-response defects have already been found and fixed; the pattern recurs wherever a per-connection fetch is added.]**
- *(Added during adversarial review — Issue 1: gap identified between REQ-011's "fresh context after failure" behavior and the absence of any requirement establishing that history exists at all.)*
- AC-004-1: Given a user has previously asked questions on a connection, when they return and re-select that connection, then their prior conversation (messages, not diagnostics) is restored.
- AC-004-2: Given a user switches away from a connection before its history finishes loading, when the load resolves after the switch, then it does not overwrite the now-selected connection's state (stale-response protection).

### Business Glossary & Curation

#### REQ-005: The system maintains a curated business glossary mapping business vocabulary and synonyms to underlying schema (tables/columns), per connection
- **Priority:** Must | **Complexity:** Medium
- **[RISK: Generic auto-generated glossary terms can collide with curated ones, producing false ambiguity prompts or steering SQL to the wrong table. Observed in practice with `members`/`email` and with three separate entries all named `appointments`.]**
- AC-005-1: Given a user's question contains wording matched in the glossary, when SQL is generated, then the matched business term's related tables/columns/join hints are included in the generation context.
- AC-005-2: Given two glossary entries could both plausibly match a question's wording, when one entry's schema already covers the attribute in question (e.g., a specific table/column), then the system does not surface a redundant, unrelated entry as a competing match.

#### REQ-006: An authorized curator can browse, edit, and add glossary entries through an admin UI
- **Priority:** Must | **Complexity:** Medium
- **[RISK: The `DataIqGlossaryEditor` app role is not provisioned in Azure AD, so this requirement is unreachable by real users in production today despite being implemented. See Open External Dependencies & Blockers.]**
- *(Depends on REQ-005.)*
- AC-006-1: Given a curator opens the glossary editor for a connection, when they select a table with no existing curated entry, then the system shows an auto-generated default (business term, description, related tables/columns derived from the live schema) that they can edit and save.
- AC-006-2: Given a curator is not assigned the `DataIqGlossaryEditor` role, when they attempt to access the glossary admin endpoints, then the request is denied (403).
- **Role ownership:** The `DataIqGlossaryEditor` role is held by the developer/admin maintaining DataIQ initially, with planned handoff to a designated business owner once curation ownership is formally established.
- **Implementation note:** This app role does not yet exist in Azure AD, so no user currently holds it in production. Until it is created and assigned, the glossary editor is reachable only via a local-development bypass and is effectively unavailable to real users.

#### REQ-007: Negative feedback on an answer tied to a matched glossary term is queued for curator review, with the matched term(s) recorded for traceability
- **Priority:** Must | **Complexity:** Medium
- **[RISK: The feedback loop is only partially implemented today — the queue and inbox UI exist, but the term-to-table deep-link and full curator workflow are not complete.]**
- *(Refined during adversarial review — Issue 2: added the requirement to record which glossary term(s) matched a turn, without which curator review has nothing concrete to act on.)*
- AC-007-1: Given a user gives negative (thumbs-down) feedback on an answer that matched one or more glossary terms, when the feedback is processed, then a review item is created recording the question, the matched term(s), and the feedback reason/comment.
- AC-007-2: Given a review item has been created, when a curator resolves it, then it no longer appears in the pending feedback inbox.
- **Processing schedule:** The feedback-processing job runs on a **daily** schedule. Curation is not time-sensitive, so a daily cadence keeps noise and cost low; curators can additionally trigger processing on demand when they need results immediately. Feedback may therefore sit up to 24 hours before appearing in the inbox unless manually triggered.

### Security & Compliance

#### REQ-008: The system enforces role-based access control (RBAC) restricting which connections, tables, and columns a given user may query
- **Priority:** Must | **Complexity:** High
- **[RISK: Misconfiguration of the RBAC policy could expose PHI. This is the highest-stakes requirement in the PRD.]**
- *(Refined during adversarial review — Issue 4: added AC-008-3 covering policy-change propagation timing.)*
- AC-008-1: Given a user's RBAC policy denies a table, column, or connection, when they ask a question that would require that data, then the generated SQL is rejected before execution and the user receives a message that the data is not available under their access policy.
- AC-008-2 (negative test): Given a user attempts to phrase a question to indirectly retrieve denied data (e.g., via a join path not explicitly blocked), when the generated SQL is validated, then it is still rejected if it references any denied table or column.
- AC-008-3: Given a user's RBAC policy changes (e.g., a role is revoked) during an active session, when they submit their next request, then the updated policy applies immediately — no re-login or session invalidation is required.

#### REQ-009: The system logs denied/blocked access attempts for audit purposes
- **Priority:** Must | **Complexity:** Medium
- **[RISK: The fail-closed behavior in AC-009-2 makes application availability dependent on the audit store — an audit-logging outage degrades the app rather than only degrading observability. This is a deliberate trade of availability for auditability under the HIPAA mandate.]**
- AC-009-1: Given a query is rejected due to an RBAC violation or disallowed SQL pattern, when the rejection occurs, then an audit log entry is recorded including the user, the attempted question/SQL, the connection, and the timestamp.
- AC-009-2: Given the audit log write itself fails, when a request would otherwise be processed, then the request is blocked (fail-closed) rather than proceeding unaudited — no access decision is made without a corresponding audit record.
- **Retention:** Audit log retention follows **Progeny Health's existing audit/PHI retention policy** rather than a DataIQ-specific period, so this product inherits whatever the organization already mandates.
- **Review ownership:** The **Security/Compliance team** is responsible for reviewing these logs.
- **Assumption to verify:** This resolution assumes an organizational retention policy exists and explicitly covers application-level audit logs. If it does not, a DataIQ-specific period must be set — HIPAA §164.316(b)(2)(i) documentation retention (6 years) is the conventional fallback for PHI-adjacent audit trails.

#### REQ-010: The system rate-limits a user who exceeds a defined number of access-denied responses within a rolling time window
- **Priority:** Should | **Complexity:** High
- **[RISK: The 5-denials-in-10-minutes default is an untuned starting point — too strict blocks legitimate users still learning their access boundaries, too loose doesn't deter probing. Mitigated by making both values configurable so they can be tuned from real usage data without a redeploy.]**
- *(Narrowed during adversarial review — Issue 3: scoped from open-ended "suspicious pattern detection" to a concrete, measurable trigger.)*
- AC-010-1: Given a user receives **5** access-denied responses within a rolling **10-minute** window, when the 5th denial occurs, then further requests from that user are rate-limited until the window elapses.
- **Configurability:** Both the denial count (N = 5) and window duration (T = 10 minutes) are configuration values, changeable without redeployment.

### Reliability

#### REQ-011: Failed queries do not compound across retries — the AI-unavailable case shows a clear message, and a failed attempt is excluded from conversation context so the next attempt starts fresh
- **Priority:** Must | **Complexity:** Low
- AC-011-1: Given the AI service is unavailable or rate-limited, when a user submits a question, then they receive a clear message to wait and try again, rather than a silent failure or crash.
- AC-011-2: Given a question previously failed (any recognized failure fallback message), when the user asks the same or a related question again, then the failed turn is excluded from the conversation context sent to the model, so the new attempt is not biased by the prior failure.

## Non-Functional Requirements

#### REQ-012: All PHI data access, storage, and transmission complies with HIPAA safeguards
- **Priority:** Must | **Complexity:** High
- **[RISK: Compliance is a hard mandate for this product; any gap here is a business-critical failure, not a quality issue.]**
- AC-012-1: Given data is stored (chat history, glossary, feedback queue) or transmitted (client-server, server-database), when at rest or in transit, then it is encrypted using the organization's approved standards.
- AC-012-2: Given a user accesses any data, when RBAC evaluates their request, then they receive only the minimum data necessary permitted by their policy (see REQ-008).

#### REQ-013: The system's answer accuracy is measurable via the existing feedback signal
- **Priority:** Must | **Complexity:** Medium
- **[RISK: Thumbs-up rate is a proxy, not ground truth — it reflects only answers users chose to rate. If participation is low or skewed (users more inclined to flag failures than successes), the measured rate may misrepresent real accuracy and mislead the target-setting in AC-013-2.]**
- *(Resolved during adversarial review — Issue 5: tied an otherwise unmeasurable requirement to the existing thumbs-up/thumbs-down mechanism.)*
- AC-013-1: Given users provide thumbs-up/thumbs-down feedback on answers, when accuracy is measured over a rolling period, then it is computed as the thumbs-up rate (or inverse thumbs-down rate) over that period.
- AC-013-2: Given the system has been in use for a defined baseline period, when the baseline thumbs-up rate has been established and reported, then a numeric accuracy target is set from that baseline — no fixed target percentage is committed before baseline data exists.
- **Measurement caveat:** The thumbs-up rate reflects only answers users actually rated. If feedback participation is low, the rate may not represent true accuracy across all questions asked, so feedback coverage should be reported alongside the rate.

#### REQ-014: The system never executes data-modifying SQL against connected databases
- **Priority:** Must | **Complexity:** Medium
- **[RISK: Enforcement is pattern/validation-based rather than guaranteed by database permissions alone. A novel or obfuscated SQL form could in principle evade the validator, so defense-in-depth via read-only database credentials is strongly advisable.]**
- AC-014-1: Given the AI generates a SQL statement, when it is validated before execution, then only read-only `SELECT` statements are permitted.
- AC-014-2 (negative test): Given the AI generates SQL containing `INSERT`, `UPDATE`, `DELETE`, or any DDL statement, when validation runs, then execution is blocked and the user receives an error rather than the statement running.

## Acceptance Criteria Summary

| REQ | Description | Priority | Complexity | Risk | AC Count |
|---|---|---|---|---|---|
| REQ-001 | Ask questions in plain English | Must | Medium | ⚠ | 2 |
| REQ-002 | Select database connection | Must | Low | — | 2 |
| REQ-003 | Streaming responses | Should | Medium | ⚠ | 1 |
| REQ-004 | Chat history persistence | Must | Medium | ⚠ | 2 |
| REQ-005 | Curated business glossary | Must | Medium | ⚠ | 2 |
| REQ-006 | Glossary admin editor | Must | Medium | ⚠ | 2 |
| REQ-007 | Feedback-driven review queue | Must | Medium | ⚠ | 2 |
| REQ-008 | RBAC enforcement | Must | High | ⚠ | 3 |
| REQ-009 | Audit logging of denied access | Must | Medium | ⚠ | 2 |
| REQ-010 | Rate-limit suspicious access patterns | Should | High | ⚠ | 1 |
| REQ-011 | Failure recovery / resilience | Must | Low | — | 2 |
| REQ-012 | HIPAA/PHI compliance | Must | High | ⚠ | 2 |
| REQ-013 | Accuracy measurement | Must | Medium | ⚠ | 2 |
| REQ-014 | Read-only enforcement | Must | Medium | ⚠ | 2 |

**Totals:** 27 ACs across 14 requirements. All 12 Must requirements have ≥2 ACs. Both Should requirements have ≥1 AC. Risk flags on all 12 Medium/High complexity requirements; the 2 Low-complexity requirements (REQ-002, REQ-011) are unflagged by design.

## Dependency Map

| REQ | Depends On | Notes |
|---|---|---|
| REQ-001 | REQ-002, REQ-005 | Needs a selected connection and glossary context to generate good SQL |
| REQ-002 | REQ-008 | Connection list itself must be RBAC-filtered |
| REQ-003 | REQ-001 | Streaming applies to the query-answering flow |
| REQ-004 | REQ-001 | History persistence wraps the Q&A flow |
| REQ-006 | REQ-005 | Editor operates on the glossary structure |
| REQ-007 | REQ-005, REQ-006 | Feedback loop deep-links into curated entries |
| REQ-009 | REQ-008 | Audit logging captures RBAC denials |
| REQ-010 | REQ-009 | Rate-limiting trigger is defined off audit/denial data |
| REQ-011 | REQ-001 | Failure recovery applies to the query-answering flow |
| REQ-013 | REQ-001 | Accuracy is measured from feedback on Q&A turns |

No circular dependencies identified.

## Rollout and Product Observability

1. **Pre-production validation:** Resolve B-1 and B-3; confirm B-2; verify that every pilot user sees only permitted connections and that database credentials are read-only.
2. **Controlled pilot:** Enable a representative group from the target persona. Collect question volume, rated-answer feedback, feedback participation, denied-attempt counts, failure categories, and curator-queue age.
3. **Baseline review:** After the first complete 30-day pilot period, review SC-1 through SC-5 with the product owner, Security/Compliance, and the glossary owner. Approve numeric targets for metrics that intentionally require a baseline.
4. **Expansion decision:** Expand beyond the pilot only if SC-3 and SC-5 meet their initial decision rules, no unresolved high-severity privacy or access-control finding exists, and owners accept targets and remediation plans for the remaining criteria.
5. **Ongoing review:** Report success metrics by connection and user cohort where permitted, without exposing PHI in telemetry. Track AI unavailability, SQL validation rejection, SQL execution failure, zero-result, and user-cancelled outcomes separately so a single aggregate error rate does not obscure causes.

This section defines product signals and release decisions. Infrastructure dashboards, alert thresholds, deployment mechanics, and runbooks remain outside this PRD and belong in the TRD and operational assets.

## Source-Grounding Notes

- The current `.sln`, `src/PH-DataIQ.Functions`, `client/`, `infra/main.bicep`, and `azure-pipelines.yml` establish Azure Functions plus React/Static Web Apps as the active architecture.
- The root README still describes the retired Blazor/console shape. Treat it as stale documentation for architecture decisions until it is reconciled; do not use it to override the current project structure.
- Requirements describing existing behavior are not evidence that the behavior is production-ready. The blockers below remain authoritative.

## Readiness Scorecard

| Dimension | Score | v1.0.1 | v1.0.0 | Rationale |
|---|---|---|---|---|
| Completeness | 5/5 | 4/5 | 4/5 | Improved. All feature areas covered; the deployment/ops exclusion now carries an explicit rationale and tracking home rather than a bare non-goal line; a Technical Constraints & Dependencies section documents the fixed stack, the HIPAA mandate, and the three constraints that apply but remain unquantified. |
| Testability | 5/5 | 4.5/5 | 4/5 | Improved. Every Must requirement now has ≥2 ACs including an edge case; the previously single-AC Musts (REQ-002, REQ-009) gained meaningful negative/edge criteria rather than filler. |
| Clarity | 5/5 | 5/5 | 4/5 | Maintained. No unresolved ambiguity markers; risk flags now make previously-implicit fragility explicit, reducing the chance two readers assume different reliability characteristics. |
| Feasibility | 4.5/5 | 5/5 | 5/5 | **Declined.** Not because the product got harder, but because this pass documented two real external blockers (unprovisioned Azure AD role, unverified retention policy) and eight concrete risks. Feasibility is no longer a clean 5 because REQ-006 is provably unreachable in production today and REQ-009's retention basis is unconfirmed. This is more accurate, not worse. |
| **Overall** | **4.88** | **4.63** | **4.25** | **PASS (improved +0.25)** |

**Note on the Feasibility decline:** the drop from 5.0 to 4.5 is deliberate and reflects better information, not regression. v1.0.0–1.0.1 scored Feasibility as a clean 5 on the basis that most requirements were already implemented. That was true but incomplete: "implemented" is not the same as "reachable by users," and REQ-006 is a concrete case where working code sits behind an unprovisioned role. Documenting that lowered the score while raising the document's accuracy.

## Open External Dependencies & Blockers

These items are outside the product's own codebase but block or qualify requirements in it. None can be resolved by further PRD refinement — each requires an action or confirmation by a named party.

| # | Blocker | Blocks | Owner | Status | Impact if unresolved |
|---|---|---|---|---|---|
| B-1 | The `DataIqGlossaryEditor` app role does not exist in Azure AD and is assigned to no one | REQ-006 (and therefore the curation half of REQ-007) | Azure AD administrator | Open | Glossary editor is unreachable in production. Implemented and working, but no real user can access it — currently only reachable via a local-development bypass. |
| B-2 | Progeny Health's organizational audit/PHI retention policy is unverified — existence and applicability to application-level audit logs not confirmed | REQ-009 retention basis | Security/Compliance team | Open | REQ-009's retention requirement has no confirmed authority behind it. If no such policy covers application logs, a DataIQ-specific period must be set (HIPAA §164.316(b)(2)(i) 6-year documentation retention is the conventional fallback). |
| B-3 | Production Function App requires a Key Vault `Key Vault Secrets User` role assignment for its managed identity | Entire product in production | Owner / User Access Administrator on `rg-SankalpAIPOC` | Open | The deployed backend cannot read its secrets and has been crash-looping since first deployment. Every requirement in this PRD is unverifiable in production until resolved. Remediation script exists at `infra/grant-deployment-permissions.ps1`. |

## Clarification Decisions (resolved in v1.0.1)

All five `[NEEDS CLARIFICATION]` markers from v1.0.0 are now resolved:

| # | REQ | Question | Decision |
|---|---|---|---|
| 1 | REQ-006 | Who holds the glossary-curator role? | Developer/admin maintaining DataIQ initially, with planned handoff to a designated business owner. Role does not yet exist in Azure AD. |
| 2 | REQ-007 | Feedback-processing job interval? | Daily, with on-demand manual trigger available. |
| 3 | REQ-009 | Audit log retention and reviewer? | Retention defers to Progeny Health's existing audit/PHI retention policy; Security/Compliance team reviews. Assumes such a policy exists and covers application audit logs. |
| 4 | REQ-010 | Rate-limit N and T values? | N = 5 denials, T = 10-minute rolling window; both configurable without redeployment. |
| 5 | REQ-013 | Target accuracy percentage? | No fixed target committed yet — establish a measured baseline first, then set the target from it. |

## Changelog

### v1.0.0 — 2026-07-29

Codex `create-prd` decision-ready deliverable grounded in the supplied reference PRD and current repository.

- Added four key user journeys covering permitted queries, history restoration, restricted data, and glossary curation.
- Added five measurable product success criteria and explicit goal-to-requirement-to-outcome traceability.
- Added a controlled rollout and product-observability plan with baseline and expansion decisions.
- Added source-grounding notes distinguishing the active React/Azure Functions implementation from the stale root README.
- Corrected the dependency summary from 10 dependencies to 10 dependent requirements comprising 12 dependency edges.
- Product scope and the 14 existing requirements remain unchanged.

### v1.0.1 — 2026-07-29

Refinement pass scoped to the five `[NEEDS CLARIFICATION]` markers (findings 1–5). Findings 6–8 were reviewed and deliberately deferred by the document owner.

- **REQ-006:** Resolved curator-role ownership (developer/admin initially, handoff planned). Added an implementation note recording that the `DataIqGlossaryEditor` app role does not yet exist in Azure AD, so the glossary editor is not currently reachable by real users in production.
- **REQ-007:** Set the feedback-processing schedule to daily (changed from the 15-minute interval currently implemented), noting the on-demand manual trigger and the resulting up-to-24h inbox latency.
- **REQ-009:** Resolved audit log retention (defers to organizational policy) and review ownership (Security/Compliance team). Added an explicit assumption-to-verify that an organizational policy exists and covers application audit logs, with the HIPAA 6-year documentation-retention fallback noted.
- **REQ-010:** Replaced abstract N/T placeholders with concrete values (5 denials / 10-minute rolling window) and made both configurable. Updated the existing risk flag to reflect that configurability mitigates the tuning risk.
- **REQ-013:** Resolved the accuracy target as baseline-first rather than a committed percentage. Added AC-013-2 encoding the baseline-then-target behavior, plus a measurement caveat that thumbs-up rate covers only rated answers and should be reported alongside feedback coverage. *(Note: this incidentally raised REQ-013's AC count from 1 to 2, partially overlapping deferred finding 6 — the new AC was added because it encodes the decision itself, not to pad AC counts.)*
- **Metadata:** Version 1.0.0 → 1.0.1; Readiness Score 4.25 → 4.63; ambiguity markers 5 → 0; total ACs 24 → 25.

### v1.0.2 — 2026-07-29

Refinement pass addressing all six findings — the three carried over from v1.0.1 plus three newly identified.

- **REQ-002 (finding 1):** Added AC-002-2 covering the zero-permitted-connections case — explanatory empty state naming who to contact, with chat input disabled rather than appearing usable.
- **REQ-009 (finding 1):** Added AC-009-2 establishing **fail-closed** behavior — if the audit log write fails, the request is blocked rather than proceeding unaudited. Added a corresponding risk flag noting this deliberately trades availability for auditability.
- **Risk indicators (finding 2):** Added risk flags to all 8 previously-untagged Medium-complexity requirements (REQ-001, 003, 004, 005, 006, 009, 013, 014). Every flag describes behavior actually observed in this codebase — non-deterministic SQL generation, stream truncation, connection-switch race conditions, glossary term collisions, the unprovisioned editor role, the fail-closed availability trade, thumbs-up-rate proxy bias, and pattern-based read-only enforcement. Risk coverage now spans all 12 Medium/High requirements.
- **Deployment/ops scope (finding 3):** Replaced the bare non-goal line with an explicit rationale (product-behavior scope at LIGHT depth), a tracking home (`infra/` assets and the Azure DevOps pipeline), and a pointer to the known production blocker.
- **Technical Constraints & Dependencies (finding 4):** Added a new section documenting the fixed platform/stack, the HIPAA hard mandate, and three constraints that apply but are deliberately left unquantified (data residency, query latency, concurrent scale) as a checklist for technical design.
- **Open External Dependencies & Blockers (finding 5):** Added a new section tracking three blockers with owner, status, and impact — the unprovisioned `DataIqGlossaryEditor` role (B-1), the unverified organizational retention policy (B-2), and the Key Vault permission grant blocking all of production (B-3, newly surfaced during this pass).
- **Stale meta-section (finding 6):** Removed the "Path to a higher score" section, which referenced findings now addressed.
- **Metadata:** Version 1.0.1 → 1.0.2; Readiness Score 4.63 → 4.88; total ACs 25 → 27; risk flags 4 → 12; AC summary table gained a Risk column and totals row.
- **Scoring note:** Feasibility was *lowered* 5.0 → 4.5 in this pass. This reflects newly documented blockers rather than any regression in the product — see the note under the Readiness Scorecard.
