using LightRcon.Core;

namespace LightRcon.Tests;

internal static class LayerBufferChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        using var output = new MemoryStream();
        using var gate = new SemaphoreSlim(1);
        var expected = Enumerable.Range(0, 12).Select(index => new LayerFrame
        {
            Type = "event", Id = index, Words = ["player.onChat", "buffer-" + index, new string('界', 32000)]
        }).ToArray();
        await Task.WhenAll(expected.Select(frame => LayerWire.WriteAsync(output, frame, gate, default)));
        output.Position = 0;
        var received = new List<LayerFrame>();
        while (output.Position < output.Length) received.Add(await LayerWire.ReadAsync(output, default));
        check(received.Count == expected.Length && received.All(frame => frame.Words.SequenceEqual(expected[(int)frame.Id].Words)),
            "pooled Layer frames preserve large Unicode payloads across concurrent writes and buffer reuse");
        await gate.WaitAsync();
        using var cancel = new CancellationTokenSource();
        var waiting = LayerWire.WriteAsync(output, new() { Type = "login", Password = "cancellation-probe" }, gate, cancel.Token);
        cancel.Cancel();
        try { await waiting; throw new Exception("Canceled Layer write succeeded"); }
        catch (OperationCanceledException) { check(output.Position == output.Length, "canceling a queued Layer write leaves the stream and gate unchanged"); }
        gate.Release();
        var start = output.Position;
        await LayerWire.WriteAsync(output, new() { Type = "command", Words = ["version"] }, gate, default);
        output.Position = start;
        check((await LayerWire.ReadAsync(output, default)).Words.SequenceEqual(new[] { "version" }), "a canceled pooled write does not corrupt the next frame");
    }
}
