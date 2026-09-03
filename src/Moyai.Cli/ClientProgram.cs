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
    string? registrationClient = command is "configure" or "unconfigure" or "client-transaction" ? args.ElementAtOrDefault(1) : null;
    if (command is "configure" or "unconfigure" or "client-transaction" && registrationClient is null)
        throw new ArgumentException("Specify codex or claude.");
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
    Dictionary<string, string?> values = CliArguments.Parse(args.Skip(serviceAction is null && registrationClient is null ? 1 : 2).ToArray());
    string configPath = values.Remove("config", out string? config) && !string.IsNullOrWhiteSpace(config) ? Path.GetFullPath(config) : MoyaiSettings.DefaultPath;
    if (command is "help" or "--help" or "-h")
    {
        Console.WriteLine("moyaictl <command> [--config <moyai.json>] [--kebab-case-option <value>]\n" +
            "moyaictl service <start|stop|pause|resume|register|unregister|status> [--config <moyai.json>]\n" +
            "config-init: create configuration only when absent.\n" +
            "configure|unconfigure <codex|claude> [--profile <user-directory>] [--config <moyai.json>]: manage the user's MCP entry.\n" +
            "--transaction [--transaction-id <id>]: retain rollback state for MSI. client-transaction <codex|claude> --phase <rollback|commit> [--profile <user-directory>] [--transaction-id <id>].\n" +
            "project-create --name <name>: create using only a name. project-ensure --name <name>: create only when missing.\n" +
            "project-configure --name <name> --expected-revision <revision> [settings]: associate execution settings later.\n" +
            "project-rename --current-name <old> --name <new> --expected-revision <revision>: rename while preserving settings.\n" +
            "commands: list service business commands and schemas. version: query service version.\n" +
            "Business commands always connect to the service. Management commands target the Moyai Windows service.\n" +
            "Business failures (ok=false) return exit code 1 and structured errors on standard error.");
        return 0;
    }
    if (registrationClient is not null)
    {
        string profile = values.Remove("profile", out string? selectedProfile)
            ? selectedProfile ?? throw new ArgumentException("--profile requires a value.")
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        bool transaction = values.Remove("transaction", out string? transactionValue);
        if (transactionValue is not null) throw new ArgumentException("--transaction takes no value.");
        values.Remove("phase", out string? phase);
        values.Remove("transaction-id", out string? transactionId);
        if (values.Count != 0 || (command != "client-transaction" && phase is not null) || (command == "client-transaction" && transaction))
            throw new ArgumentException("Unknown client configuration option.");
        var registration = new ClientRegistration(registrationClient, profile);
        if (command == "client-transaction")
        {
            registration.Finish(phase ?? throw new ArgumentException("--phase is required."), transactionId);
            Console.WriteLine(JsonSerializer.Serialize(new { ok = true, client = registrationClient, status = phase }));
            return 0;
        }
        string? endpoint = command == "configure" ? MoyaiSettings.Load(configPath).ServerUrl.TrimEnd('/') + "/mcp" : null;
        string status = registration.Apply(command == "configure", endpoint, transaction, transactionId ?? "manual");
        Console.WriteLine(JsonSerializer.Serialize(new { ok = true, client = registrationClient, status, restartClient = true }));
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
    return CliResponse.Write(command, result, Console.Out, Console.Error);
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
