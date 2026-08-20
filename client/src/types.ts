export type Role = "user" | "assistant";
export type Rating = "up" | "down";

export interface ConnectionSummary {
  id: string;
  displayName: string;
}

export interface Feedback {
  rating: Rating;
  reason?: string;
  comment?: string;
}

export interface ResultRow {
  [key: string]: unknown;
}

export interface ChatMessage {
  id: string;
  role: Role;
  content: string;
  createdAt: string;
  rows?: ResultRow[];
  feedback?: Feedback;
  matchedTerms?: string[];
  matchedTables?: string[];
  streaming?: boolean;
  interrupted?: boolean;
}

export interface GlossaryEntry {
  connectionId: string;
  table: string;
  businessTerm: string;
  description: string;
  synonyms: string[];
  relatedColumns: string[];
  joinHints: string[];
}

export interface FeedbackReviewItem {
  id: string;
  connectionId: string;
  question: string;
  matchedTerms: string[];
  matchedTables?: string[];
  reason?: string;
  comment?: string;
  createdAt: string;
}

export interface AccuracyReport {
  thumbsUpRate: number;
  thumbsDownRate: number;
  feedbackCoverage: number;
  answerCount?: number;
  ratedCount?: number;
  totalAssistantMessages?: number;
  from?: string | null;
  to?: string | null;
  dateRange?: {
    from?: string | null;
    to?: string | null;
  };
}

export type StreamEvent =
  | { type: "status"; data: string }
  | { type: "answer"; data: string }
  | { type: "rows"; data: ResultRow[] }
  | {
      type: "complete";
      data?: {
        id?: string;
        messageId?: string;
        matchedTerms?: string[];
        matchedTables?: string[];
      };
    }
  | { type: "error"; data: string };

export type AppView = "ask" | "glossary" | "feedback" | "accuracy";
