import { afterEach, describe, expect, it, vi } from "vitest";

afterEach(() => {
  delete window.__SYSTEMIQ_CONFIG__;
  vi.resetModules();
});

describe("runtime configuration", () => {
  it("prefers deployment-time values and trims the API trailing slash", async () => {
    window.__SYSTEMIQ_CONFIG__ = {
      apiBaseUrl: "/gateway/api/",
      auth: { mode: "DevelopmentHeader", developmentIdentity: "fixed-local" },
    };
    const { config } = await import("./config");
    expect(config.apiBaseUrl).toBe("/gateway/api");
    expect(config.developmentIdentity).toBe("fixed-local");
    expect(config.devAuthBypass).toBe(true);
  });

  it("does not enable development auth from an unavailable runtime profile", async () => {
    window.__SYSTEMIQ_CONFIG__ = { auth: { mode: "Unavailable" } };
    const { config } = await import("./config");
    expect(config.authMode).toBe("Unavailable");
  });
});
