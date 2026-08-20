# SystemIQ API contract

All endpoints require an authenticated Azure AD bearer token. Admin endpoints also
require the `DataIqGlossaryEditor` app role.

## User endpoints

- `GET /api/connections` returns `ConnectionSummary[]`.
- `GET /api/history/{connectionId}` returns `ChatMessage[]`.
- `POST /api/chat/stream` accepts `{ connectionId, question }` and returns
  server-sent events named `status`, `answer`, `rows`, `complete`, or `error`.
- `POST /api/feedback` accepts `{ connectionId, messageId, rating, reason?, comment? }`.

## Admin endpoints

- `GET /api/admin/glossary/{connectionId}` returns `GlossaryEntry[]`.
- `GET /api/admin/glossary/{connectionId}/defaults` returns schema-derived
  `GlossaryEntry[]` for every live table.
- `PUT /api/admin/glossary/{connectionId}/{table}` upserts a glossary entry.
- `GET /api/admin/feedback?connectionId=...` returns pending review items.
- `POST /api/admin/feedback/process` processes queued negative feedback.
- `POST /api/admin/feedback/{id}/resolve` resolves a review item.
- `GET /api/admin/accuracy-report?days=30` returns thumbs-up/down and coverage
  rates for the requested rolling period. Omitting `days` reports all history.

## Core shapes

`ConnectionSummary`: `{ id, displayName }`

`ChatMessage`: `{ id, role, content, createdAt, rows?, feedback?, matchedTerms? }`

`GlossaryEntry`: `{ connectionId, table, businessTerm, description, synonyms,
relatedColumns, joinHints }`

Percentages are returned as numbers in the inclusive range 0–100.
