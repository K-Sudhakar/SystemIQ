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
  }
}

const extractError = async (response: Response) => {
  const fallback = `Request failed (${response.status})`;
  try {
    const body = (await response.json()) as { message?: string; error?: string };
    return body.message || body.error || fallback;
  } catch {
    return (await response.text()) || fallback;
  }
};

export class ApiClient {
  constructor(private readonly auth: AuthService) {}

  private async request<T>(path: string, init: RequestInit = {}): Promise<T> {
    const token = await this.auth.getToken();
    const response = await fetch(`${config.apiBaseUrl}${path}`, {
      ...init,
      headers: {
        Accept: "application/json",
        ...(init.body ? { "Content-Type": "application/json" } : {}),
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
        ...(config.devAuthBypass
          ? {
              "x-test-user": "local-curator",
              "x-test-role": "DataIqGlossaryEditor",
            }
          : {}),
        ...init.headers,
      },
    });
    if (!response.ok) throw new ApiError(await extractError(response), response.status);
    const body = await response.text();
    return (body ? JSON.parse(body) : undefined) as T;
  }

  getConnections(signal?: AbortSignal) {
    return this.request<ConnectionSummary[]>("/connections", { signal });
  }

  getHistory(connectionId: string, signal?: AbortSignal) {
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
      body: JSON.stringify({ connectionId, messageId, rating, reason, comment }),
    });
  }

  async streamChat(
    connectionId: string,
    question: string,
    onEvent: (event: StreamEvent) => void,
    signal?: AbortSignal,
  ) {
    const token = await this.auth.getToken();
    const response = await fetch(`${config.apiBaseUrl}/chat/stream`, {
      method: "POST",
      signal,
      headers: {
        Accept: "text/event-stream",
        "Content-Type": "application/json",
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
        ...(config.devAuthBypass
          ? {
              "x-test-user": "local-curator",
              "x-test-role": "DataIqGlossaryEditor",
            }
          : {}),
      },
      body: JSON.stringify({ connectionId, question }),
    });
    if (!response.ok) throw new ApiError(await extractError(response), response.status);
    if (!response.body) throw new Error("The server returned an empty response stream.");

    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let buffer = "";
    while (true) {
      const { value, done } = await reader.read();
      buffer += decoder.decode(value, { stream: !done });
      const frames = buffer.split(/\r?\n\r?\n/);
      buffer = frames.pop() ?? "";
      frames.filter(Boolean).forEach((frame) => {
        const parsed = parseSseFrame(frame);
        if (parsed) onEvent(parsed);
      });
      if (done) break;
    }
    if (buffer.trim()) {
      const parsed = parseSseFrame(buffer);
      if (parsed) onEvent(parsed);
    }
  }

  getGlossary(connectionId: string, signal?: AbortSignal) {
    return this.request<GlossaryEntry[]>(
      `/admin/glossary/${encodeURIComponent(connectionId)}`,
      { signal },
    );
  }

  getGlossaryDefaults(connectionId: string, signal?: AbortSignal) {
    return this.request<GlossaryEntry[]>(
      `/admin/glossary/${encodeURIComponent(connectionId)}/defaults`,
      { signal },
    );
  }

  saveGlossary(entry: GlossaryEntry) {
    return this.request<GlossaryEntry>(
      `/admin/glossary/${encodeURIComponent(entry.connectionId)}/${encodeURIComponent(entry.table)}`,
      { method: "PUT", body: JSON.stringify(entry) },
    );
  }

  getFeedback(connectionId?: string, signal?: AbortSignal) {
    const query = connectionId
      ? `?connectionId=${encodeURIComponent(connectionId)}`
      : "";
    return this.request<FeedbackReviewItem[]>(`/admin/feedback${query}`, { signal });
  }

  processFeedback() {
    return this.request<{ processed?: number }>("/admin/feedback/process", {
      method: "POST",
    });
  }

  resolveFeedback(id: string) {
    return this.request<void>(`/admin/feedback/${encodeURIComponent(id)}/resolve`, {
      method: "POST",
    });
  }

  getAccuracyReport(signal?: AbortSignal, days = 30) {
    return this.request<AccuracyReport>(
      `/admin/accuracy-report?days=${encodeURIComponent(days)}`,
      { signal },
    );
  }
}

export function parseSseFrame(frame: string): StreamEvent | null {
  let eventName = "message";
  const dataLines: string[] = [];
  for (const line of frame.split(/\r?\n/)) {
    if (line.startsWith("event:")) eventName = line.slice(6).trim();
    if (line.startsWith("data:")) dataLines.push(line.slice(5).trimStart());
  }
  if (!dataLines.length) return null;
  const raw = dataLines.join("\n");
  let value: unknown = raw;
  try {
    value = JSON.parse(raw);
  } catch {
    // Plain-text SSE data is valid.
  }

  const text = (candidate: unknown) => {
    if (typeof candidate === "string") return candidate;
    if (candidate && typeof candidate === "object") {
      const body = candidate as {
        content?: string;
        message?: string;
        chunk?: string;
        text?: string;
      };
      return body.content ?? body.message ?? body.chunk ?? body.text ?? "";
    }
    return "";
  };

  switch (eventName) {
    case "status":
      return { type: "status", data: text(value) };
    case "answer":
      return { type: "answer", data: text(value) };
    case "rows": {
      const rows =
        Array.isArray(value)
          ? value
          : ((value as { rows?: ResultRow[] } | null)?.rows ?? []);
      return { type: "rows", data: rows as ResultRow[] };
    }
    case "complete":
      return {
        type: "complete",
        data:
          value && typeof value === "object"
            ? (value as {
                id?: string;
                messageId?: string;
                matchedTerms?: string[];
                matchedTables?: string[];
              })
            : undefined,
      };
    case "error":
      return { type: "error", data: text(value) || "The response could not be completed." };
    default:
      return null;
  }
}
