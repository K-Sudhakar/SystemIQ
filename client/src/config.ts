const trimTrailingSlash = (value: string) => value.replace(/\/+$/, "");

export const config = {
  apiBaseUrl: trimTrailingSlash(import.meta.env.VITE_API_BASE_URL || "/api"),
  clientId: import.meta.env.VITE_AZURE_CLIENT_ID?.trim() || "",
  tenantId: import.meta.env.VITE_AZURE_TENANT_ID?.trim() || "",
  apiScope: import.meta.env.VITE_AZURE_API_SCOPE?.trim() || "",
  devAuthBypass:
    import.meta.env.DEV && import.meta.env.VITE_DEV_AUTH_BYPASS !== "false",
};
