using System.Text.Json;
using Moyai.Application.Diagnostics;
using Moyai.Cli;
using Moyai.Configuration;

string executedCommand = args.FirstOrDefault() ?? "help";
GlobalExceptionHandler.Register(exception => ReportError(exception, true, executedCommand));
try
{
    if (args.FirstOrDefault() == "--config")
    {
        if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]) || args[1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException("--config requires a path.");
        args = [.. args.Skip(2).DefaultIfEmpty("help"), "--config", args[1]];
    }
    string command = args.FirstOrDefault() ?? "help";
    executedCommand = command;
    string? registrationClient = command is "configure" or "unconfigure" or "client-transaction" ? args.ElementAtOrDefault(1) : null;
    if (command is "configure" or "unconfigure" or "client-transaction" && registrationClient is null)
        throw new ArgumentException("Specify codex or claude.");
    string? hostAction = null;
    if (command == "service")
    {
        throw new ArgumentException("Moyai no longer runs as a Windows service. Use 'host <start|stop|restart|status>'.");
    }
    if (command == "host")
    {
        hostAction = args.ElementAtOrDefault(1);
        if (hostAction is not ("start" or "stop" or "restart" or "status"))
            throw new ArgumentException("Usage: moyaictl host <start|stop|restart|status> [--config <moyai.json>]");
        executedCommand = $"host {hostAction}";
    }
    string? autostartAction = null;
    if (command == "autostart")
    {
        autostartAction = args.ElementAtOrDefault(1);
        if (autostartAction is not ("enable" or "disable" or "status"))
            throw new ArgumentException("Usage: moyaictl autostart <enable|disable|status>");
        executedCommand = $"autostart {autostartAction}";
    }
    Dictionary<string, string?> values = CliArguments.Parse(args.Skip(hostAction is null && autostartAction is null && registrationClient is null ? 1 : 2).ToArray());
    string? selectedConfig = values.Remove("config", out string? config)
        ? !string.IsNullOrWhiteSpace(config) ? config : throw new ArgumentException("--config requires a path.")
        : null;
    if (command is "help" or "--help" or "-h")
    {
        Console.WriteLine("moyaimcp [--config <moyai.json>] <command> [--kebab-case-option <value>]\n" +
            "moyaictl <command> [--config <moyai.json>] [--kebab-case-option <value>]\n" +
            "moyaimcp is an MCP command client sharing the moyaictl implementation. Moyai.Mcp.exe hosts the current user's MCP server.\n" +
            $"Only operational configuration: {MoyaiSettings.DefaultPath}. --config accepts only this path, before or after the command. No profile fallback.\n" +
            "--help: local help. --version: local CLI version. version: query the running service.\n" +
            "Provider configuration: connectionMode=static (default), buckettie_user_host_v1, or githubie_user_host_v1. Githubie owner mode requires an absolute runtimeExecutable; runtimeSha256 is deprecated and ignored. See docs/provider-user-host.md (.ja.md).\n" +
            "moyaictl host <start|stop|restart|status> [--config <moyai.json>]: manage the current user's host process.\n" +
            "moyaictl autostart <enable|disable|status>: manage logon startup separately from the running host.\n" +
            "profile-init: initialize the fixed configuration and per-user runtime state; enable autostart for a new configuration.\n" +
            "configure|unconfigure <codex|claude> [--profile <user-directory>] [--config <moyai.json>]: manage the user's MCP entry.\n" +
            "--transaction [--transaction-id <id>]: retain rollback state for MSI. client-transaction <codex|claude> --phase <rollback|commit> [--profile <user-directory>] [--transaction-id <id>].\n" +
            "project-create --name <name>: create using only a name. project-ensure --name <name>: create only when missing.\n" +
            "project-configure --name <name> --expected-revision <revision> [settings]: associate execution settings later.\n" +
            "project-rename --current-name <old> --name <new> --expected-revision <revision>: rename while preserving settings.\n" +
            "project-provider-repository-set --project <name> --expected-revision <revision> (--provider-repository-id <id>|--clear-mapping): set/clear the Provider registration mapping.\n" +
            "commands: list service business commands and schemas. version: query service version.\n" +
            "runtime-diagnostics [--project <registered-name>]: inspect the running host's loaded config/DB, config staleness and nonsecret Provider/authentication state without contacting Providers.\n" +
            "branch-protection-get|branch-protection-update|branch-protection-delete --project <registered-name> --branch <exact-name>: hosting protection; update takes --edit <JSON> and optional --create-if-missing true. Changes require Provider approval and audit; unknown outcomes are never replayed.\n" +
            "pull-request-create --project <name> --source-branch <source> --destination-branch <destination> --title <title> --description <text> --draft <true|false>: create on a Provider-allowed route with dedicated PR authority.\n" +
            "pull-request-get --project <name> --pull-request-id <id>: read authenticated PR state and source SHA.\n" +
            "pull-request-publish-create --project <name> --source-branch <branch> --destination-branch <branch> --expected-source-sha <SHA> --title <text> --operation-id <D-UUID> --draft <true|false> [--description <text>]: explicit approval required; unknown results require status, never automatic retry.\n" +
            "pull-request-publish-status --project <name> --operation-id <D-UUID>: read-only reconciliation.\n" +
            "pull-request-merge --project <name> --pull-request-id <id> --strategy <repository_default|merge|squash|rebase> --expected-source-commit <full-SHA> [--message <text>]: requires explicit PR approval and dedicated merge authority; never retries unknown results.\n" +
            "repository-settings-get --project <name>: read registered settings.\n" +
            "branch-switch --project <name> --branch <existing-local-branch>: switch through the Provider; requires clean tree and repository.branch.write.\n" +
            "branch-create-local --project <name> --branch <new-local-branch> --source <local-branch|tag|full-SHA>: create without checkout or remote changes; requires dedicated capability and repository.branch.write.\n" +
            "repository-settings-update --project <name> --changes <JSON-object>: update explicit fields with Provider approval; read before retry after unknown outcome.\n" +
            "Githubie settings: pr_publication_source_branches is an exact branch array; pull_request_routes is an array of source_branch/destination_branch objects. Omit to preserve, [] to clear; no automatic local_commit_branches migration.\n" +
            "Business commands always connect to the current user's host. Management commands use a current-user-only local control pipe.\n" +
            "Provider approval uses the existing human approval flow; business failures (ok=false) return exit code 1 and structured errors on standard error.");
        return 0;
    }
    if (command == "--version")
    {
        if (values.Count != 0) throw new ArgumentException("Unknown --version option.");
        Console.WriteLine(typeof(CliResponse).Assembly.GetName().Version?.ToString());
        return 0;
    }
    string configPath = MoyaiSettings.ResolveOperationalPath(selectedConfig);
    if (registrationClient is not null)
    {
        string profile = values.Remove("profile", out string? selectedProfile)
            ? selectedProfile ?? throw new ArgumentException("--profile requires a value.")
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        bool transaction = values.Remove("transaction", out string? transactionValue);
        if (!StringComparer.OrdinalIgnoreCase.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(profile)),
            Path.TrimEndingDirectorySeparator(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))))
            throw new ArgumentException("Client configuration must target the current user's profile.");
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
        MoyaiSettings? clientSettings = command == "configure" ? MoyaiSettings.Load(configPath) : null;
        string? endpoint = clientSettings?.ServerUrl.TrimEnd('/') + (clientSettings is null ? null : "/mcp");
        string status = registration.Apply(command == "configure", endpoint, transaction, transactionId ?? "manual", clientSettings?.ApiToken);
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
    if (command == "profile-init")
    {
        string transactionId = values.Remove("transaction-id", out string? id) && !string.IsNullOrWhiteSpace(id) ? id : throw new ArgumentException("--transaction-id is required.");
        string? trayPath = values.Remove("tray-path", out string? selectedTrayPath) ? selectedTrayPath ?? throw new ArgumentException("--tray-path requires a path.") : null;
        if (values.Count != 0) throw new ArgumentException("Unknown profile-init option.");
        ServiceCommands.InitializeUserProfile(configPath, transactionId, trayPath);
        Console.WriteLine(JsonSerializer.Serialize(new { ok = true, configPath, autostart = ServiceCommands.SetAutostart("status") }));
        return 0;
    }
    if (command == "profile-uninstall")
    {
        string transactionId = values.Remove("transaction-id", out string? id) && !string.IsNullOrWhiteSpace(id) ? id : throw new ArgumentException("--transaction-id is required.");
        string? trayPath = values.Remove("tray-path", out string? selectedTrayPath) ? selectedTrayPath ?? throw new ArgumentException("--tray-path requires a path.") : null;
        if (values.Count != 0) throw new ArgumentException("Unknown profile-uninstall option.");
        ServiceCommands.RemoveProductAutostart(transactionId, trayPath);
        Console.WriteLine(JsonSerializer.Serialize(new { ok = true, autostart = "removed_if_product_owned" }));
        return 0;
    }
    if (command == "profile-transaction")
    {
        string phase = values.Remove("phase", out string? selectedPhase) ? selectedPhase ?? throw new ArgumentException("--phase requires a value.") : throw new ArgumentException("--phase is required.");
        string transactionId = values.Remove("transaction-id", out string? id) && !string.IsNullOrWhiteSpace(id) ? id : throw new ArgumentException("--transaction-id is required.");
        if (values.Count != 0) throw new ArgumentException("Unknown profile-transaction option.");
        ServiceCommands.FinishProfileTransaction(phase, transactionId);
        Console.WriteLine(JsonSerializer.Serialize(new { ok = true, phase }));
        return 0;
    }
    if (hostAction is not null)
    {
        if (values.Count != 0) throw new ArgumentException("Unknown service option.");
        Console.WriteLine(JsonSerializer.Serialize(await ServiceCommands.ExecuteHostAsync(hostAction, configPath)));
        return 0;
    }
    if (autostartAction is not null)
    {
        if (values.Count != 0) throw new ArgumentException("Unknown autostart option.");
        Console.WriteLine(JsonSerializer.Serialize(ServiceCommands.SetAutostart(autostartAction)));
        return 0;
    }
    MoyaiSettings settings = MoyaiSettings.Load(configPath);
    return await McpCommandClient.ExecuteAsync(settings, command, values, Console.Out, Console.Error);
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
