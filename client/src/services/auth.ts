import {
  InteractionRequiredAuthError,
  PublicClientApplication,
  type AccountInfo,
} from "@azure/msal-browser";
import { config } from "../config";

export interface UserIdentity {
  name: string;
  username: string;
  isCurator: boolean;
  isDevelopment: boolean;
}

export interface AuthService {
  initialize(): Promise<void>;
  signIn(): Promise<UserIdentity>;
  signOut(): Promise<void>;
  getUser(): UserIdentity | null;
  getToken(): Promise<string | null>;
  getRequestHeaders(): Record<string, string>;
}

const curatorRole = "DataIqGlossaryEditor";

class DevelopmentAuthService implements AuthService {
  private readonly user: UserIdentity = {
    name: "Local curator",
    username: "development@local",
    isCurator: true,
    isDevelopment: true,
  };

  async initialize() {}
  async signIn() {
    return this.user;
  }
  async signOut() {}
  getUser() {
    return this.user;
  }
  async getToken() {
    return null;
  }
  getRequestHeaders() {
    return { [config.developmentHeader]: config.developmentIdentity };
  }
}

class UnavailableAuthService implements AuthService {
  async initialize() {}
  async signIn(): Promise<UserIdentity> {
    throw new Error(
      "Azure AD is not configured. Set the client, tenant, and API scope environment values.",
    );
  }
  async signOut() {}
  getUser() {
    return null;
  }
  async getToken() {
    return null;
  }
  getRequestHeaders() { return {}; }
}

class MsalAuthService implements AuthService {
  private readonly client = new PublicClientApplication({
    auth: {
      clientId: config.clientId,
      authority: `https://login.microsoftonline.com/${config.tenantId}`,
      redirectUri: window.location.origin,
      postLogoutRedirectUri: window.location.origin,
    },
    cache: { cacheLocation: "sessionStorage" },
  });
  private account: AccountInfo | null = null;
  private hasCuratorRole = false;

  async initialize() {
    await this.client.initialize();
    const redirectResult = await this.client.handleRedirectPromise();
    this.account = redirectResult?.account ?? this.client.getAllAccounts()[0] ?? null;
    if (this.account) {
      this.client.setActiveAccount(this.account);
      await this.getToken();
    }
  }

  async signIn() {
    const result = await this.client.loginPopup({
      scopes: config.apiScope ? [config.apiScope] : [],
      prompt: "select_account",
    });
    this.account = result.account;
    this.client.setActiveAccount(result.account);
    await this.getToken();
    return this.toIdentity(result.account);
  }

  async signOut() {
    await this.client.logoutPopup({ account: this.account ?? undefined });
    this.account = null;
    this.hasCuratorRole = false;
  }

  getUser() {
    return this.account ? this.toIdentity(this.account) : null;
  }

  async getToken() {
    if (!this.account || !config.apiScope) return null;
    try {
      const result = await this.client.acquireTokenSilent({
        account: this.account,
        scopes: [config.apiScope],
      });
      this.hasCuratorRole = tokenHasRole(result.accessToken, curatorRole);
      return result.accessToken;
    } catch (error) {
      if (!(error instanceof InteractionRequiredAuthError)) throw error;
      const result = await this.client.acquireTokenPopup({
        account: this.account,
        scopes: [config.apiScope],
      });
      this.hasCuratorRole = tokenHasRole(result.accessToken, curatorRole);
      return result.accessToken;
    }
  }
  getRequestHeaders() { return {}; }

  private toIdentity(account: AccountInfo): UserIdentity {
    return {
      name: account.name || account.username,
      username: account.username,
      isCurator: this.hasCuratorRole,
      isDevelopment: false,
    };
  }
}

function tokenHasRole(token: string, role: string) {
  try {
    const payload = token.split(".")[1];
    if (!payload) return false;
    const normalized = payload.replace(/-/g, "+").replace(/_/g, "/");
    const padded = normalized.padEnd(Math.ceil(normalized.length / 4) * 4, "=");
    const claims = JSON.parse(atob(padded)) as { roles?: string[] };
    return claims.roles?.includes(role) ?? false;
  } catch {
    return false;
  }
}

export const isAuthConfigured = config.authMode === "Oidc" && Boolean(config.clientId && config.tenantId);
export const isDevelopmentAuth = !isAuthConfigured && config.devAuthBypass;

export const authService: AuthService =
  isAuthConfigured
    ? new MsalAuthService()
    : isDevelopmentAuth
      ? new DevelopmentAuthService()
      : new UnavailableAuthService();
