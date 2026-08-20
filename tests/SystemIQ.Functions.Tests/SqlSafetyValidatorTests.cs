using SystemIQ.Functions.Services;

namespace SystemIQ.Functions.Tests;

public sealed class SqlSafetyValidatorTests
{
    private readonly SqlSafetyValidator _subject = new();

    [Theory]
    [InlineData("SELECT TOP (10) [Id] FROM [dbo].[Members]")]
    [InlineData("WITH active AS (SELECT Id FROM Members) SELECT Id FROM active")]
    public void Allows_single_read_only_query(string sql) => _subject.EnsureReadOnly(sql);

    [Theory]
    [InlineData("UPDATE Members SET Name = 'x'")]
    [InlineData("SELECT * INTO Copy FROM Members")]
    [InlineData("SELECT * FROM Members; DELETE FROM Members")]
    [InlineData("SELECT * FROM Members -- hide mutation")]
    [InlineData("DROP TABLE Members")]
    public void Blocks_mutating_or_obfuscated_query(string sql) =>
        Assert.Throws<UnauthorizedAccessException>(() => _subject.EnsureReadOnly(sql));

    [Fact]
    public void Extracts_tables_across_indirect_join_paths()
    {
        var tables = SqlSafetyValidator.ReferencedIdentifiers(
            "SELECT m.Id FROM dbo.Members m JOIN [dbo].[RestrictedClaims] c ON c.MemberId=m.Id");
        Assert.Contains("Members", tables);
        Assert.Contains("RestrictedClaims", tables);
    }

    [Theory]
    [InlineData("SELECT p.SSN AS value FROM dbo.Patients p", "SSN")]
    [InlineData("SELECT [SSN] encrypted_value FROM dbo.Patients", "SSN")]
    [InlineData("SELECT p.MemberId FROM dbo.Patients p", "MemberId")]
    public void Finds_denied_identifiers_even_when_aliased(string sql, string identifier) =>
        Assert.True(SqlSafetyValidator.ReferencesIdentifier(sql, identifier));

    [Theory]
    [InlineData("SELECT * FROM dbo.Patients")]
    [InlineData("SELECT p.Id, p.* FROM dbo.Patients p")]
    [InlineData("SELECT TOP (10) * FROM dbo.Patients")]
    [InlineData("SELECT DISTINCT * FROM dbo.Patients")]
    [InlineData("SELECT TOP 1 p.* FROM dbo.Patients p")]
    public void Finds_wildcard_projection_that_could_expose_denied_columns(string sql) =>
        Assert.True(SqlSafetyValidator.SelectsWildcard(sql));

    [Fact]
    public void Does_not_treat_count_wildcard_as_row_projection() =>
        Assert.False(SqlSafetyValidator.SelectsWildcard("SELECT COUNT(*) FROM dbo.Patients"));
}
