# Security and compliance baseline

This document records the deployment controls required by REQ-008, REQ-009,
REQ-012, and REQ-014. It is an engineering checklist, not a legal determination
of HIPAA compliance.

## Identity and least privilege

- The SPA and API use separate Microsoft Entra ID app registrations.
- The API validates issuer, audience, signature, and token lifetime.
- Glossary and reporting administration requires the `DataIqGlossaryEditor`
  application role.
- The API managed identity receives only data-plane access to its own Key Vault
  secrets and Storage resources.
- Connection/table/column policy is loaded for every request. It is not cached in
  the browser or retained for the duration of a login session.

## Data access

- Application SQL is limited to a single read-only `SELECT`/CTE statement.
- SQL is parsed and checked against the effective table and column policy before
  execution.
- Each configured database credential must be a member of `db_datareader` only
  and must be explicitly denied write and DDL permissions.
- Query results are capped and command timeouts are configured.

Example database hardening, to be adapted and run by a database administrator:

```sql
CREATE USER [systemiq-api] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [systemiq-api];
DENY INSERT, UPDATE, DELETE, EXECUTE, ALTER, CONTROL
  TO [systemiq-api];
```

## Encryption

- Storage requires HTTPS, disables public blob access, and uses Microsoft-managed
  encryption at rest unless the organization mandates a customer-managed key.
- Key Vault uses RBAC authorization, purge protection, and soft delete.
- Azure SQL connections require encryption and reject untrusted certificates.
- Static Web Apps and Functions expose HTTPS endpoints only.
- Approved Azure regions and any customer-managed-key requirement must be
  confirmed by Security/Compliance before production.

## Audit and retention

- Every access or SQL-policy denial is synchronously written to the audit
  container before a denial is returned.
- Failure to persist the audit record fails closed.
- Audit records contain the authenticated subject, connection, question or SQL,
  denial reason, and UTC timestamp. They must not be sent to client telemetry.
- Container lifecycle/immutability and deletion authorization must be aligned to
  Progeny Health's verified audit/PHI retention policy. If no applicable policy
  exists, Security/Compliance must set one before production.

## PHI handling

- Never log query result rows, generated natural-language answers, access tokens,
  connection strings, or secret values to Application Insights.
- Chat-history, feedback, and glossary containers are private.
- Non-production data must be synthetic or de-identified.
- Production access reviews, incident response, breach notification, backups,
  disaster recovery, and vendor BAAs remain organizational controls outside this
  repository.

