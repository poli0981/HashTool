using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace HashTool.App.Infrastructure;

/// <summary>
/// Keeps one window per user: a second launch (for example one Explorer context-menu click per selected file) forwards its
/// files to the running instance over a local named pipe and exits. Elevated and normal instances are kept apart.
/// </summary>
public sealed partial class SingleInstance : IDisposable
{
    private const int MaxMessageBytes = 1024 * 1024;

    private readonly string _name;
    private readonly CancellationTokenSource _stop = new();
    private Mutex? _mutex;
    private FileStream? _lockFile;

    internal SingleInstance(string name) => _name = name;

    public static SingleInstance Create(bool elevated)
    {
        var identity = $"{Environment.UserDomainName}\\{Environment.UserName}";
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..12];
        return new SingleInstance($"HashTool-{hash}{(elevated ? "-adm" : "")}");
    }

    public bool TryBecomePrimary()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                _mutex = new Mutex(true, $@"Local\{_name}", out var createdNew);
                if (createdNew) return true;
                _mutex.Dispose();
                _mutex = null;
                return false;
            }

            var lockPath = Path.Combine(Path.GetTempPath(), $"{_name}.lock");
            _lockFile = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return true; // Cannot coordinate: behave like an independent instance.
        }
    }

    /// <summary>Sends the request to the primary instance; false if it could not be reached.</summary>
    public bool TryForward(ActivationRequest? request, TimeSpan timeout)
    {
        request ??= new ActivationRequest(ActivationVerb.Auto, []);
        var deadline = DateTime.UtcNow + timeout;
        var delay = 50;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", _name, PipeDirection.Out, PipeOptions.CurrentUserOnly);
                client.Connect(Math.Max(100, (int)(deadline - DateTime.UtcNow).TotalMilliseconds));
                if (OperatingSystem.IsWindows()) AllowSetForegroundWindow(-1);
                var payload = JsonSerializer.SerializeToUtf8Bytes(new PipeMessage(request.Verb, [.. request.Paths]), PipeJsonContext.Default.PipeMessage);
                Span<byte> header = stackalloc byte[4];
                BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
                client.Write(header);
                client.Write(payload);
                client.Flush();
                return true;
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
            {
                Thread.Sleep(delay);
                delay = Math.Min(delay * 2, 400);
            }
        }

        return false;
    }

    /// <summary>Listens for requests from later launches.</summary>
    public void StartServer(Action<ActivationRequest> onRequest)
    {
        _ = Task.Run(async () =>
        {
            NamedPipeServerStream? current = null, next = null;
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    current = next ?? CreateServer();
                    next = null;
                    await current.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                    // Listen again before the connected instance is released. On Linux and macOS the pipe is a Unix
                    // socket that closes, dropping connections already queued on it, once no server instance exists.
                    next = CreateServer();
                    var connected = current;
                    current = null;
                    _ = HandleAsync(connected, onRequest);
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception)
                {
                    // One bad or broken connection must never stop the listener: keep serving later launches.
                    current?.Dispose();
                    current = null;
                    // Not the stop token: a cancelled delay would throw out of this handler and skip the cleanup below.
                    await Task.Delay(200, CancellationToken.None).ConfigureAwait(false);
                }
            }

            current?.Dispose();
            next?.Dispose();
        }, _stop.Token);
    }

    private NamedPipeServerStream CreateServer() =>
        new(_name, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private async Task HandleAsync(NamedPipeServerStream server, Action<ActivationRequest> onRequest)
    {
        await using (server.ConfigureAwait(false))
        {
            try
            {
                var request = await ReadAsync(server, _stop.Token).ConfigureAwait(false);
                if (request is not null) onRequest(request);
            }
            catch (Exception)
            {
                // A client that disconnects early or sends garbage is ignored.
            }
        }
    }

    private static async Task<ActivationRequest?> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is <= 0 or > MaxMessageBytes) return null;
        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        var message = JsonSerializer.Deserialize(payload, PipeJsonContext.Default.PipeMessage);
        if (message?.Paths is null) return null;
        return new ActivationRequest(message.Verb, message.Paths.Length > 10_000 ? message.Paths[..10_000] : message.Paths);
    }

    public void Dispose()
    {
        _stop.Cancel();
        try
        {
            _mutex?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
        }

        _mutex?.Dispose();
        _lockFile?.Dispose();
        _stop.Dispose();
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(int processId);

    /// <summary>Wire format; a single constructor so the source-generated serializer can create it.</summary>
    internal sealed record PipeMessage(ActivationVerb Verb, string[] Paths);
}

[JsonSerializable(typeof(SingleInstance.PipeMessage))]
internal sealed partial class PipeJsonContext : JsonSerializerContext;
