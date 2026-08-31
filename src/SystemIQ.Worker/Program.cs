using SystemIQ.Worker;

return await WorkerHost.RunAsync(args, CancellationToken.None,
[
    new UnavailableOneShotCommand("process-feedback"),
    new UnavailableOneShotCommand("reindex-rag")
]);
