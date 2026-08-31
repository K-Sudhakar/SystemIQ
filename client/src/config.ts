const trimTrailingSlash = (value: string) => value.replace(/\/+$/, "");

export interface RuntimeConfig {
  apiBaseUrl?: string;
  auth?: {
    mode?: "Oidc" | "DevelopmentHeader" | "Unavailable";
    clientId?: string;
    tenantId?: string;
    apiScope?: string;
    developmentIdentity?: string;
    developmentHeader?: string;
  };
}

declare global {
  interface Window { __SYSTEMIQ_CONFIG__?: RuntimeConfig; }
}

const runtime = typeof window === "undefined" ? {} : (window.__SYSTEMIQ_CONFIG__ ?? {});
const runtimeAuth = runtime.auth ?? {};
const developmentMode = runtimeAuth.mode === "DevelopmentHeader";
const clientId = runtimeAuth.clientId?.trim() || import.meta.env.VITE_AZURE_CLIENT_ID?.trim() || "";
const tenantId = runtimeAuth.tenantId?.trim() || import.meta.env.VITE_AZURE_TENANT_ID?.trim() || "";

export const config = {
  apiBaseUrl: trimTrailingSlash(runtime.apiBaseUrl || import.meta.env.VITE_API_BASE_URL || "/api"),
  authMode: runtimeAuth.mode ?? (clientId && tenantId ? "Oidc" : "Unavailable"),
  clientId,
  tenantId,
  apiScope: runtimeAuth.apiScope?.trim() || import.meta.env.VITE_AZURE_API_SCOPE?.trim() || "",
  developmentIdentity: runtimeAuth.developmentIdentity?.trim() || "local-curator",
  developmentHeader: runtimeAuth.developmentHeader?.trim() || "X-SystemIQ-Development-Identity",
  devAuthBypass:
    developmentMode || (import.meta.env.DEV && import.meta.env.VITE_DEV_AUTH_BYPASS === "true"),
};
