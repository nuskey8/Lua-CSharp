using Lua;
using Lua.Debugger.Dap;
using Lua.Standard;

var path = Path.GetFullPath(
    args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "example.lua")
);
using var state = LuaState.Create();
state.OpenStandardLibraries();
using var server = new LuaDapServer(state);
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};
var source = await File.ReadAllTextAsync(path, cancellation.Token);
var closure = state.Load(source, "@" + path);
server.Debugger.RegisterSource(closure, source);
var listening = server.RunAsync(cancellation.Token);
Console.Error.WriteLine(
    $"Lua debugger listening on {server.LocalEndpoint}. Attach an editor to begin."
);
try
{
    await server.WaitForConfigurationAsync(cancellation.Token);
    await state.ExecuteAsync(closure, cancellation.Token);
    await server.NotifyTerminatedAsync(cancellation.Token);
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
finally
{
    server.Dispose();
    await listening;
}
