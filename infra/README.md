# DataIQ Azure infrastructure

`main.bicep` provisions the application hosting and data-plane dependencies:

- .NET 9 isolated Azure Functions on an EP1 Linux plan
- Azure Static Web Apps Standard
- Storage containers for chat history, glossary, feedback, and fail-closed audit logs
- Azure Table Storage for denial-window rate limiting
- Key Vault with RBAC, purge protection, and 90-day soft-delete retention
- workspace-based Application Insights
- a system-assigned Function identity with Key Vault and Storage data-plane roles

The template never accepts secret values. Populate the two named Key Vault
secrets after deployment through an approved secret-management process:

- `database-connections`
- `dataiq-access-policy`

## Validate and deploy

Copy `main.parameters.example.json` outside source control and replace the
placeholders. Confirm `location` is approved for PHI before deployment.

```powershell
az bicep build --file infra/main.bicep
az deployment group what-if `
  --resource-group <resource-group> `
  --template-file infra/main.bicep `
  --parameters @<parameters-file>
az deployment group create `
  --resource-group <resource-group> `
  --template-file infra/main.bicep `
  --parameters @<parameters-file>
```

The deployer needs permission to create role assignments. If Bicep role
assignments are skipped by policy, run `grant-deployment-permissions.ps1` as an
Owner or User Access Administrator. That script also creates the
`DataIqGlossaryEditor` API app role and can assign it to curator object IDs.

## Release prerequisites

- Confirm the tenant's audit/PHI retention policy applies to the `audit-log`
  container. The template deliberately does not invent or enforce a deletion
  lifecycle while that policy remains unconfirmed.
- Confirm Azure SQL credentials in `database-connections` are read-only and
  require encryption/TLS.
- Confirm the Function managed identity has the `Cognitive Services OpenAI
  User` role on the existing Azure OpenAI account. The template creates this
  assignment when the deployer has role-assignment permission.
- Add alert rules for Function failures, audit-write failures, rate-limit
  triggers, and Application Insights availability.
- Restrict Storage and Key Vault with private endpoints before production if
  required by the organization's network baseline; public endpoints still
  require authenticated TLS access in this baseline.
