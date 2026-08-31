import { useEffect, useMemo, useState } from "react";
import { AccuracyReport } from "./components/AccuracyReport";
import { ChatPanel } from "./components/ChatPanel";
import { FeedbackInbox } from "./components/FeedbackInbox";
import { GlossaryAdmin } from "./components/GlossaryAdmin";
import {
  BookIcon,
  ChartIcon,
  ChatIcon,
  DatabaseIcon,
  InboxIcon,
  SparkIcon,
} from "./components/Icons";
import { useChatController } from "./hooks/useChatController";
import { ApiClient } from "./services/api";
import {
  authService,
  isAuthConfigured,
  isDevelopmentAuth,
  type UserIdentity,
} from "./services/auth";
import type { AppView, ConnectionSummary } from "./types";

interface GlossaryTarget {
  connectionId: string;
  term: string;
  table?: string;
}

export default function App() {
  const api = useMemo(() => new ApiClient(authService), []);
  const [user, setUser] = useState<UserIdentity | null>(null);
  const [authReady, setAuthReady] = useState(false);
  const [authError, setAuthError] = useState("");
  const [connections, setConnections] = useState<ConnectionSummary[]>([]);
  const [connectionsLoading, setConnectionsLoading] = useState(false);
  const [connectionsError, setConnectionsError] = useState("");
  const [view, setView] = useState<AppView>("ask");
  const [glossaryTarget, setGlossaryTarget] = useState<GlossaryTarget | null>(null);
  const chat = useChatController(api);

  useEffect(() => {
    void authService
      .initialize()
      .then(() => {
        const existing = authService.getUser();
        if (existing) setUser(existing);
      })
      .catch((cause) =>
        setAuthError(cause instanceof Error ? cause.message : "Sign-in could not be initialized."),
      )
      .finally(() => setAuthReady(true));
  }, []);

  useEffect(() => {
    if (!user) return;
    const controller = new AbortController();
    setConnectionsLoading(true);
    setConnectionsError("");
    void api
      .getConnections(controller.signal)
      .then((result) => {
        if (controller.signal.aborted) return;
        setConnections(result);
        if (result[0] && !chat.selectedConnectionId) {
          void chat.selectConnection(result[0].id);
        }
      })
      .catch((cause) => {
        if (!controller.signal.aborted)
          setConnectionsError(
            cause instanceof Error ? cause.message : "Connections could not be loaded.",
          );
      })
      .finally(() => {
        if (!controller.signal.aborted) setConnectionsLoading(false);
      });
    return () => controller.abort();
    // selectConnection is intentionally omitted: the connection catalog owns initial selection.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [api, user]);

  const signIn = async () => {
    setAuthError("");
    try {
      setUser(await authService.signIn());
    } catch (cause) {
      setAuthError(cause instanceof Error ? cause.message : "Sign-in was cancelled.");
    }
  };

  const signOut = async () => {
    await authService.signOut();
    setConnections([]);
    setUser(null);
  };

  if (!authReady) {
    return <FullPageStatus message="Preparing your secure workspace…" />;
  }
  if (!user) {
    return (
      <main className="sign-in-page">
        <section className="sign-in-card">
          <div className="brand-mark large"><SparkIcon /></div>
          <span className="eyebrow">SystemIQ</span>
          <h1>Answers from your data,<br />in plain language.</h1>
          <p>
            SystemIQ turns everyday business questions into clear, traceable answers
            from the databases you’re allowed to access.
          </p>
          <button
            className="button button-primary sign-in-button"
            disabled={!isAuthConfigured && !isDevelopmentAuth}
            onClick={() => void signIn()}
          >
            {isAuthConfigured ? "Sign in with Microsoft" : isDevelopmentAuth ? "Enter development workspace" : "Sign-in unavailable"}
          </button>
          {authError && <div className="error-banner" role="alert">{authError}</div>}
          <small>Access is protected by your organization’s identity and data policies.</small>
        </section>
        <div className="sign-in-decoration" aria-hidden="true"><span /><span /><span /></div>
      </main>
    );
  }

  const selectedConnection = connections.find(
    (connection) => connection.id === chat.selectedConnectionId,
  );

  const openGlossaryTerm = (connectionId: string, term: string, table?: string) => {
    setGlossaryTarget({ connectionId, term, table });
    setView("glossary");
  };

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <div className="brand">
          <div className="brand-mark"><SparkIcon /></div>
          <div><strong>SystemIQ</strong><span>Natural Language SQL</span></div>
        </div>
        <nav className="main-nav" aria-label="Primary navigation">
          <NavButton active={view === "ask"} icon={<ChatIcon />} onClick={() => setView("ask")}>
            Ask SystemIQ
          </NavButton>
          {user.isCurator && (
            <>
              <div className="nav-section-label">Curator</div>
              <NavButton active={view === "glossary"} icon={<BookIcon />} onClick={() => setView("glossary")}>
                Business glossary
              </NavButton>
              <NavButton active={view === "feedback"} icon={<InboxIcon />} onClick={() => setView("feedback")}>
                Feedback inbox
              </NavButton>
              <NavButton active={view === "accuracy"} icon={<ChartIcon />} onClick={() => setView("accuracy")}>
                Accuracy report
              </NavButton>
            </>
          )}
        </nav>
        <div className="sidebar-footer">
          <div className="user-avatar">{initials(user.name)}</div>
          <div className="user-details"><strong>{user.name}</strong><span>{user.username}</span></div>
          <button className="sign-out" onClick={() => void signOut()}>Sign out</button>
        </div>
      </aside>
      <div className="content-shell">
        {user.isDevelopment && (
          <div className="dev-banner" role="status">
            Development mode · authentication bypass is active
          </div>
        )}
        {view === "ask" && (
          <main className="ask-page">
            <header className="topbar">
              <div>
                <span className="eyebrow">Workspace</span>
                <h1>Ask your data</h1>
              </div>
              <label className="connection-selector">
                <DatabaseIcon />
                <span>
                  <small>Data connection</small>
                  {connectionsLoading ? (
                    <strong>Loading…</strong>
                  ) : (
                    <select
                      value={chat.selectedConnectionId}
                      disabled={!connections.length}
                      onChange={(event) => void chat.selectConnection(event.target.value)}
                    >
                      {!connections.length && <option value="">No connections</option>}
                      {connections.map((connection) => (
                        <option key={connection.id} value={connection.id}>
                          {connection.displayName}
                        </option>
                      ))}
                    </select>
                  )}
                </span>
              </label>
            </header>
            {connectionsError ? (
              <ConnectionError message={connectionsError} />
            ) : !connectionsLoading && connections.length === 0 ? (
              <NoConnections />
            ) : (
              <ChatPanel
                connection={selectedConnection}
                messages={chat.messages}
                loading={chat.loadingHistory}
                sending={chat.sending}
                status={chat.status}
                error={chat.error}
                disabled={!selectedConnection}
                onSubmit={chat.submit}
                onStop={chat.stop}
                onRate={chat.rateMessage}
              />
            )}
          </main>
        )}
        {view === "glossary" && user.isCurator && (
          <GlossaryAdmin
            api={api}
            connections={connections}
            initialConnectionId={glossaryTarget?.connectionId}
            initialTerm={glossaryTarget?.term}
            initialTable={glossaryTarget?.table}
          />
        )}
        {view === "feedback" && user.isCurator && (
          <FeedbackInbox api={api} connections={connections} onEditTerm={openGlossaryTerm} />
        )}
        {view === "accuracy" && user.isCurator && <AccuracyReport api={api} />}
      </div>
    </div>
  );
}

function NavButton({
  active,
  icon,
  onClick,
  children,
}: {
  active: boolean;
  icon: React.ReactNode;
  onClick: () => void;
  children: React.ReactNode;
}) {
  return (
    <button className={active ? "active" : ""} aria-current={active ? "page" : undefined} onClick={onClick}>
      {icon}<span>{children}</span>
    </button>
  );
}

function NoConnections() {
  return (
    <section className="chat-empty card">
      <div className="empty-art"><DatabaseIcon /></div>
      <h2>No data connections available</h2>
      <p>Your current access policy does not include a database connection.</p>
      <p className="empty-help">Contact your SystemIQ administrator to request access.</p>
      <button className="button button-secondary" disabled>Chat unavailable</button>
    </section>
  );
}

function ConnectionError({ message }: { message: string }) {
  return (
    <section className="chat-empty card">
      <div className="error-banner" role="alert">{message}</div>
      <h2>Connections are temporarily unavailable</h2>
      <p>Try refreshing the page. If this continues, contact your SystemIQ administrator.</p>
    </section>
  );
}

function FullPageStatus({ message }: { message: string }) {
  return (
    <main className="full-page-status">
      <div className="brand-mark large"><SparkIcon /></div>
      <span className="spinner" />
      <p>{message}</p>
    </main>
  );
}

const initials = (name: string) =>
  name.split(/\s+/).slice(0, 2).map((part) => part[0]).join("").toUpperCase();
