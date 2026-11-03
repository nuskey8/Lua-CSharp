const vscode = require("vscode");

exports.activate = (context) => {
  context.subscriptions.push(
    vscode.debug.registerDebugAdapterDescriptorFactory("lua-csharp", {
      createDebugAdapterDescriptor(session) {
        return new vscode.DebugAdapterServer(
          session.configuration.port ?? 4711,
          session.configuration.host ?? "127.0.0.1",
        );
      },
    }),
  );
};
