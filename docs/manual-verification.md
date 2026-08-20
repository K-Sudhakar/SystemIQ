# Manual verification checklist

Record the operator, UTC date, environment, evidence link, and result for every
item before pilot rollout.

## Microsoft Entra ID

- [ ] SPA and API app registrations use the intended tenant.
- [ ] API exposes the configured delegated scope.
- [ ] `DataIqGlossaryEditor` is present, enabled, and assignable.
- [ ] A curator receives 200 from an admin endpoint.
- [ ] A normal user receives 403 from the same endpoint.

## Data access and policy

- [ ] A permitted connection/table/column query succeeds.
- [ ] A denied direct table or column query is rejected before execution.
- [ ] A denied indirect join is rejected before execution.
- [ ] A policy change applies to the user's next request without re-login.
- [ ] Direct `INSERT`, `UPDATE`, `DELETE`, and DDL attempts using the application
      database identity fail with a database permission error.

## Audit and denial limiting

- [ ] A denied request creates exactly one correctly shaped audit blob.
- [ ] Blocking audit-container access causes the request to fail closed with the
      distinct system-error response.
- [ ] Five denials inside ten minutes cause the next request to be rate-limited.
- [ ] Denials older than the configured window do not count.

## Storage, transport, and secrets

- [ ] Storage secure transfer is required and public access is disabled.
- [ ] Key Vault purge protection and RBAC authorization are enabled.
- [ ] SQL connection settings enforce encryption and certificate validation.
- [ ] Function and Static Web App public endpoints redirect/reject plain HTTP.
- [ ] Application Insights contains no tokens, SQL result rows, answers, or
      connection strings.

## User journeys

- [ ] A user with no permitted connections sees the access-contact empty state
      and cannot submit chat.
- [ ] A streamed answer renders chunks and result rows.
- [ ] Switching connections during a delayed history request never shows stale
      history.
- [ ] Negative feedback is processed into the curator inbox.
- [ ] “Edit terms” opens the exact matched glossary table.
- [ ] Accuracy report rates match a hand-calculated sample.

