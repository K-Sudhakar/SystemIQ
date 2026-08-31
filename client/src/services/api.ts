import { config } from "../config";
import type {
  AccuracyReport,
  ChatMessage,
  ConnectionSummary,
  FeedbackReviewItem,
  GlossaryEntry,
  Rating,
  ResultRow,
  StreamEvent,
} from "../types";
import type { AuthService } from "./auth";

export class ApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
  ) {
    super(message);
    this.name = "ApiError";
  }
}

/**
 * Reads an HTTP response body exactly once.
 *
 * We intentionally use response.text() first and then try to parse
 * the text as JSON. This prevents:
 *
 * "Failed to execute 'text' on 'Response': body stream already read"
 *
 * which can happen when response.json() consumes the body and
 * response.text() is then called again.
 */
const readResponseBody = async (response: Response): Promise<unknown> => {
  const responseText = await response.text();

  if (!responseText) {
    return undefined;
  }

  try {
    return JSON.parse(responseText);
  } catch {
    return responseText;
  }
};

/**
 * Extracts a useful error message from an already-read response body.
 */
const getErrorMessage = (
  data: unknown,
  status: number,
): string => {
  if (typeof data === "string" && data.trim()) {
    return data;
  }

  if (data && typeof data === "object") {
    const body = data as {
      message?: string;
      error?: string;
    };

    return (
      body.message ||
      body.error ||
      `Request failed with status ${status}`
    );
  }

  return `Request failed with status ${status}`;
};

export class ApiClient {
  constructor(private readonly auth: AuthService) {}

  private async request<T>(
    path: string,
    init: RequestInit = {},
  ): Promise<T> {
    const token = await this.auth.getToken();
    const authHeaders = this.auth.getRequestHeaders();

    const response = await fetch(`${config.apiBaseUrl}${path}`, {
      ...init,
      headers: {
        Accept: "application/json",
        ...(init.body ? { "Content-Type": "application/json" } : {}),
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
        ...authHeaders,
        ...init.headers,
      },
    });

    // Read the response body exactly once.
    const data = await readResponseBody(response);

    if (!response.ok) {
      throw new ApiError(
        getErrorMessage(data, response.status),
        response.status,
      );
    }

    return data as T;
  }

  getConnections(signal?: AbortSignal) {
    return this.request<ConnectionSummary[]>(
      "/connections",
      { signal },
    );
  }

  getHistory(
    connectionId: string,
    signal?: AbortSignal,
  ) {
    return this.request<ChatMessage[]>(
      `/history/${encodeURIComponent(connectionId)}`,
      { signal },
    );
  }

  submitFeedback(
    connectionId: string,
    messageId: string,
    rating: Rating,
    reason?: string,
    comment?: string,
  ) {
    return this.request<void>("/feedback", {
      method: "POST",
      body: JSON.stringify({
        connectionId,
        messageId,
        rating,
        reason,
        comment,
      }),
    });
  }

  async streamChat(
    connectionId: string,
    question: string,
    onEvent: (event: StreamEvent) => void,
    signal?: AbortSignal,
  ) {
    const token = await this.auth.getToken();
    const authHeaders = this.auth.getRequestHeaders();

    const response = await fetch(
      `${config.apiBaseUrl}/chat/stream`,
      {
        method: "POST",
        signal,
        headers: {
          Accept: "text/event-stream",
          "Content-Type": "application/json",
          ...(token
            ? { Authorization: `Bearer ${token}` }
            : {}),
          ...authHeaders,
        },
        body: JSON.stringify({
          connectionId,
          question,
        }),
      },
    );

    if (!response.ok) {
      // Read the error response exactly once.
      const data = await readResponseBody(response);

      throw new ApiError(
        getErrorMessage(data, response.status),
        response.status,
      );
    }

    if (!response.body) {
      throw new Error(
        "The server returned an empty response stream.",
      );
    }

    const reader = response.body.getReader();
    const decoder = new TextDecoder();

    let buffer = "";
    let completed = false;

    while (true) {
      const { value, done } = await reader.read();

      buffer += decoder.decode(value, {
        stream: !done,
      });

      const frames = buffer.split(/\r?\n\r?\n/);

      buffer = frames.pop() ?? "";

      frames
        .filter(Boolean)
        .forEach((frame) => {
          const parsed = parseSseFrame(frame);

          if (parsed) {
            if (parsed.type === "complete") completed = true;
            onEvent(parsed);
          }
        });

      if (done) {
        break;
      }
    }

    if (buffer.trim()) {
      const parsed = parseSseFrame(buffer);

      if (parsed) {
        if (parsed.type === "complete") completed = true;
        onEvent(parsed);
      }
    }

    if (!completed) {
      throw new Error("The response stream ended before completion.");
    }
  }

  getGlossary(
    connectionId: string,
    signal?: AbortSignal,
  ) {
    return this.request<GlossaryEntry[]>(
      `/curation/glossary/${encodeURIComponent(connectionId)}`,
      { signal },
    );
  }

  getGlossaryDefaults(
    connectionId: string,
    signal?: AbortSignal,
  ) {
    return this.request<GlossaryEntry[]>(
      `/curation/glossary/${encodeURIComponent(connectionId)}/defaults`,
      { signal },
    );
  }

  saveGlossary(entry: GlossaryEntry) {
    return this.request<GlossaryEntry>(
      `/curation/glossary/${encodeURIComponent(entry.connectionId)}/${encodeURIComponent(entry.table)}`,
      {
        method: "PUT",
        body: JSON.stringify(entry),
      },
    );
  }

  getFeedback(
    connectionId?: string,
    signal?: AbortSignal,
  ) {
    const query = connectionId
      ? `?connectionId=${encodeURIComponent(connectionId)}`
      : "";

    return this.request<FeedbackReviewItem[]>(
      `/curation/feedback${query}`,
      { signal },
    );
  }

  processFeedback() {
    return this.request<{ processed?: number }>(
      "/curation/feedback/process",
      {
        method: "POST",
      },
    );
  }

  resolveFeedback(id: string) {
    return this.request<void>(
      `/curation/feedback/${encodeURIComponent(id)}/resolve`,
      {
        method: "POST",
      },
    );
  }

  getAccuracyReport(
    signal?: AbortSignal,
    days = 30,
  ) {
    return this.request<AccuracyReport>(
      `/curation/accuracy-report?days=${encodeURIComponent(days)}`,
      { signal },
    );
  }
}

export function parseSseFrame(
  frame: string,
): StreamEvent | null {
  let eventName = "message";

  const dataLines: string[] = [];

  for (const line of frame.split(/\r?\n/)) {
    if (line.startsWith("event:")) {
      eventName = line.slice(6).trim();
    }

    if (line.startsWith("data:")) {
      dataLines.push(
        line.slice(5).trimStart(),
      );
    }
  }

  if (!dataLines.length) {
    return null;
  }

  const raw = dataLines.join("\n");

  let value: unknown = raw;

  try {
    value = JSON.parse(raw);
  } catch {
    // Plain-text SSE data is valid.
  }

  const text = (candidate: unknown) => {
    if (typeof candidate === "string") {
      return candidate;
    }

    if (
      candidate &&
      typeof candidate === "object"
    ) {
      const body = candidate as {
        content?: string;
        message?: string;
        chunk?: string;
        text?: string;
      };

      return (
        body.content ??
        body.message ??
        body.chunk ??
        body.text ??
        ""
      );
    }

    return "";
  };

  switch (eventName) {
    case "status":
      return {
        type: "status",
        data: text(value),
      };

    case "answer":
      return {
        type: "answer",
        data: text(value),
      };

    case "rows": {
      const rows =
        Array.isArray(value)
          ? value
          : (
              value as {
                rows?: ResultRow[];
              } | null
            )?.rows ?? [];

      return {
        type: "rows",
        data: rows as ResultRow[],
      };
    }

    case "complete":
      return {
        type: "complete",
        data:
          value &&
          typeof value === "object"
            ? (value as {
                id?: string;
                messageId?: string;
                matchedTerms?: string[];
                matchedTables?: string[];
              })
            : undefined,
      };

    case "error":
      return {
        type: "error",
        data:
          text(value) ||
          "The response could not be completed.",
      };

    default:
      return null;
  }
}
