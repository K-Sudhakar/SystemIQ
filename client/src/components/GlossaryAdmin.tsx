import { FormEvent, useEffect, useMemo, useState } from "react";
import type { ApiClient } from "../services/api";
import type { ConnectionSummary, GlossaryEntry } from "../types";
import { BookIcon } from "./Icons";

interface Props {
  api: ApiClient;
  connections: ConnectionSummary[];
  initialConnectionId?: string;
  initialTerm?: string;
  initialTable?: string;
  onTargetConsumed?: () => void;
}

const blankEntry = (connectionId: string): GlossaryEntry => ({
  connectionId,
  table: "",
  businessTerm: "",
  description: "",
  synonyms: [],
  relatedColumns: [],
  joinHints: [],
});

export function GlossaryAdmin({
  api,
  connections,
  initialConnectionId,
  initialTerm,
  initialTable,
  onTargetConsumed,
}: Props) {
  const [connectionId, setConnectionId] = useState(
    initialConnectionId || connections[0]?.id || "",
  );
  const [entries, setEntries] = useState<GlossaryEntry[]>([]);
  const [selectedTable, setSelectedTable] = useState("");
  const [draft, setDraft] = useState<GlossaryEntry | null>(null);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  useEffect(() => {
    if (initialConnectionId) setConnectionId(initialConnectionId);
  }, [initialConnectionId]);

  useEffect(() => {
    if (!connectionId) return;
    const controller = new AbortController();
    setLoading(true);
    setError("");
    void Promise.all([
      api.getGlossary(connectionId, controller.signal),
      api.getGlossaryDefaults(connectionId, controller.signal),
    ])
      .then(([curated, defaults]) => {
        if (controller.signal.aborted) return;
        const curatedByTable = new Map(
          curated.map((entry) => [entry.table.toLocaleLowerCase(), entry]),
        );
        const result = defaults
          .map((entry) => curatedByTable.get(entry.table.toLocaleLowerCase()) ?? entry)
          .concat(
            curated.filter(
              (entry) =>
                !defaults.some(
                  (candidate) =>
                    candidate.table.toLocaleLowerCase() ===
                    entry.table.toLocaleLowerCase(),
                ),
            ),
          )
          .sort((a, b) => a.table.localeCompare(b.table));
        setEntries(result);
        const target =
          initialTable && result.some((entry) => entry.table === initialTable)
            ? initialTable
            : initialTerm
              ? resolveTermToTable(result, initialTerm)
              : "";
        const nextTable = target || result[0]?.table || "";
        setSelectedTable(nextTable);
        setDraft(result.find((entry) => entry.table === nextTable) ?? null);
        if (target) onTargetConsumed?.();
      })
      .catch((cause) => {
        if (!controller.signal.aborted)
          setError(cause instanceof Error ? cause.message : "Glossary could not be loaded.");
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, [api, connectionId, initialTable, initialTerm, onTargetConsumed]);

  const selectTable = (table: string) => {
    setSelectedTable(table);
    setDraft(entries.find((entry) => entry.table === table) ?? null);
    setNotice("");
  };

  const startNew = () => {
    const next = blankEntry(connectionId);
    setSelectedTable("");
    setDraft(next);
    setNotice("");
  };

  const save = async (event: FormEvent) => {
    event.preventDefault();
    if (!draft) return;
    setSaving(true);
    setError("");
    setNotice("");
    try {
      const saved = await api.saveGlossary(draft);
      setEntries((current) => [
        ...current.filter((entry) => entry.table !== saved.table),
        saved,
      ].sort((a, b) => a.table.localeCompare(b.table)));
      setDraft(saved);
      setSelectedTable(saved.table);
      setNotice(`Saved “${saved.businessTerm}”.`);
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "The glossary entry could not be saved.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <AdminPage
      icon={<BookIcon />}
      eyebrow="Curator workspace"
      title="Business glossary"
      description="Shape how SystemIQ understands your organization’s language."
      connectionId={connectionId}
      connections={connections}
      onConnectionChange={setConnectionId}
    >
      <div className="glossary-layout">
        <aside className="table-list card">
          <div className="panel-heading">
            <div><span className="eyebrow">Schema</span><h2>Tables</h2></div>
            <button className="button button-secondary button-small" onClick={startNew}>
              Add entry
            </button>
          </div>
          {loading ? (
            <div className="admin-loading"><span className="spinner" /> Loading tables…</div>
          ) : entries.length === 0 ? (
            <div className="small-empty">No glossary entries yet. Add the first table.</div>
          ) : (
            <nav aria-label="Database tables">
              {entries.map((entry) => (
                <button
                  key={entry.table}
                  className={entry.table === selectedTable ? "active" : ""}
                  onClick={() => selectTable(entry.table)}
                >
                  <span>{entry.table}</span>
                  <small>{entry.businessTerm || "Auto-generated"}</small>
                </button>
              ))}
            </nav>
          )}
        </aside>
        <section className="glossary-editor card" aria-label="Glossary entry editor">
          {draft ? (
            <GlossaryForm
              entry={draft}
              isNew={!selectedTable}
              saving={saving}
              onChange={setDraft}
              onSubmit={save}
            />
          ) : (
            <div className="small-empty large">
              <BookIcon />
              <h2>Select a table</h2>
              <p>Choose a table to review its business term and schema hints.</p>
            </div>
          )}
          {notice && <div className="success-banner" role="status">{notice}</div>}
          {error && <div className="error-banner" role="alert">{error}</div>}
        </section>
      </div>
    </AdminPage>
  );
}

function GlossaryForm({
  entry,
  isNew,
  saving,
  onChange,
  onSubmit,
}: {
  entry: GlossaryEntry;
  isNew: boolean;
  saving: boolean;
  onChange: (entry: GlossaryEntry) => void;
  onSubmit: (event: FormEvent) => void;
}) {
  const field = (key: keyof GlossaryEntry, value: string | string[]) =>
    onChange({ ...entry, [key]: value });
  const list = (value: string) =>
    value.split(/[\n,]/).map((item) => item.trim()).filter(Boolean);

  return (
    <form onSubmit={onSubmit}>
      <div className="panel-heading">
        <div>
          <span className="eyebrow">{isNew ? "New entry" : "Editing table"}</span>
          <h2>{entry.table || "Define a table"}</h2>
        </div>
        <span className="draft-badge">Curated</span>
      </div>
      <div className="form-grid">
        <label>
          Table identifier
          <input
            value={entry.table}
            required
            disabled={!isNew}
            onChange={(event) => field("table", event.target.value)}
            placeholder="schema.TableName"
          />
        </label>
        <label>
          Business term
          <input
            value={entry.businessTerm}
            required
            onChange={(event) => field("businessTerm", event.target.value)}
            placeholder="Appointments"
          />
        </label>
        <label className="full">
          Description
          <textarea
            rows={4}
            value={entry.description}
            required
            onChange={(event) => field("description", event.target.value)}
            placeholder="Explain what this data represents in business language."
          />
        </label>
        <label className="full">
          Synonyms
          <input
            value={entry.synonyms.join(", ")}
            onChange={(event) => field("synonyms", list(event.target.value))}
            placeholder="visits, encounters, bookings"
          />
          <small>Separate values with commas.</small>
        </label>
        <label className="full">
          Related columns
          <textarea
            rows={3}
            value={entry.relatedColumns.join("\n")}
            onChange={(event) => field("relatedColumns", list(event.target.value))}
            placeholder={"AppointmentId\nScheduledDate\nStatus"}
          />
          <small>One schema column per line.</small>
        </label>
        <label className="full">
          Join hints
          <textarea
            rows={3}
            value={entry.joinHints.join("\n")}
            onChange={(event) => field("joinHints", list(event.target.value))}
            placeholder="Appointments.MemberId → Members.Id"
          />
          <small>One relationship per line.</small>
        </label>
      </div>
      <div className="form-actions">
        <button className="button button-primary" disabled={saving}>
          {saving ? "Saving…" : "Save glossary entry"}
        </button>
      </div>
    </form>
  );
}

export function resolveTermToTable(entries: GlossaryEntry[], term: string) {
  const normalized = term.trim().toLocaleLowerCase();
  const exact = entries.find((entry) =>
    [entry.businessTerm, ...entry.synonyms].some(
      (candidate) => candidate.trim().toLocaleLowerCase() === normalized,
    ),
  );
  if (exact) return exact.table;
  const tableMatch = entries.find(
    (entry) => entry.table.trim().toLocaleLowerCase() === normalized,
  );
  return tableMatch?.table ?? "";
}

interface AdminPageProps {
  icon: React.ReactNode;
  eyebrow: string;
  title: string;
  description: string;
  connectionId?: string;
  connections?: ConnectionSummary[];
  onConnectionChange?: (id: string) => void;
  actions?: React.ReactNode;
  children: React.ReactNode;
}

export function AdminPage(props: AdminPageProps) {
  return (
    <main className="admin-page">
      <header className="admin-header">
        <div className="admin-title-icon">{props.icon}</div>
        <div>
          <span className="eyebrow">{props.eyebrow}</span>
          <h1>{props.title}</h1>
          <p>{props.description}</p>
        </div>
        <div className="admin-header-actions">
          {props.connections && props.onConnectionChange && (
            <label className="compact-select">
              <span>Connection</span>
              <select
                value={props.connectionId}
                onChange={(event) => props.onConnectionChange?.(event.target.value)}
              >
                {props.connections.map((connection) => (
                  <option key={connection.id} value={connection.id}>
                    {connection.displayName}
                  </option>
                ))}
              </select>
            </label>
          )}
          {props.actions}
        </div>
      </header>
      {props.children}
    </main>
  );
}
