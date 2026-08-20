import { useCallback, useEffect, useState } from "react";
import type { ApiClient } from "../services/api";
import type { AccuracyReport as Report } from "../types";
import { ChartIcon } from "./Icons";
import { AdminPage } from "./GlossaryAdmin";

export function AccuracyReport({ api }: { api: ApiClient }) {
  const [report, setReport] = useState<Report | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");

  const load = useCallback(async (signal?: AbortSignal) => {
    setLoading(true);
    setError("");
    try {
      setReport(await api.getAccuracyReport(signal));
    } catch (cause) {
      if (!signal?.aborted)
        setError(cause instanceof Error ? cause.message : "Report could not be loaded.");
    } finally {
      if (!signal?.aborted) setLoading(false);
    }
  }, [api]);

  useEffect(() => {
    const controller = new AbortController();
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  const from = report?.from ?? report?.dateRange?.from;
  const to = report?.to ?? report?.dateRange?.to;

  return (
    <AdminPage
      icon={<ChartIcon />}
      eyebrow="Answer quality"
      title="Accuracy report"
      description="Track user sentiment and how representative the ratings are."
      actions={
        <button className="button button-secondary" disabled={loading} onClick={() => void load()}>
          {loading ? "Refreshing…" : "Refresh report"}
        </button>
      }
    >
      {error && <div className="error-banner" role="alert">{error}</div>}
      {loading && !report ? (
        <div className="admin-loading card"><span className="spinner" /> Calculating report…</div>
      ) : report ? (
        <>
          <section className="metric-grid" aria-label="Accuracy metrics">
            <Metric label="Thumbs-up rate" value={report.thumbsUpRate} tone="positive" />
            <Metric label="Thumbs-down rate" value={report.thumbsDownRate} tone="negative" />
            <Metric label="Feedback coverage" value={report.feedbackCoverage} tone="neutral" />
          </section>
          <section className="report-detail card">
            <div>
              <span className="eyebrow">Interpretation</span>
              <h2>Context for this baseline</h2>
              <p>
                Accuracy is represented by user ratings. Coverage shows the share of
                assistant answers that received a rating, so interpret sentiment alongside it.
              </p>
            </div>
            <dl>
              <div><dt>Rated answers</dt><dd>{report.ratedCount ?? "—"}</dd></div>
              <div><dt>Total answers</dt><dd>{report.answerCount ?? report.totalAssistantMessages ?? "—"}</dd></div>
              <div>
                <dt>Date range</dt>
                <dd>{from || to ? `${formatDate(from)} – ${formatDate(to)}` : "All available history"}</dd>
              </div>
            </dl>
          </section>
        </>
      ) : (
        <div className="small-empty large card">
          <ChartIcon /><h2>No history to report</h2><p>Metrics will appear after users receive answers.</p>
        </div>
      )}
    </AdminPage>
  );
}

function Metric({
  label,
  value,
  tone,
}: {
  label: string;
  value: number;
  tone: "positive" | "negative" | "neutral";
}) {
  const safeValue = Math.min(100, Math.max(0, Number(value) || 0));
  return (
    <article className={`metric-card card metric-${tone}`}>
      <span>{label}</span>
      <strong>{safeValue.toFixed(1)}%</strong>
      <div className="metric-track" aria-hidden="true">
        <div style={{ width: `${safeValue}%` }} />
      </div>
    </article>
  );
}

const formatDate = (value?: string | null) =>
  value ? new Date(value).toLocaleDateString() : "Start";
