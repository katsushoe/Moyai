using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Moyai.Application.Diagnostics;
using Moyai.Cli;
using Moyai.Configuration;

string executedCommand = args.FirstOrDefault() ?? "help";
GlobalExceptionHandler.Register(exception => ReportError(exception, true, executedCommand));
try
{
    string command = args.FirstOrDefault() ?? "help";
    string? serviceAction = null;
    if (command == "service")
    {
        serviceAction = args.ElementAtOrDefault(1);
        if (serviceAction is not ("start" or "stop" or "pause" or "resume" or "register" or "unregister" or "status"))
            throw new ArgumentException("Usage: moyaictl service <start|stop|pause|resume|register|unregister|status> [--config <moyai.json>]");
        executedCommand = $"service {serviceAction}";
    }
    if (command.StartsWith("service-", StringComparison.Ordinal))
        throw new ArgumentException("Use 'moyaictl service <action>' instead of 'service-<action>'.");
    Dictionary<string, string?> values = CliArguments.Parse(args.Skip(serviceAction is null ? 1 : 2).ToArray());
    string configPath = values.Remove("config", out string? config) && !string.IsNullOrWhiteSpace(config) ? Path.GetFullPath(config) : MoyaiSettings.DefaultPath;
    if (command is "help" or "--help" or "-h")
    {
        Console.WriteLine("moyaictl <command> [--config <moyai.json>] [--kebab-case-option <value>]\n" +
            "moyaictl service <start|stop|pause|resume|register|unregister|status> [--config <moyai.json>]\n" +
            "config-init: create configuration only when absent.\n" +
            "project-create --name <name>: create using only a name. project-ensure --name <name>: create only when missing.\n" +
            "project-configure --name <name> --expected-revision <revision> [settings]: associate execution settings later.\n" +
            "project-rename --current-name <old> --name <new> --expected-revision <revision>: rename while preserving settings.\n" +
            "commands: list service business commands and schemas. version: query service version.\n" +
            "Business commands always connect to the service. Management commands target the Moyai Windows service.");
        return 0;
    }
    if (command == "config-init")
    {
        if (values.Count != 0) throw new ArgumentException("Unknown config-init option.");
        ServiceCommands.InitializeConfig(configPath);
        Console.WriteLine(JsonSerializer.Serialize(new { ok = true, configPath }));
        return 0;
    }
    if (serviceAction is not null)
    {
        if (values.Count != 0) throw new ArgumentException("Unknown service option.");
        Console.WriteLine(JsonSerializer.Serialize(await ServiceCommands.ExecuteAsync(serviceAction, configPath)));
        return 0;
    }
    MoyaiSettings settings = MoyaiSettings.Load(configPath);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds));
    using var http = new HttpClient();
    await using var transport = new HttpClientTransport(new HttpClientTransportOptions
    {
        Endpoint = new Uri(settings.ServerUrl.TrimEnd('/') + "/mcp"),
        TransportMode = HttpTransportMode.StreamableHttp,
    }, http);
    await using McpClient client = await McpClient.CreateAsync(transport, cancellationToken: timeout.Token);
    var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
    if (command == "commands")
    {
        if (values.Count != 0) throw new ArgumentException("Unknown commands option.");
        Console.WriteLine(JsonSerializer.Serialize(tools.Select(tool => new { command = tool.Name.Replace('_', '-'), tool.Description, schema = tool.JsonSchema })));
        return 0;
    }
    string toolName = command == "version" ? "get_version" : command.Replace('-', '_');
    var selected = tools.SingleOrDefault(tool => tool.Name == toolName) ?? throw new ArgumentException($"Unknown service command '{command}'. Use commands to list supported commands.");
    IReadOnlyDictionary<string, object?> arguments = CliArguments.Convert(values, selected.JsonSchema, toolName);
    CallToolResult result = await client.CallToolAsync(toolName, arguments, cancellationToken: timeout.Token);
    string output = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "null";
    if (result.IsError is true)
    {
        Console.Error.WriteLine(JsonSerializer.Serialize(new { command, summary = output, ok = false, fatal = false, error = new { code = "service_error", message = output } }));
        return 1;
    }
    using (JsonDocument.Parse(output)) Console.WriteLine(output);
    return 0;
}
catch (Exception exception)
{
    ReportError(exception, false, executedCommand);
    return 1;
}

static void ReportError(Exception exception, bool fatal, string command) => Console.Error.WriteLine(JsonSerializer.Serialize(new
{
    command,
    ok = false,
    fatal,
    summary = exception.Message,
    error = new { code = exception.GetType().Name, message = exception.Message },
}));
