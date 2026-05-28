using System;
using System.Threading;

namespace Romestead.MapWorkshop;

/// <summary>
/// Thread-safe sink the long-running operations write to. Implementations are
/// expected to marshal log lines back to the UI thread.
/// </summary>
internal interface IProgressSink
{
    void Log(string line);
    void Status(string text);
    CancellationToken CancellationToken { get; }
}

/// <summary>
/// Bridges an IProgressSink to ad-hoc callbacks. Used by tests and for
/// composing nested operations.
/// </summary>
internal sealed class DelegatingSink : IProgressSink
{
    private readonly Action<string> _log;
    private readonly Action<string> _status;
    public DelegatingSink(Action<string> log, Action<string>? status = null, CancellationToken ct = default)
    {
        _log = log;
        _status = status ?? (_ => { });
        CancellationToken = ct;
    }
    public void Log(string line) => _log(line);
    public void Status(string text) => _status(text);
    public CancellationToken CancellationToken { get; }
}
