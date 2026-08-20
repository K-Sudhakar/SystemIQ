using SystemIQ.Functions.Services;

namespace SystemIQ.Functions.Tests;

public sealed class ChatOrchestratorTests
{
    [Fact]
    public void Extracts_sql_from_markdown_fence()
    {
        var sql = ChatOrchestrator.ExtractSql("```sql\nSELECT Id FROM Members\n```");
        Assert.Equal("SELECT Id FROM Members", sql);
    }
}
