using Pamac;

using var daemon = new Daemon();
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, args) =>
{
    args.Cancel = true;
    cancellation.Cancel();
};
Console.WriteLine($"pamac-daemon ready ({daemon.Sender})");
try
{
    await Task.Delay(Timeout.InfiniteTimeSpan, cancellation.Token);
}
catch (OperationCanceledException)
{
    // Normal shutdown.
}
return 0;
