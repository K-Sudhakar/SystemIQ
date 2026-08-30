import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import test from "node:test";

const port = 7171;
const baseUrl = `http://127.0.0.1:${port}`;
const clientOrigin = "http://127.0.0.1:5173";

const startDemoApi = () =>
  new Promise((resolve, reject) => {
    const child = spawn(process.execPath, ["scripts/demo-api.mjs"], {
      env: {
        ...process.env,
        DEMO_API_PORT: String(port),
      },
      stdio: ["ignore", "pipe", "pipe"],
    });

    const onError = (chunk) => reject(new Error(chunk.toString()));
    child.stderr.once("data", onError);
    child.once("error", reject);
    child.stdout.on("data", (chunk) => {
      if (chunk.toString().includes("SystemIQ demo API listening")) {
        child.stderr.off("data", onError);
        resolve(child);
      }
    });
  });

test("demo API exposes a friendly root and supports the local client origin", async (context) => {
  const child = await startDemoApi();
  context.after(() => child.kill());

  const rootResponse = await fetch(`${baseUrl}/`);
  assert.equal(rootResponse.status, 200);
  assert.deepEqual(await rootResponse.json(), {
    service: "SystemIQ demo API",
    status: "ok",
    connectionsEndpoint: "/api/connections",
    clientUrl: "http://localhost:5173",
  });

  const connectionsResponse = await fetch(`${baseUrl}/api/connections`, {
    headers: { Origin: clientOrigin },
  });
  assert.equal(connectionsResponse.status, 200);
  assert.equal(
    connectionsResponse.headers.get("access-control-allow-origin"),
    clientOrigin,
  );
  assert.deepEqual(await connectionsResponse.json(), [
    { id: "demo", displayName: "Demo Clinical Operations" },
  ]);

  const preflightResponse = await fetch(`${baseUrl}/api/connections`, {
    method: "OPTIONS",
    headers: { Origin: clientOrigin },
  });
  assert.equal(preflightResponse.status, 204);
  assert.equal(
    preflightResponse.headers.get("access-control-allow-origin"),
    clientOrigin,
  );

  const disallowedResponse = await fetch(`${baseUrl}/api/connections`, {
    headers: { Origin: "https://example.com" },
  });
  assert.equal(
    disallowedResponse.headers.get("access-control-allow-origin"),
    null,
  );

  const unknownResponse = await fetch(`${baseUrl}/unknown`);
  assert.equal(unknownResponse.status, 404);
});
