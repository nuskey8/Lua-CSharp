using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;

namespace Lua.Debugger.Dap.Tests;

sealed class DapClient(Stream connection, CancellationToken cancellationToken)
{
    readonly Queue<JsonObject> events = new();
    readonly List<string> receivedEvents = new();
    public IReadOnlyList<string> ReceivedEvents => receivedEvents;
    int sequence;

    public async Task<JsonObject> RequestAsync(
        string command,
        JsonObject? args = null,
        bool expectSuccess = true
    )
    {
        var id = await SendAsync(command, args);
        return await ResponseAsync(id, expectSuccess);
    }

    public async Task<int> SendAsync(string command, JsonObject? args = null, bool fragment = false)
    {
        var id = ++sequence;
        var request = new JsonObject
        {
            ["seq"] = id,
            ["type"] = "request",
            ["command"] = command,
            ["arguments"] = args,
        };
        var body = Encoding.UTF8.GetBytes(
            request.ToJsonString(
                new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }
            )
        );
        var packet = Encoding
            .ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n")
            .Concat(body)
            .ToArray();
        var stream = connection;
        if (fragment)
            for (var i = 0; i < packet.Length; i++)
                await stream.WriteAsync(packet.AsMemory(i, 1), cancellationToken);
        else
            await stream.WriteAsync(packet, cancellationToken);
        return id;
    }

    public async Task<JsonObject> ResponseAsync(int id, bool success)
    {
        while (true)
        {
            var response = await ReadAsync();
            if (response["type"]!.GetValue<string>() == "event")
            {
                events.Enqueue(response);
                continue;
            }
            Assert.That(response["request_seq"]!.GetValue<int>(), Is.EqualTo(id));
            Assert.That(
                response["success"]!.GetValue<bool>(),
                Is.EqualTo(success),
                response.ToJsonString()
            );
            return response;
        }
    }

    public async Task<JsonObject> EventAsync(string name)
    {
        while (true)
        {
            var message = events.Count != 0 ? events.Dequeue() : await ReadAsync();
            Assert.That(message["type"]!.GetValue<string>(), Is.EqualTo("event"));
            if (message["event"]!.GetValue<string>() == name)
                return message;
        }
    }

    async Task<JsonObject> ReadAsync()
    {
        var stream = connection;
        var header = new List<byte>();
        var one = new byte[1];
        while (header.Count < 4 || !header.TakeLast(4).SequenceEqual("\r\n\r\n"u8.ToArray()))
        {
            if (await stream.ReadAsync(one, cancellationToken) == 0)
                throw new EndOfStreamException();
            header.Add(one[0]);
        }
        var text = Encoding.ASCII.GetString(header.ToArray());
        var length = int.Parse(text.Split(':')[1].Trim());
        var body = new byte[length];
        await stream.ReadExactlyAsync(body, cancellationToken);
        var message = JsonNode.Parse(body)!.AsObject();
        if (message["type"]!.GetValue<string>() == "event")
            receivedEvents.Add(message["event"]!.GetValue<string>());
        return message;
    }
}
