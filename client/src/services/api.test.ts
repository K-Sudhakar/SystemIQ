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
    });

    await expect(api.submitFeedback("mp3", "answer-1", "up")).resolves.toBeUndefined();
  });

  it("ignores unknown and empty frames", () => {
    expect(parseSseFrame(": keep-alive")).toBeNull();
    expect(parseSseFrame("event: something\ndata: value")).toBeNull();
  });
});
