using SystemIQ.Worker;

namespace SystemIQ.Api.Tests;

public sealed class WorkerHostTests
{
    [Fact]
    public async Task Unknown_command_has_stable_nonzero_exit_code()
    {
        var error = new StringWriter();
        Assert.Equal(WorkerHost.InvalidArguments, await WorkerHost.RunAsync(["unknown"], default, error: error));
        Assert.Contains("Unknown", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Registered_command_runs_once()
    {
        var command = new StubCommand();
        Assert.Equal(0, await WorkerHost.RunAsync(["process-feedback"], default, [command]));
        Assert.Equal(1, command.Executions);
    }

    private sealed class StubCommand : IOneShotCommand
    {
        public string Name => "process-feedback";
        public int Executions { get; private set; }
        public Task<int> ExecuteAsync(CancellationToken cancellationToken) { Executions++; return Task.FromResult(0); }
    }
}
