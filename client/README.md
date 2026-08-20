# SystemIQ client

React, Vite, and TypeScript frontend for the DataIQ SQL Assistant.

## Local development

1. Copy `.env.example` to `.env.local`.
2. Leave `VITE_DEV_AUTH_BYPASS=true` to use the local curator identity, or provide the
   Azure AD client, tenant, and API scope values and set it to `false`.
3. Install the locked dependencies and run `npm run dev`.

Vite proxies `/api` to the Azure Functions development host at
`http://localhost:7071`.

## Checks

- `npm run typecheck`
- `npm test`
- `npm run build`

The production deployment should provide `VITE_AZURE_CLIENT_ID`,
`VITE_AZURE_TENANT_ID`, `VITE_AZURE_API_SCOPE`, and `VITE_API_BASE_URL`.
