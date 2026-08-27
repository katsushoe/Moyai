using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Moyai.Application.Lifecycle;

namespace Moyai.Infrastructure.Persistence;

/// <summary>Lifecycle操作結果をProject監査Eventとして保存します。</summary>
public sealed class SqliteLifecycleEventWriter : ILifecycleEventWriter
{
    private readonly SqliteDatabaseOptions _options;
    private readonly TimeProvider _timeProvider;

    public SqliteLifecycleEventWriter(SqliteDatabaseOptions options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _options = options;
        _timeProvider = timeProvider;
    }

    public async Task WriteAsync(Guid projectId, LifecycleAction action, LifecycleResult result, string actorType, string actorName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "INSERT INTO events(id,project_id,entity_type,entity_id,event_type,actor_type,actor_name,before_json,after_json,message,created_at) VALUES($id,$project,'lifecycle',$project,$event,$actor_type,$actor_name,NULL,$after,$message,$created);";
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$project", projectId.ToString("D", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$event", $"{action.ToString().ToLowerInvariant()}_{(result.Ok ? "completed" : "failed")}");
        command.Parameters.AddWithValue("$actor_type", actorType);
        command.Parameters.AddWithValue("$actor_name", actorName);
        command.Parameters.AddWithValue("$after", JsonSerializer.Serialize(result));
        command.Parameters.AddWithValue("$message", result.ErrorMessage is null ? DBNull.Value : result.ErrorMessage);
        command.Parameters.AddWithValue("$created", _timeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
