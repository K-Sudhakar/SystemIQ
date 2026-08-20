import http from "node:http";
import { randomUUID } from "node:crypto";

const port = Number(process.env.DEMO_API_PORT || 7071);
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

const json = (response, status, value) => {
  response.writeHead(status, {
    "Content-Type": "application/json",
    "Access-Control-Allow-Origin": "http://localhost:5173",
    "Access-Control-Allow-Headers": "authorization, content-type, x-test-user, x-test-role",
    "Access-Control-Allow-Methods": "GET,POST,PUT,OPTIONS",
  });
  response.end(value === undefined ? undefined : JSON.stringify(value));
};

const server = http.createServer(async (request, response) => {
  const url = new URL(request.url ?? "/", `http://${request.headers.host}`);
  if (request.method === "OPTIONS") return json(response, 204);
  if (request.method === "GET" && url.pathname === "/api/connections") {
    return json(response, 200, [{ id: "demo", displayName: "Demo Clinical Operations" }]);
  }
  if (request.method === "GET" && url.pathname === "/api/history/demo") {
    return json(response, 200, []);
  }
  if (request.method === "POST" && url.pathname === "/api/chat/stream") {
    response.writeHead(200, {
      "Content-Type": "text/event-stream",
      "Cache-Control": "no-cache",
      "Access-Control-Allow-Origin": "http://localhost:5173",
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
    return json(response, 202);
  }
  if (
    request.method === "GET" &&
    url.pathname === "/api/admin/glossary/demo/defaults"
  ) {
    return json(response, 200, glossary);
  }
  if (request.method === "GET" && url.pathname === "/api/admin/glossary/demo") {
    return json(response, 200, glossary);
  }
  if (request.method === "PUT" && url.pathname.startsWith("/api/admin/glossary/")) {
    let body = "";
    for await (const chunk of request) body += chunk;
    return json(response, 200, JSON.parse(body));
  }
  if (request.method === "GET" && url.pathname === "/api/admin/feedback") {
    return json(response, 200, []);
  }
  if (request.method === "POST" && url.pathname === "/api/admin/feedback/process") {
    return json(response, 200, { processed: 0 });
  }
  if (request.method === "GET" && url.pathname === "/api/admin/accuracy-report") {
    return json(response, 200, {
      thumbsUpRate: 80,
      thumbsDownRate: 20,
      feedbackCoverage: 62.5,
      answerCount: 24,
      ratedCount: 15,
      from: new Date(Date.now() - 30 * 86400000).toISOString(),
      to: new Date().toISOString(),
    });
  }
  return json(response, 404, { error: "Demo endpoint not found." });
});

server.listen(port, "127.0.0.1", () => {
  console.log(`SystemIQ demo API listening on http://127.0.0.1:${port}`);
});

