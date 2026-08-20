import { useCallback, useEffect, useState } from "react";
import type { ApiClient } from "../services/api";
import type { ConnectionSummary, FeedbackReviewItem } from "../types";
import { InboxIcon } from "./Icons";
import { AdminPage } from "./GlossaryAdmin";

interface Props {
  api: ApiClient;
  connections: ConnectionSummary[];
  onEditTerm: (connectionId: string, term: string, table?: string) => void;
}

export function FeedbackInbox({ api, connections, onEditTerm }: Props) {
  const [connectionId, setConnectionId] = useState("");
  const [items, setItems] = useState<FeedbackReviewItem[]>([]);
  const [loading, setLoading] = useState(false);
  const [processing, setProcessing] = useState(false);
  const [error, setError] = useState("");

  const load = useCallback(
    async (signal?: AbortSignal) => {
      setLoading(true);
      setError("");
      try {
        setItems(await api.getFeedback(connectionId || undefined, signal));
      } catch (cause) {
        if (!signal?.aborted)
          setError(cause instanceof Error ? cause.message : "Feedback could not be loaded.");
      } finally {
        if (!signal?.aborted) setLoading(false);
      }
    },
    [api, connectionId],
  );

  useEffect(() => {
    const controller = new AbortController();
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  const process = async () => {
    setProcessing(true);
    setError("");
    try {
      await api.processFeedback();
      await load();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Feedback processing failed.");
    } finally {
      setProcessing(false);
    }
  };

  const resolve = async (id: string) => {
    try {
      await api.resolveFeedback(id);
      setItems((current) => current.filter((item) => item.id !== id));
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "The item could not be resolved.");
    }
  };

  return (
    <AdminPage
      icon={<InboxIcon />}
      eyebrow="Quality loop"
      title="Feedback inbox"
      description="Review answers that need clearer business context."
      connectionId={connectionId}
      connections={[{ id: "", displayName: "All connections" }, ...connections]}
      onConnectionChange={setConnectionId}
      actions={
        <button className="button button-primary" disabled={processing} onClick={() => void process()}>
          {processing ? "Processing…" : "Process new feedback"}
        </button>
      }
    >
      <section className="feedback-inbox card">
        <div className="panel-heading">
          <div><span className="eyebrow">Pending review</span><h2>{items.length} items</h2></div>
        </div>
        {error && <div className="error-banner" role="alert">{error}</div>}
        {loading ? (
          <div className="admin-loading"><span className="spinner" /> Loading feedback…</div>
        ) : items.length === 0 ? (
          <div className="small-empty large">
            <InboxIcon />
            <h2>You’re all caught up</h2>
            <p>There are no negative-feedback items awaiting review.</p>
          </div>
        ) : (
          <div className="review-list">
            {items.map((item) => (
              <article className="review-card" key={item.id}>
                <div className="review-meta">
                  <span>{connections.find((c) => c.id === item.connectionId)?.displayName || item.connectionId}</span>
                  <time dateTime={item.createdAt}>{new Date(item.createdAt).toLocaleDateString()}</time>
                </div>
                <h3>{item.question}</h3>
                {(item.reason || item.comment) && (
                  <blockquote>
                    {item.reason && <strong>{humanize(item.reason)}</strong>}
                    {item.comment && <p>{item.comment}</p>}
                  </blockquote>
                )}
                <div className="matched-terms">
                  <span>Matched terms</span>
                  {item.matchedTerms.length ? item.matchedTerms.map((term, index) => (
                    <button
                      key={`${term}-${item.matchedTables?.[index] ?? index}`}
                      className="term-chip"
                      onClick={() =>
                        onEditTerm(item.connectionId, term, item.matchedTables?.[index])
                      }
                    >
                      {term} <span aria-hidden="true">↗</span>
                    </button>
                  )) : <em>No glossary term recorded</em>}
                </div>
                <div className="review-actions">
                  {item.matchedTerms[0] && (
                    <button
                      className="button button-secondary"
                      onClick={() =>
                        onEditTerm(
                          item.connectionId,
                          item.matchedTerms[0],
                          item.matchedTables?.[0],
                        )
                      }
                    >
                      Edit matched term
                    </button>
                  )}
                  <button className="button button-quiet" onClick={() => void resolve(item.id)}>
                    Mark resolved
                  </button>
                </div>
              </article>
            ))}
          </div>
        )}
      </section>
    </AdminPage>
  );
}

const humanize = (value: string) =>
  value.replace(/[-_]/g, " ").replace(/^./, (letter) => letter.toUpperCase());
