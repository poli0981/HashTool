using System;
using System.Collections.Concurrent;
using Avalonia.Threading;
using HashTool.App.Infrastructure;

namespace HashTool.App.Services;

/// <summary>Queues incoming files until the main window is ready, then delivers them on the UI thread.</summary>
public sealed class ActivationService
{
    private readonly ConcurrentQueue<ActivationRequest> _pending = new();
    private Action<ActivationRequest>? _handler;

    public void Enqueue(ActivationRequest? request)
    {
        if (request is null || request.Paths.Count == 0) return;
        _pending.Enqueue(request);
        if (_handler is not null) Dispatcher.UIThread.Post(Drain);
    }

    /// <summary>Starts delivering requests (including those received before the UI existed).</summary>
    public void Attach(Action<ActivationRequest> handler)
    {
        _handler = handler;
        Dispatcher.UIThread.Post(Drain);
    }

    private void Drain()
    {
        while (_handler is not null && _pending.TryDequeue(out var request)) _handler(request);
    }
}
