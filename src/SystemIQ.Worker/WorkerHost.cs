namespace SystemIQ.Worker;

public interface IOneShotCommand
{
    string Name { get; }
    Task<int> ExecuteAsync(CancellationToken cancellationToken);
}

public sealed class UnavailableOneShotCommand(string name) : IOneShotCommand
{
    public string Name { get; } = name;
    public Task<int> ExecuteAsync(CancellationToken cancellationToken) => Task.FromResult(WorkerHost.IncompleteRun);
}

public static class WorkerHost
{
    public const int Success = 0;
    public const int InvalidArguments = 2;
    public const int IncompleteRun = 3;

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken,
        IEnumerable<IOneShotCommand>? commands = null, TextWriter? error = null)
    {
        error ??= Console.Error;
        if (args.Length != 1)
        {
            await error.WriteLineAsync("Usage: SystemIQ.Worker <process-feedback|reindex-rag>");
            return InvalidArguments;
        }

        var command = (commands ?? []).SingleOrDefault(x => x.Name.Equals(args[0], StringComparison.OrdinalIgnoreCase));
        if (command is null)
        {
            await error.WriteLineAsync($"Unknown or unavailable command '{args[0]}'.");
            return InvalidArguments;
        }

        try { return await command.ExecuteAsync(cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return IncompleteRun; }
        catch (Exception exception)
        {
            await error.WriteLineAsync($"Command '{command.Name}' did not complete: {exception.GetType().Name}.");
            return IncompleteRun;
        }
    }
}
