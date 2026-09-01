namespace SystemIQ.Domain.Glossary;

public sealed record GlossaryEntry(
    string ConnectionId,
    string Table,
    string BusinessTerm,
    string Description,
    IReadOnlyList<string> Synonyms,
    IReadOnlyList<string> RelatedColumns,
    IReadOnlyList<string> JoinHints);
