import { useCallback, useEffect, useRef, useState } from "react";
import type { ApiClient } from "../services/api";
import type { ChatMessage, Rating, ResultRow, StreamEvent } from "../types";

type MessagesByConnection = Record<string, ChatMessage[]>;

const newId = () =>
  globalThis.crypto?.randomUUID?.() ??
  `${Date.now()}-${Math.random().toString(36).slice(2)}`;

export function useChatController(api: ApiClient) {
  const [selectedConnectionId, setSelectedConnectionId] = useState("");
  const [messagesByConnection, setMessagesByConnection] =
    useState<MessagesByConnection>({});
  const [loadingHistory, setLoadingHistory] = useState(false);
  const [sending, setSending] = useState(false);
  const [status, setStatus] = useState("");
  const [error, setError] = useState("");
  const historyRequest = useRef(0);
  const historyAbort = useRef<AbortController | null>(null);
  const streamAbort = useRef<AbortController | null>(null);
  const selectedRef = useRef("");

  useEffect(
    () => () => {
      historyAbort.current?.abort();
      streamAbort.current?.abort();
    },
    [],
  );

  const updateMessages = useCallback(
    (connectionId: string, updater: (current: ChatMessage[]) => ChatMessage[]) => {
      setMessagesByConnection((all) => ({
        ...all,
        [connectionId]: updater(all[connectionId] ?? []),
      }));
    },
    [],
  );

  const selectConnection = useCallback(
    async (connectionId: string) => {
      selectedRef.current = connectionId;
      setSelectedConnectionId(connectionId);
      setError("");
      setStatus("");
      historyAbort.current?.abort();
      const requestId = ++historyRequest.current;
      if (!connectionId || messagesByConnection[connectionId]) return;

      const controller = new AbortController();
      historyAbort.current = controller;
      setLoadingHistory(true);
      try {
        const history = await api.getHistory(connectionId, controller.signal);
        if (
          requestId !== historyRequest.current ||
          connectionId !== selectedRef.current
        )
          return;
        setMessagesByConnection((all) => ({ ...all, [connectionId]: history }));
      } catch (reason) {
        if (controller.signal.aborted) return;
        if (
          requestId === historyRequest.current &&
          connectionId === selectedRef.current
        ) {
          setError(reason instanceof Error ? reason.message : "History could not be loaded.");
        }
      } finally {
        if (requestId === historyRequest.current) setLoadingHistory(false);
      }
    },
    [api, messagesByConnection],
  );

  const submit = useCallback(
    async (question: string) => {
      const connectionId = selectedRef.current;
      if (!connectionId || sending) return;
      const userMessage: ChatMessage = {
        id: newId(),
        role: "user",
        content: question.trim(),
        createdAt: new Date().toISOString(),
      };
      const assistantId = newId();
      const assistantMessage: ChatMessage = {
        id: assistantId,
        role: "assistant",
        content: "",
        createdAt: new Date().toISOString(),
        streaming: true,
      };
      updateMessages(connectionId, (current) => [
        ...current,
        userMessage,
        assistantMessage,
      ]);
      setSending(true);
      setError("");
      setStatus("Understanding your question…");
      const controller = new AbortController();
      streamAbort.current = controller;

      const patchAssistant = (patch: Partial<ChatMessage>) =>
        updateMessages(connectionId, (current) =>
          current.map((message) =>
            message.id === assistantId ? { ...message, ...patch } : message,
          ),
        );

      const handleEvent = (event: StreamEvent) => {
        if (event.type === "status") setStatus(event.data);
        if (event.type === "answer") {
          updateMessages(connectionId, (current) =>
            current.map((message) =>
              message.id === assistantId
                ? { ...message, content: message.content + event.data }
                : message,
            ),
          );
        }
        if (event.type === "rows") patchAssistant({ rows: event.data });
        if (event.type === "complete") {
          patchAssistant({
            id: event.data?.id || event.data?.messageId || assistantId,
            matchedTerms: event.data?.matchedTerms,
            matchedTables: event.data?.matchedTables,
            streaming: false,
          });
          setStatus("");
        }
        if (event.type === "error") throw new Error(event.data);
      };

      try {
        await api.streamChat(connectionId, question.trim(), handleEvent, controller.signal);
        patchAssistant({ streaming: false });
      } catch (reason) {
        if (controller.signal.aborted) {
          patchAssistant({ streaming: false, interrupted: true });
        } else {
          const message =
            reason instanceof Error ? reason.message : "The question could not be answered.";
          patchAssistant({ content: message, streaming: false, interrupted: true });
          if (connectionId === selectedRef.current) setError(message);
        }
      } finally {
        setSending(false);
        setStatus("");
      }
    },
    [api, sending, updateMessages],
  );

  const stop = useCallback(() => streamAbort.current?.abort(), []);

  const rateMessage = useCallback(
    async (
      messageId: string,
      rating: Rating,
      reason?: string,
      comment?: string,
    ) => {
      const connectionId = selectedRef.current;
      if (!connectionId) return;
      await api.submitFeedback(connectionId, messageId, rating, reason, comment);
      updateMessages(connectionId, (current) =>
        current.map((message) =>
          message.id === messageId
            ? { ...message, feedback: { rating, reason, comment } }
            : message,
        ),
      );
    },
    [api, updateMessages],
  );

  const clearError = () => setError("");
  const messages = selectedConnectionId
    ? messagesByConnection[selectedConnectionId] ?? []
    : [];

  return {
    selectedConnectionId,
    messages,
    loadingHistory,
    sending,
    status,
    error,
    selectConnection,
    submit,
    stop,
    rateMessage,
    clearError,
  };
}

export function getTableColumns(rows: ResultRow[]) {
  const seen = new Set<string>();
  rows.forEach((row) => Object.keys(row).forEach((key) => seen.add(key)));
  return [...seen];
}
