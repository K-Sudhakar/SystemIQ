import { FormEvent, useEffect, useRef, useState } from "react";
import type { ConnectionSummary, ChatMessage, Rating } from "../types";
import { ChatIcon, DatabaseIcon, SendIcon, SparkIcon } from "./Icons";
import { ResultTable } from "./ResultTable";
import { FeedbackControl } from "./FeedbackControl";

interface Props {
  connection?: ConnectionSummary;
  messages: ChatMessage[];
  loading: boolean;
  sending: boolean;
  status: string;
  error: string;
  disabled: boolean;
  onSubmit: (question: string) => Promise<void>;
  onStop: () => void;
  onRate: (
    messageId: string,
    rating: Rating,
    reason?: string,
    comment?: string,
  ) => Promise<void>;
}

export function ChatPanel(props: Props) {
  const [question, setQuestion] = useState("");
  const endRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    endRef.current?.scrollIntoView({ behavior: "smooth", block: "end" });
  }, [props.messages, props.status]);

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const value = question.trim();
    if (!value || props.sending || props.disabled) return;
    setQuestion("");
    void props.onSubmit(value);
  };

  if (!props.connection) {
    return (
      <section className="chat-empty card" aria-labelledby="no-connection-title">
        <div className="empty-art"><DatabaseIcon /></div>
        <h2 id="no-connection-title">Choose a data connection</h2>
        <p>Select an available database above to start asking questions.</p>
      </section>
    );
  }

  return (
    <section className="chat-panel" aria-label="Data assistant conversation">
      <div className="chat-header">
        <div>
          <span className="eyebrow">Connected to</span>
          <h2>{props.connection.displayName}</h2>
        </div>
        <span className="secure-indicator"><span /> Read-only access</span>
      </div>
      <div className="messages" aria-live="polite" aria-busy={props.sending}>
        {props.loading ? (
          <div className="history-loading"><span className="spinner" /> Loading conversation…</div>
        ) : props.messages.length === 0 ? (
          <Welcome connectionName={props.connection.displayName} />
        ) : (
          props.messages.map((message) => (
            <Message
              key={message.id}
              message={message}
              onRate={(rating, reason, comment) =>
                props.onRate(message.id, rating, reason, comment)
              }
            />
          ))
        )}
        {props.status && <div className="stream-status"><span className="spinner" /> {props.status}</div>}
        <div ref={endRef} />
      </div>
      {props.error && (
        <div className="error-banner" role="alert">
          {props.error}
        </div>
      )}
      <form className="composer" onSubmit={submit}>
        <label className="sr-only" htmlFor="question">Ask a question</label>
        <textarea
          id="question"
          rows={2}
          value={question}
          onChange={(event) => setQuestion(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === "Enter" && !event.shiftKey) {
              event.preventDefault();
              event.currentTarget.form?.requestSubmit();
            }
          }}
          disabled={props.disabled}
          placeholder={
            props.disabled
              ? "Select an available connection to ask a question"
              : `Ask ${props.connection.displayName} in plain English…`
          }
        />
        {props.sending ? (
          <button type="button" className="stop-button" onClick={props.onStop}>
            <span /> Stop
          </button>
        ) : (
          <button
            className="send-button"
            type="submit"
            disabled={!question.trim() || props.disabled}
            aria-label="Send question"
          >
            <SendIcon />
          </button>
        )}
        <p className="composer-hint">
          AI-generated answers may be inaccurate. Verify critical results.
        </p>
      </form>
    </section>
  );
}

function Welcome({ connectionName }: { connectionName: string }) {
  return (
    <div className="welcome">
      <div className="welcome-icon"><SparkIcon /></div>
      <span className="eyebrow">DataIQ assistant</span>
      <h1>What would you like to know?</h1>
      <p>
        Ask a business question about <strong>{connectionName}</strong>. I’ll find the
        relevant data and explain the result.
      </p>
      <div className="prompt-examples">
        <span>Try asking</span>
        <div>“How many appointments were completed last month?”</div>
        <div>“Show the trend by week.”</div>
      </div>
    </div>
  );
}

function Message({
  message,
  onRate,
}: {
  message: ChatMessage;
  onRate: (rating: Rating, reason?: string, comment?: string) => Promise<void>;
}) {
  return (
    <article className={`message message-${message.role}`}>
      <div className="message-avatar">
        {message.role === "assistant" ? <SparkIcon /> : <ChatIcon />}
      </div>
      <div className="message-body">
        <div className="message-meta">
          <strong>{message.role === "assistant" ? "DataIQ" : "You"}</strong>
          <time dateTime={message.createdAt}>
            {new Date(message.createdAt).toLocaleTimeString([], {
              hour: "numeric",
              minute: "2-digit",
            })}
          </time>
        </div>
        <div className="message-content">
          {message.content || (message.streaming && <span className="typing-dots">•••</span>)}
        </div>
        {message.rows && <ResultTable rows={message.rows} />}
        {message.interrupted && (
          <span className="interrupted-note">Response incomplete — try asking again.</span>
        )}
        {message.role === "assistant" && !message.streaming && !message.interrupted && (
          <FeedbackControl value={message.feedback} onSubmit={onRate} />
        )}
      </div>
    </article>
  );
}
