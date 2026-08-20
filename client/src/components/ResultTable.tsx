import { getTableColumns } from "../hooks/useChatController";
import type { ResultRow } from "../types";

export function ResultTable({ rows }: { rows: ResultRow[] }) {
  if (!rows.length) {
    return (
      <div className="no-results" role="status">
        No matching records were found.
      </div>
    );
  }
  const columns = getTableColumns(rows);
  return (
    <div className="result-table-wrap" tabIndex={0} aria-label="Query results">
      <table className="result-table">
        <caption className="sr-only">{rows.length} query result rows</caption>
        <thead>
          <tr>{columns.map((column) => <th key={column}>{column}</th>)}</tr>
        </thead>
        <tbody>
          {rows.map((row, index) => (
            <tr key={index}>
              {columns.map((column) => (
                <td key={column}>{formatCell(row[column])}</td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

const formatCell = (value: unknown) => {
  if (value === null || value === undefined) return "—";
  if (typeof value === "object") return JSON.stringify(value);
  if (typeof value === "boolean") return value ? "Yes" : "No";
  return String(value);
};
