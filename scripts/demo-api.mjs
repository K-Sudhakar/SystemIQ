import http from "node:http";
import { randomUUID } from "node:crypto";

const port = Number(process.env.DEMO_API_PORT || 7071);
const clientOrigin = process.env.DEMO_CLIENT_ORIGIN || "http://localhost:5173";
const allowedClientOrigins = new Set([
  clientOrigin,
  "http://localhost:5173",
  "http://127.0.0.1:5173",
]);
const glossary = [
  {
    connectionId: "demo",
    table: "dbo.Members",
    businessTerm: "members",
    description: "People enrolled in a care program.",
    synonyms: ["patients", "participants"],
    relatedColumns: ["MemberId", "Program", "EnrollmentDate", "Status"],
    joinHints: [],
  },
];

const corsHeaders = (request) => {
  const requestOrigin = request.headers.origin;
  const allowedOrigin = allowedClientOrigins.has(requestOrigin)
    ? requestOrigin
    : undefined;

  return {
    "Access-Control-Allow-Headers": "authorization, content-type, x-test-user, x-test-role",
    "Access-Control-Allow-Methods": "GET,POST,PUT,OPTIONS",
    ...(allowedOrigin
      ? {
          "Access-Control-Allow-Origin": allowedOrigin,
          Vary: "Origin",
        }
      : {}),
  };
};

const json = (request, response, status, value) => {
  response.writeHead(status, {
    "Content-Type": "application/json",
    ...corsHeaders(request),
  });
  response.end(value === undefined ? undefined : JSON.stringify(value));
};

const server = http.createServer(async (request, response) => {
  const url = new URL(request.url ?? "/", `http://${request.headers.host}`);
  if (request.method === "OPTIONS") return json(request, response, 204);
  if (request.method === "GET" && url.pathname === "/") {
    return json(request, response, 200, {
      service: "SystemIQ demo API",
      status: "ok",
      connectionsEndpoint: "/api/connections",
      clientUrl: clientOrigin,
    });
  }
  if (request.method === "GET" && url.pathname === "/api/connections") {
    return json(request, response, 200, [{ id: "demo", displayName: "Demo Clinical Operations" }]);
  }
  if (request.method === "GET" && url.pathname === "/api/history/demo") {
    return json(request, response, 200, []);
  }
  if (request.method === "POST" && url.pathname === "/api/chat/stream") {
    response.writeHead(200, {
      "Content-Type": "text/event-stream",
      "Cache-Control": "no-cache",
      ...corsHeaders(request),
    });
    const send = (event, data) =>
      response.write(`event: ${event}\ndata: ${JSON.stringify(data)}\n\n`);
    send("status", { message: "Generating a safe demo query" });
    await new Promise((resolve) => setTimeout(resolve, 250));
    for (const text of [
      "The demo connection contains ",
      "three active members across ",
      "two care programs.",
    ]) {
      send("answer", { text });
      await new Promise((resolve) => setTimeout(resolve, 180));
    }
    send("rows", [
      { program: "Maternal Care", activeMembers: 2 },
      { program: "Neonatal Follow-up", activeMembers: 1 },
    ]);
    send("complete", {
      messageId: randomUUID(),
      matchedTerms: ["members"],
      matchedTables: ["dbo.Members"],
    });
    return response.end();
  }
  if (request.method === "POST" && url.pathname === "/api/feedback") {
    return json(request, response, 202);
  }
  if (
    request.method === "GET" &&
    url.pathname === "/api/curation/glossary/demo/defaults"
  ) {
    return json(request, response, 200, glossary);
  }
  if (request.method === "GET" && url.pathname === "/api/curation/glossary/demo") {
    return json(request, response, 200, glossary);
  }
  if (request.method === "PUT" && url.pathname.startsWith("/api/curation/glossary/")) {
    let body = "";
    for await (const chunk of request) body += chunk;
    return json(request, response, 200, JSON.parse(body));
  }
  if (request.method === "GET" && url.pathname === "/api/curation/feedback") {
    return json(request, response, 200, []);
  }
  if (request.method === "POST" && url.pathname === "/api/curation/feedback/process") {
    return json(request, response, 200, { processed: 0 });
  }
  if (request.method === "GET" && url.pathname === "/api/curation/accuracy-report") {
    return json(request, response, 200, {
      thumbsUpRate: 80,
      thumbsDownRate: 20,
      feedbackCoverage: 62.5,
      answerCount: 24,
      ratedCount: 15,
      from: new Date(Date.now() - 30 * 86400000).toISOString(),
      to: new Date().toISOString(),
    });
  }
  return json(request, response, 404, { error: "Demo endpoint not found." });
});

server.listen(port, "127.0.0.1", () => {
  console.log(`SystemIQ demo API listening on http://127.0.0.1:${port}`);
});
