import { useState } from "react";
import type { Feedback, Rating } from "../types";
import { ThumbDownIcon, ThumbUpIcon } from "./Icons";

interface Props {
  value?: Feedback;
  disabled?: boolean;
  onSubmit: (rating: Rating, reason?: string, comment?: string) => Promise<void>;
}

export function FeedbackControl({ value, disabled, onSubmit }: Props) {
  const [rating, setRating] = useState<Rating | null>(null);
  const [reason, setReason] = useState("");
  const [comment, setComment] = useState("");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  if (value) {
    return (
      <span className="feedback-saved">
        {value.rating === "up" ? "Thanks — helpful" : "Thanks — feedback recorded"}
      </span>
    );
  }

  const submit = async (next: Rating, withDetails = false) => {
    if (next === "down" && !withDetails) {
      setRating("down");
      return;
    }
    setSaving(true);
    setError("");
    try {
      await onSubmit(next, reason || undefined, comment.trim() || undefined);
      setRating(null);
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Feedback could not be saved.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="feedback-control">
      <span>Was this useful?</span>
      <button
        className="icon-button"
        aria-label="Mark answer as helpful"
        disabled={disabled || saving}
        onClick={() => void submit("up")}
      >
        <ThumbUpIcon />
      </button>
      <button
        className="icon-button"
        aria-label="Mark answer as not helpful"
        aria-expanded={rating === "down"}
        disabled={disabled || saving}
        onClick={() => setRating(rating === "down" ? null : "down")}
      >
        <ThumbDownIcon />
      </button>
      {rating === "down" && (
        <div className="feedback-form">
          <label>
            What went wrong?
            <select value={reason} onChange={(event) => setReason(event.target.value)}>
              <option value="">Select a reason (optional)</option>
              <option value="incorrect">The answer was incorrect</option>
              <option value="missing-data">Important data was missing</option>
              <option value="misunderstood">My question was misunderstood</option>
              <option value="other">Something else</option>
            </select>
          </label>
          <label>
            Tell us more (optional)
            <textarea
              rows={3}
              maxLength={1000}
              value={comment}
              onChange={(event) => setComment(event.target.value)}
              placeholder="Do not include personal, confidential, or identifying information."
            />
          </label>
          <div className="feedback-form-actions">
            <button className="button button-secondary" onClick={() => setRating(null)}>
              Cancel
            </button>
            <button
              className="button button-primary"
              disabled={saving}
              onClick={() => void submit("down", true)}
            >
              {saving ? "Sending…" : "Send feedback"}
            </button>
          </div>
        </div>
      )}
      {error && <span className="inline-error">{error}</span>}
    </div>
  );
}
