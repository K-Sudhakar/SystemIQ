import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiClient, parseSseFrame } from "./api";

afterEach(() => vi.unstubAllGlobals());

describe("parseSseFrame", () => {
  it("parses incremental answer chunks", () => {
    expect(parseSseFrame('event: answer\ndata: {"content":"Hello"}')).toEqual({
      type: "answer",
      data: "Hello",
    });
  });

  it("parses rows and completion metadata", () => {
    expect(parseSseFrame('event: rows\ndata: [{"count":4}]')).toEqual({
      type: "rows",
      data: [{ count: 4 }],
    });
    expect(
      parseSseFrame(
        'event: complete\ndata: {"messageId":"answer-1","matchedTerms":["appointments"]}',
      ),
    ).toEqual({
      type: "complete",
      data: { messageId: "answer-1", matchedTerms: ["appointments"] },
    });
  });

  it("accepts a successful response with an empty body", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(new Response(null, { status: 202 })),
    );
    const api = new ApiClient({
      initialize: async () => {},
      signIn: async () => ({
        name: "Test",
        username: "test@example.test",
        isCurator: false,
        isDevelopment: false,
      }),
      signOut: async () => {},
      getUser: () => null,
      getToken: async () => "token",
      getRequestHeaders: () => ({}),
    });

    await expect(api.submitFeedback("mp3", "answer-1", "up")).resolves.toBeUndefined();
  });

  it("uses the non-reserved curation route for curator APIs", async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response("[]", {
        status: 200,
        headers: { "Content-Type": "application/json" },
      }),
    );
    vi.stubGlobal("fetch", fetchMock);
    const api = new ApiClient({
      initialize: async () => {},
      signIn: async () => ({
        name: "Test",
        username: "test@example.test",
        isCurator: true,
        isDevelopment: false,
      }),
      signOut: async () => {},
      getUser: () => null,
      getToken: async () => "token",
      getRequestHeaders: () => ({}),
    });

    await api.getGlossary("demo");

    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringMatching(/\/curation\/glossary\/demo$/),
      expect.any(Object),
    );
  });

  it("ignores unknown and empty frames", () => {
    expect(parseSseFrame(": keep-alive")).toBeNull();
    expect(parseSseFrame("event: something\ndata: value")).toBeNull();
  });

  it("uses headers selected by the auth adapter", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response("[]", { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);
    const api = new ApiClient({
      initialize: async () => {}, signIn: async () => ({ name: "Local", username: "local", isCurator: true, isDevelopment: true }),
      signOut: async () => {}, getUser: () => null, getToken: async () => null,
      getRequestHeaders: () => ({ "X-SystemIQ-Development-Identity": "fixed-local" }),
    });
    await api.getConnections();
    expect(fetchMock).toHaveBeenCalledWith(expect.any(String), expect.objectContaining({
      headers: expect.objectContaining({ "X-SystemIQ-Development-Identity": "fixed-local" }),
    }));
  });

  it("rejects an SSE stream that ends without a complete event", async () => {
    const body = new ReadableStream({
      start(controller) {
        controller.enqueue(new TextEncoder().encode('event: answer\ndata: {"content":"partial"}\n\n'));
        controller.close();
      },
    });
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(body, {
      status: 200, headers: { "Content-Type": "text/event-stream" },
    })));
    const api = new ApiClient({
      initialize: async () => {}, signIn: async () => ({ name: "Test", username: "test", isCurator: false, isDevelopment: false }),
      signOut: async () => {}, getUser: () => null, getToken: async () => null, getRequestHeaders: () => ({}),
    });

    await expect(api.streamChat("demo", "question", () => {})).rejects.toThrow("before completion");
  });
});
