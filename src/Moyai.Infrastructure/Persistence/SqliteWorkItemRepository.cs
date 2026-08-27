using System.Globalization;
using Microsoft.Data.Sqlite;
using Moyai.Application.Projects;
using Moyai.Application.WorkItems;
using Moyai.Domain.Events;
using Moyai.Domain.WorkItems;

namespace Moyai.Infrastructure.Persistence;

/// <summary>WorkItem、Key採番、EventをSQLiteで原子的に処理します。</summary>
public sealed class SqliteWorkItemRepository : IWorkItemRepository
{
    private readonly SqliteDatabaseOptions _options;

    public SqliteWorkItemRepository(SqliteDatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public async Task<WorkItem> AddAsync(Guid projectId, WorkItemType type, Func<long, WorkItem> itemFactory, Func<WorkItem, ProjectEvent> eventFactory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(itemFactory);
        ArgumentNullException.ThrowIfNull(eventFactory);
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            long sequence = await NextSequenceAsync(connection, transaction, projectId, type, cancellationToken).ConfigureAwait(false);
            WorkItem item = itemFactory(sequence);
            await InsertItemAsync(connection, transaction, item, cancellationToken).ConfigureAwait(false);
            await InsertEventAsync(connection, transaction, eventFactory(item), cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return item;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<WorkItem?> GetAsync(Guid projectId, string key, bool includeDeleted, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = includeDeleted
            ? $"{SelectSql} WHERE project_id=$project AND key=$key COLLATE NOCASE;"
            : $"{SelectSql} WHERE project_id=$project AND key=$key COLLATE NOCASE AND deleted_at IS NULL;";
        command.Parameters.AddWithValue("$project", Format(projectId));
        command.Parameters.AddWithValue("$key", key);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Read(reader) : null;
    }

    public async Task<IReadOnlyList<WorkItem>> ListAsync(Guid projectId, bool includeDeleted, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = includeDeleted
            ? $"{SelectSql} WHERE project_id=$project ORDER BY type,sequence_no;"
            : $"{SelectSql} WHERE project_id=$project AND deleted_at IS NULL ORDER BY type,sequence_no;";
        command.Parameters.AddWithValue("$project", Format(projectId));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var items = new List<WorkItem>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) items.Add(Read(reader));
        return items;
    }

    public async Task UpdateAsync(WorkItem item, long expectedRevision, ProjectEvent projectEvent, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE work_items SET title=$title,description=$description,status=$status,priority=$priority,
                    severity=$severity,owner=$owner,metadata_json=$metadata,updated_at=$updated,
                    closed_at=$closed,deleted_at=$deleted,revision=$revision
                WHERE id=$id AND revision=$expected;
                """;
            AddMutableParameters(command, item);
            command.Parameters.AddWithValue("$id", Format(item.Id));
            command.Parameters.AddWithValue("$expected", expectedRevision);
            int affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (affected == 0) throw new RevisionConflictException(expectedRevision);
            await InsertEventAsync(connection, transaction, projectEvent, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static async Task<long> NextSequenceAsync(SqliteConnection connection, SqliteTransaction transaction, Guid projectId, WorkItemType type, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO work_item_sequences(project_id,type,next_sequence_no) VALUES($project,$type,2)
            ON CONFLICT(project_id,type) DO UPDATE SET next_sequence_no=next_sequence_no+1
            RETURNING next_sequence_no-1;
            """;
        command.Parameters.AddWithValue("$project", Format(projectId));
        command.Parameters.AddWithValue("$type", TypeText(type));
        return (long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("Sequence was not returned."));
    }

    private static async Task InsertItemAsync(SqliteConnection connection, SqliteTransaction transaction, WorkItem item, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO work_items(id,project_id,key,sequence_no,type,title,description,status,priority,severity,owner,metadata_json,created_by_type,created_by_name,created_at,updated_at,closed_at,deleted_at,revision)
            VALUES($id,$project,$key,$sequence,$type,$title,$description,$status,$priority,$severity,$owner,$metadata,$created_type,$created_name,$created,$updated,$closed,$deleted,$revision);
            """;
        AddMutableParameters(command, item);
        command.Parameters.AddWithValue("$id", Format(item.Id));
        command.Parameters.AddWithValue("$project", Format(item.ProjectId));
        command.Parameters.AddWithValue("$key", item.Key);
        command.Parameters.AddWithValue("$sequence", item.SequenceNumber);
        command.Parameters.AddWithValue("$type", TypeText(item.Type));
        command.Parameters.AddWithValue("$created_type", item.CreatedByType);
        command.Parameters.AddWithValue("$created_name", item.CreatedByName);
        command.Parameters.AddWithValue("$created", Format(item.CreatedAt));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddMutableParameters(SqliteCommand command, WorkItem item)
    {
        command.Parameters.AddWithValue("$title", item.Title);
        command.Parameters.AddWithValue("$description", Value(item.Description));
        command.Parameters.AddWithValue("$status", item.Status);
        command.Parameters.AddWithValue("$priority", item.Priority.ToString().ToLowerInvariant());
        command.Parameters.AddWithValue("$severity", item.Severity is null ? DBNull.Value : item.Severity.Value.ToString().ToLowerInvariant());
        command.Parameters.AddWithValue("$owner", Value(item.Owner));
        command.Parameters.AddWithValue("$metadata", Value(item.MetadataJson));
        command.Parameters.AddWithValue("$updated", Format(item.UpdatedAt));
        command.Parameters.AddWithValue("$closed", item.ClosedAt is null ? DBNull.Value : Format(item.ClosedAt.Value));
        command.Parameters.AddWithValue("$deleted", item.DeletedAt is null ? DBNull.Value : Format(item.DeletedAt.Value));
        command.Parameters.AddWithValue("$revision", item.Revision);
    }

    private static async Task InsertEventAsync(SqliteConnection connection, SqliteTransaction transaction, ProjectEvent projectEvent, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO events(id,project_id,entity_type,entity_id,event_type,actor_type,actor_name,before_json,after_json,message,created_at) VALUES($id,$project,$entity_type,$entity,$event_type,$actor_type,$actor_name,$before,$after,$message,$created);";
        command.Parameters.AddWithValue("$id", Format(projectEvent.Id));
        command.Parameters.AddWithValue("$project", Format(projectEvent.ProjectId));
        command.Parameters.AddWithValue("$entity_type", projectEvent.EntityType);
        command.Parameters.AddWithValue("$entity", Format(projectEvent.EntityId));
        command.Parameters.AddWithValue("$event_type", projectEvent.EventType);
        command.Parameters.AddWithValue("$actor_type", projectEvent.ActorType);
        command.Parameters.AddWithValue("$actor_name", projectEvent.ActorName);
        command.Parameters.AddWithValue("$before", Value(projectEvent.BeforeJson));
        command.Parameters.AddWithValue("$after", Value(projectEvent.AfterJson));
        command.Parameters.AddWithValue("$message", Value(projectEvent.Message));
        command.Parameters.AddWithValue("$created", Format(projectEvent.CreatedAt));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static WorkItem Read(SqliteDataReader reader) => WorkItem.RestoreState(
        Guid.Parse(reader.GetString(0), CultureInfo.InvariantCulture), Guid.Parse(reader.GetString(1), CultureInfo.InvariantCulture), reader.GetString(2), reader.GetInt64(3), ParseType(reader.GetString(4)), reader.GetString(5), Optional(reader, 6), reader.GetString(7), Enum.Parse<WorkItemPriority>(reader.GetString(8), true), reader.IsDBNull(9) ? null : Enum.Parse<WorkItemSeverity>(reader.GetString(9), true), Optional(reader, 10), Optional(reader, 11), reader.GetString(12), reader.GetString(13), ParseDate(reader.GetString(14)), ParseDate(reader.GetString(15)), reader.IsDBNull(16) ? null : ParseDate(reader.GetString(16)), reader.IsDBNull(17) ? null : ParseDate(reader.GetString(17)), reader.GetInt64(18));

    private static string TypeText(WorkItemType type) => type == WorkItemType.ChangeRequest ? "change_request" : type.ToString().ToLowerInvariant();
    private static WorkItemType ParseType(string value) => value == "change_request" ? WorkItemType.ChangeRequest : Enum.Parse<WorkItemType>(value, true);
    private static object Value(string? value) => value is null ? DBNull.Value : value;
    private static string? Optional(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static string Format(Guid value) => value.ToString("D", CultureInfo.InvariantCulture);
    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset ParseDate(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    private const string SelectSql = "SELECT id,project_id,key,sequence_no,type,title,description,status,priority,severity,owner,metadata_json,created_by_type,created_by_name,created_at,updated_at,closed_at,deleted_at,revision FROM work_items";
}
