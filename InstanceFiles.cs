using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ScreenDraw;

sealed class InstanceFiles : IDisposable
{
    readonly CancellationTokenSource stop = new();
    readonly Task listener;
    public static string Name => "ScreenDraw.Files." + System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value
        + "." + System.Diagnostics.Process.GetCurrentProcess().SessionId;

    public InstanceFiles(string name, Action<string[]> receive)
    {
        listener = Listen(name, receive);
    }

    async Task Listen(string name, Action<string[]> receive)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stop.Token).ConfigureAwait(false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                timeout.CancelAfter(5000);
                using var reader = new StreamReader(pipe, leaveOpen: true);
                using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                string? json = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
                var paths = json == null ? null : JsonSerializer.Deserialize<string[]>(json);
                if (paths == null || paths.Length == 0 || Array.Exists(paths, p => string.IsNullOrWhiteSpace(p)))
                    continue;
                receive(paths);
                await writer.WriteLineAsync("OK".AsMemory(), timeout.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or JsonException) { }
        }
    }

    public static async Task<bool> Send(string name, string[] paths)
    {
        using var timeout = new CancellationTokenSource(6000);
        try
        {
            using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, leaveOpen: true);
            await writer.WriteLineAsync(JsonSerializer.Serialize(paths).AsMemory(), timeout.Token).ConfigureAwait(false);
            return await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false) == "OK";
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException) { return false; }
    }

    public void Dispose() { stop.Cancel(); }
}
