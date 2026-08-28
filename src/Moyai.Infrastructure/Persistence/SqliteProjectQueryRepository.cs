using System.Globalization;
using Microsoft.Data.Sqlite;
using Moyai.Application.Projects;
using Moyai.Domain.Events;
using Moyai.Domain.Projects;
using Moyai.Domain.WorkItems;

namespace Moyai.Infrastructure.Persistence;

/// <summary>FTS5検索とProject集約QueryをSQLiteで実行します。</summary>
public sealed class SqliteProjectQueryRepository : IProjectQueryRepository
{
    private readonly SqliteDatabaseOptions _options;

    public SqliteProjectQueryRepository(SqliteDatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public async Task<PagedResult<WorkItem>> SearchAsync(Guid projectId, WorkItemSearchRequest request, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await SqliteConnectionFactory.OpenAsync(_options, cancellationToken).ConfigureAwait(false);
        var filters = new List<string> { "work_item_search.project_id=$project", "work_item_search MATCH $query", "item.deleted_at IS NULL" };
        if (request.Type is not null) filters.Add("item.type=$type");
        if (!string.IsNullOrWhiteSpace(request.Status)) filters.Add("item.status=$status");
        if (request.Priority is not null) filters.Add("item.priority=$priority");
        if (!string.IsNullOrWhiteSpace(request.Owner)) filters.Add("item.owner=$owner");
        if (request.CreatedAfter is not null) filters.Add("item.created_at>=$created_after");
        if (request.UpdatedAfter is not null) filters.Add("item.updated_at>=$updated_after");
        string from = $"FROM work_item_search JOIN work_items item ON item.id=work_item_search.work_item_id WHERE {string.Join(" AND ", filters)}";

        await using SqliteCommand countCommand = connection.CreateCommand();
        countCommand.CommandText = $"SELECT COUNT(*) {from};";
        AddSearchParameters(countCommand, projectId, request);
        long total = Convert.ToInt64(await countCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT {WorkItemColumns} {from} ORDER BY item.updated_at DESC,item.key COLLATE NOCASE,item.id LIMIT $limit OFFSET $offset;";
        AddSearchParameters(command, projectId, request);
        command.Parameters.AddWithValue("$limit", request.Limit);
        command.Parameters.AddWithValue("$offset", request.Offset);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var items = new List<WorkItem>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) items.Add(ReadWorkItem(reader));
        return new PagedResult<WorkItem>(items, request.Offset, request.Limit, total);
    }

    public async Task<ProjectOverview> GetOverviewAsync(Project project, int recentLimit, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await SqliteConnectionFactory.OpenAsync(_options, cancellationToken).ConfigureAwait(false);
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "SELECT type,COUNT(*) FROM work_items WHERE project_id=$project AND deleted_at IS NULL AND closed_at IS NULL GROUP BY type ORDER BY type;";
            command.Parameters.AddWithValue("$project", Format(project.Id));
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) counts[reader.GetString(0)] = reader.GetInt64(1);
        }

        IReadOnlyList<WorkItem> blockers = await ReadWorkItemsAsync(connection, $"SELECT DISTINCT {WorkItemColumns} FROM work_items item JOIN work_item_relations relation ON relation.source_work_item_id=item.id WHERE item.project_id=$project AND relation.relation='blocks' AND item.deleted_at IS NULL AND item.closed_at IS NULL ORDER BY item.priority DESC,item.key COLLATE NOCASE,item.id;", project.Id, cancellationToken).ConfigureAwait(false);
        string? latest = await ScalarTextAsync(connection, "SELECT version FROM releases WHERE project_id=$project AND status='released' AND channel='stable' AND deleted_at IS NULL ORDER BY released_at DESC,id DESC LIMIT 1;", project.Id, cancellationToken).ConfigureAwait(false);
        string? planned = await ScalarTextAsync(connection, "SELECT version FROM releases WHERE project_id=$project AND status IN ('planned','preparing','ready') AND deleted_at IS NULL ORDER BY COALESCE(planned_at,created_at),id LIMIT 1;", project.Id, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<ProjectEvent> recent = await ReadEventsAsync(connection, "SELECT id,project_id,entity_type,entity_id,event_type,actor_type,actor_name,before_json,after_json,message,created_at FROM events WHERE project_id=$project ORDER BY created_at DESC,id DESC LIMIT $limit;", project.Id, null, 0, recentLimit, cancellationToken).ConfigureAwait(false);
        return new ProjectOverview(project, counts, blockers, latest, planned, recent);
    }

    public async Task<ProjectChanges> GetChangesSinceAsync(Guid projectId, DateTimeOffset since, int offset, int limit, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await SqliteConnectionFactory.OpenAsync(_options, cancellationToken).ConfigureAwait(false);
        await using SqliteCommand count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM events WHERE project_id=$project AND created_at>$since;";
        count.Parameters.AddWithValue("$project", Format(projectId));
        count.Parameters.AddWithValue("$since", Format(since));
        long total = Convert.ToInt64(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
        IReadOnlyList<ProjectEvent> events = await ReadEventsAsync(connection, "SELECT id,project_id,entity_type,entity_id,event_type,actor_type,actor_name,before_json,after_json,message,created_at FROM events WHERE project_id=$project AND created_at>$since ORDER BY created_at,id LIMIT $limit OFFSET $offset;", projectId, since, offset, limit, cancellationToken).ConfigureAwait(false);
        return new ProjectChanges(events, since, offset, limit, total);
    }

    private static void AddSearchParameters(SqliteCommand command, Guid projectId, WorkItemSearchRequest request)
    {
        command.Parameters.AddWithValue("$project", Format(projectId));
        command.Parameters.AddWithValue("$query", request.Query);
        if (request.Type is not null) command.Parameters.AddWithValue("$type", TypeText(request.Type.Value));
        if (!string.IsNullOrWhiteSpace(request.Status)) command.Parameters.AddWithValue("$status", request.Status);
        if (request.Priority is not null) command.Parameters.AddWithValue("$priority", request.Priority.Value.ToString().ToLowerInvariant());
        if (!string.IsNullOrWhiteSpace(request.Owner)) command.Parameters.AddWithValue("$owner", request.Owner);
        if (request.CreatedAfter is not null) command.Parameters.AddWithValue("$created_after", Format(request.CreatedAfter.Value));
        if (request.UpdatedAfter is not null) command.Parameters.AddWithValue("$updated_after", Format(request.UpdatedAfter.Value));
    }

    private static async Task<IReadOnlyList<WorkItem>> ReadWorkItemsAsync(SqliteConnection connection, string sql, Guid projectId, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$project", Format(projectId));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var items = new List<WorkItem>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) items.Add(ReadWorkItem(reader));
        return items;
    }

    private static async Task<string?> ScalarTextAsync(SqliteConnection connection, string sql, Guid projectId, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$project", Format(projectId));
        return Convert.ToString(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    private static async Task<IReadOnlyList<ProjectEvent>> ReadEventsAsync(SqliteConnection connection, string sql, Guid projectId, DateTimeOffset? since, int offset, int limit, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$project", Format(projectId));
        if (since is not null) command.Parameters.AddWithValue("$since", Format(since.Value));
        command.Parameters.AddWithValue("$limit", limit);
        if (sql.Contains("$offset", StringComparison.Ordinal)) command.Parameters.AddWithValue("$offset", offset);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var events = new List<ProjectEvent>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) events.Add(new ProjectEvent(ParseGuid(reader, 0), ParseGuid(reader, 1), reader.GetString(2), ParseGuid(reader, 3), reader.GetString(4), reader.GetString(5), reader.GetString(6), Optional(reader, 7), Optional(reader, 8), Optional(reader, 9), ParseDate(reader, 10)));
        return events;
    }

    private static WorkItem ReadWorkItem(SqliteDataReader reader) => WorkItem.RestoreState(ParseGuid(reader, 0), ParseGuid(reader, 1), reader.GetString(2), reader.GetInt64(3), ParseType(reader.GetString(4)), reader.GetString(5), Optional(reader, 6), reader.GetString(7), Enum.Parse<WorkItemPriority>(reader.GetString(8), true), reader.IsDBNull(9) ? null : Enum.Parse<WorkItemSeverity>(reader.GetString(9), true), Optional(reader, 10), Optional(reader, 11), reader.GetString(12), reader.GetString(13), ParseDate(reader, 14), ParseDate(reader, 15), reader.IsDBNull(16) ? null : ParseDate(reader, 16), reader.IsDBNull(17) ? null : ParseDate(reader, 17), reader.GetInt64(18));
    private static string TypeText(WorkItemType type) => type == WorkItemType.ChangeRequest ? "change_request" : type.ToString().ToLowerInvariant();
    private static WorkItemType ParseType(string value) => value == "change_request" ? WorkItemType.ChangeRequest : Enum.Parse<WorkItemType>(value, true);
    private static string? Optional(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static Guid ParseGuid(SqliteDataReader reader, int ordinal) => Guid.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture);
    private static DateTimeOffset ParseDate(SqliteDataReader reader, int ordinal) => DateTimeOffset.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    private static string Format(Guid value) => value.ToString("D", CultureInfo.InvariantCulture);
    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private const string WorkItemColumns = "item.id,item.project_id,item.key,item.sequence_no,item.type,item.title,item.description,item.status,item.priority,item.severity,item.owner,item.metadata_json,item.created_by_type,item.created_by_name,item.created_at,item.updated_at,item.closed_at,item.deleted_at,item.revision";
}
