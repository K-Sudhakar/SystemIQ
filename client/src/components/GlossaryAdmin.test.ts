import { describe, expect, it } from "vitest";
import type { GlossaryEntry } from "../types";
import { resolveTermToTable } from "./GlossaryAdmin";

const entries: GlossaryEntry[] = [
  {
    connectionId: "mp3",
    table: "dbo.Appointments",
    businessTerm: "appointments",
    description: "Scheduled visits",
    synonyms: ["visits", "bookings"],
    relatedColumns: ["Status"],
    joinHints: [],
  },
  {
    connectionId: "mp3",
    table: "dbo.Members",
    businessTerm: "members",
    description: "Plan members",
    synonyms: ["patients"],
    relatedColumns: ["MemberId"],
    joinHints: [],
  },
];

describe("resolveTermToTable", () => {
  it("resolves a business term and synonym case-insensitively", () => {
    expect(resolveTermToTable(entries, "Appointments")).toBe("dbo.Appointments");
    expect(resolveTermToTable(entries, "VISITS")).toBe("dbo.Appointments");
  });

  it("accepts a table identifier and returns empty for an unknown term", () => {
    expect(resolveTermToTable(entries, "dbo.Members")).toBe("dbo.Members");
    expect(resolveTermToTable(entries, "claims")).toBe("");
  });
});
