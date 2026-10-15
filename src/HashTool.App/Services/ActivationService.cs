using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Threading;
using HashTool.App.Infrastructure;
using Microsoft.Extensions.Logging;

namespace HashTool.App.Services;

/// <summary>
/// Queues incoming files until the main window is ready, then delivers them on the UI thread one request at a time.
/// Requests that arrive together (Explorer starts one process per selected file) are merged into one.
/// </summary>
public sealed class ActivationService(ILogger<ActivationService> logger)
{
    private readonly ConcurrentQueue<ActivationRequest> _pending = new();
    private Func<ActivationRequest, Task>? _handler;
    private bool _draining; // UI thread only

    public void Enqueue(ActivationRequest? request)
    {
        if (request is null || request.Paths.Count == 0) return;
        _pending.Enqueue(request);
        if (_handler is not null) Dispatcher.UIThread.Post(() => _ = DrainAsync());
    }

    /// <summary>Starts delivering requests (including those received before the UI existed).</summary>
    public void Attach(Func<ActivationRequest, Task> handler)
    {
        _handler = handler;
        Dispatcher.UIThread.Post(() => _ = DrainAsync());
    }

    private async Task DrainAsync()
    {
        if (_draining || _handler is null) return;
        _draining = true;
        try
        {
            // Only this loop dequeues, so a peeked request is still there when it is taken.
            while (_pending.TryDequeue(out var first))
            {
                // Explicit verbs (Explorer's Generate/Verify entries) merge; "Auto" requests are routed by their own content.
                var paths = new List<string>(first.Paths);
                while (first.Verb != ActivationVerb.Auto && _pending.TryPeek(out var next) && next.Verb == first.Verb &&
                       _pending.TryDequeue(out next))
                    paths.AddRange(next.Paths);

                try
                {
                    await _handler(new ActivationRequest(first.Verb, paths));
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Opening {Count} files from another launch failed", paths.Count);
                }
            }
        }
        finally
        {
            _draining = false;
        }
    }
}
