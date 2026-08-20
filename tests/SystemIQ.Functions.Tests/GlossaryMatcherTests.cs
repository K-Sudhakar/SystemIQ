using SystemIQ.Functions.Models;
using SystemIQ.Functions.Services;

namespace SystemIQ.Functions.Tests;

public sealed class GlossaryMatcherTests
{
    [Fact]
    public void Matches_business_term_and_synonym()
    {
        var entry = Entry("members", ["patients"], ["MemberId"]);
        var result = GlossaryStore.Match("Show patients enrolled today", [entry]);
        Assert.Single(result);
        Assert.Equal("members", result[0].BusinessTerm);
    }

    [Fact]
    public void Suppresses_redundant_collision_that_adds_no_schema_coverage()
    {
        var specific = Entry("appointments", ["visits"], ["AppointmentId", "MemberId"]);
        var duplicate = Entry("appointments", ["visits"], ["AppointmentId"]);
        var result = GlossaryStore.Match("appointments by member", [specific, duplicate]);
        Assert.Single(result);
        Assert.Same(specific, result[0]);
    }

    [Fact]
    public void Tracks_all_columns_from_an_accepted_match()
    {
        var specific = Entry("appointments", ["visits"], ["AppointmentId", "MemberId"]);
        var redundant = Entry("appointments", ["visits"], ["MemberId"]);

        var result = GlossaryStore.Match("appointments by member", [specific, redundant]);

        Assert.Single(result);
        Assert.Same(specific, result[0]);
    }

    private static GlossaryEntry Entry(string term, IReadOnlyList<string> synonyms, IReadOnlyList<string> columns) =>
        new("mp3", term, term, "description", synonyms, columns, []);
}
