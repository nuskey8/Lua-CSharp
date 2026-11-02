using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lua.Debugger.Dap;

sealed class DapConnection(Stream input, Stream output)
{
    readonly byte[] buffer = new byte[8192];
    readonly SemaphoreSlim writeGate = new(1, 1);
    int position;
    int end;
    int sequence;

    public async Task<JsonObject?> ReadAsync(CancellationToken token)
    {
        var header = new ArrayBufferWriter<byte>();
        var terminator = 0;
        while (terminator != 4)
        {
            if (position == end)
            {
                end = await input.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
                position = 0;
                if (end == 0)
                {
                    if (header.WrittenCount != 0)
                        throw new EndOfStreamException("Incomplete DAP header.");
                    return null;
                }
            }
            var b = buffer[position++];
            header.GetSpan(1)[0] = b;
            header.Advance(1);
            terminator =
                b == (terminator % 2 == 0 ? '\r' : '\n') ? terminator + 1
                : b == '\r' ? 1
                : 0;
            if (header.WrittenCount > 8192)
                throw new InvalidDataException("DAP header is too large.");
        }
        var length = -1;
        foreach (
            var line in Encoding
                .ASCII.GetString(header.WrittenSpan)
                .Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries)
        )
        {
            var colon = line.IndexOf(':');
            if (
                colon < 0
                || !line[..colon].Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
            )
                continue;
            if (
                length >= 0
                || !int.TryParse(
                    line[(colon + 1)..].Trim(),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out length
                )
                || length <= 0
                || length > 16 * 1024 * 1024
            )
                throw new InvalidDataException("Invalid DAP Content-Length.");
        }
        if (length < 0)
            throw new InvalidDataException("Missing DAP Content-Length.");
        var body = new byte[length];
        var copied = Math.Min(length, end - position);
        buffer.AsSpan(position, copied).CopyTo(body);
        position += copied;
        while (copied < length)
        {
            var read = await input.ReadAsync(body.AsMemory(copied), token).ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException("Incomplete DAP message.");
            copied += read;
        }
        try
        {
            return JsonNode.Parse(body) as JsonObject
                ?? throw new InvalidDataException("Expected a DAP object.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Invalid DAP JSON.", exception);
        }
    }

    public async Task WriteAsync(JsonObject message, CancellationToken token)
    {
        await writeGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            message["seq"] = ++sequence;
            var bytes = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(bytes))
                message.WriteTo(writer);
            var header = Encoding.ASCII.GetBytes(
                $"Content-Length: {bytes.WrittenCount.ToString(CultureInfo.InvariantCulture)}\r\n\r\n"
            );
            await output.WriteAsync(header.AsMemory(), token).ConfigureAwait(false);
            await output.WriteAsync(bytes.WrittenMemory, token).ConfigureAwait(false);
            await output.FlushAsync(token).ConfigureAwait(false);
        }
        finally
        {
            writeGate.Release();
        }
    }
}
