using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Moyai.Application.WorkItems;
using Moyai.Domain.Events;
using Moyai.Domain.WorkItems;

namespace Moyai.Infrastructure.Persistence;

/// <summary>WorkItemのRelation、Comment、外部Link、履歴をSQLiteへ保存します。</summary>
public sealed class SqliteWorkItemCollaborationRepository : IWorkItemCollaborationRepository
{
    private readonly SqliteDatabaseOptions _options;

    public SqliteWorkItemCollaborationRepository(SqliteDatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public Task AddRelationAsync(WorkItemRelation relation, ProjectEvent projectEvent, CancellationToken cancellationToken = default) =>
        InsertWithEventAsync("INSERT INTO work_item_relations(id,project_id,source_work_item_id,target_work_item_id,relation,created_at) VALUES($id,$project,$source,$target,$relation,$created);", command =>
        {
            AddIdentity(command, relation.Id, relation.ProjectId, relation.CreatedAt);
            command.Parameters.AddWithValue("$source", Format(relation.SourceWorkItemId));
            command.Parameters.AddWithValue("$target", Format(relation.TargetWorkItemId));
            command.Parameters.AddWithValue("$relation", relation.Relation);
        }, projectEvent, cancellationToken);

    public async Task<WorkItemRelation?> RemoveRelationAsync(Guid projectId, Guid relationId, ProjectEvent projectEvent, CancellationToken cancellationToken = default)
    {
        WorkItemRelation? existing = await GetRelationAsync(projectId, relationId, cancellationToken).ConfigureAwait(false);
        if (existing is null) return null;
        await DeleteWithEventAsync("DELETE FROM work_item_relations WHERE id=$id AND project_id=$project;", projectId, relationId, projectEvent with { BeforeJson = JsonSerializer.Serialize(existing) }, cancellationToken).ConfigureAwait(false);
        return existing;
    }

    public async Task<IReadOnlyList<WorkItemRelation>> ListRelationsAsync(Guid projectId, Guid workItemId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id,project_id,source_work_item_id,target_work_item_id,relation,created_at FROM work_item_relations WHERE project_id=$project AND (source_work_item_id=$item OR target_work_item_id=$item) ORDER BY created_at,id;";
        command.Parameters.AddWithValue("$project", Format(projectId));
        command.Parameters.AddWithValue("$item", Format(workItemId));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<WorkItemRelation>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(ReadRelation(reader));
        return result;
    }

    public async Task<bool> HasDirectedPathAsync(Guid projectId, Guid fromWorkItemId, Guid toWorkItemId, IReadOnlyCollection<string> relationTypes, CancellationToken cancellationToken = default)
    {
        if (relationTypes.Count == 0) return false;
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        string parameters = string.Join(',', relationTypes.Select((_, index) => $"$relation{index}"));
        command.CommandText = $"""
            WITH RECURSIVE reachable(id) AS (
                SELECT target_work_item_id FROM work_item_relations
                WHERE project_id=$project AND source_work_item_id=$from AND relation IN ({parameters})
                UNION
                SELECT relation.target_work_item_id FROM work_item_relations relation
                JOIN reachable ON relation.source_work_item_id=reachable.id
                WHERE relation.project_id=$project AND relation.relation IN ({parameters})
            )
            SELECT EXISTS(SELECT 1 FROM reachable WHERE id=$to);
            """;
        command.Parameters.AddWithValue("$project", Format(projectId));
        command.Parameters.AddWithValue("$from", Format(fromWorkItemId));
        command.Parameters.AddWithValue("$to", Format(toWorkItemId));
        int index = 0;
        foreach (string relationType in relationTypes) command.Parameters.AddWithValue($"$relation{index++}", relationType);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) != 0;
    }

    public Task AddCommentAsync(WorkItemComment comment, ProjectEvent projectEvent, CancellationToken cancellationToken = default) =>
        InsertWithEventAsync("INSERT INTO work_item_comments(id,project_id,work_item_id,body,author_type,author_name,created_at) VALUES($id,$project,$item,$body,$author_type,$author_name,$created);", command =>
        {
            AddIdentity(command, comment.Id, comment.ProjectId, comment.CreatedAt);
            command.Parameters.AddWithValue("$item", Format(comment.WorkItemId));
            command.Parameters.AddWithValue("$body", comment.Body);
            command.Parameters.AddWithValue("$author_type", comment.AuthorType);
            command.Parameters.AddWithValue("$author_name", comment.AuthorName);
        }, projectEvent, cancellationToken);

    public async Task<IReadOnlyList<WorkItemComment>> ListCommentsAsync(Guid projectId, Guid workItemId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id,project_id,work_item_id,body,author_type,author_name,created_at FROM work_item_comments WHERE project_id=$project AND work_item_id=$item ORDER BY created_at,id;";
        AddScope(command, projectId, workItemId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<WorkItemComment>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(new WorkItemComment(ParseGuid(reader, 0), ParseGuid(reader, 1), ParseGuid(reader, 2), reader.GetString(3), reader.GetString(4), reader.GetString(5), ParseDate(reader, 6)));
        return result;
    }

    public Task AddTaskLinkAsync(WorkItemTaskLink link, ProjectEvent projectEvent, CancellationToken cancellationToken = default) =>
        InsertWithEventAsync("INSERT INTO work_item_task_links(id,project_id,work_item_id,task_system,task_id,relation,created_at) VALUES($id,$project,$item,$system,$task_id,$relation,$created);", command =>
        {
            AddIdentity(command, link.Id, link.ProjectId, link.CreatedAt);
            command.Parameters.AddWithValue("$item", Format(link.WorkItemId));
            command.Parameters.AddWithValue("$system", link.TaskSystem);
            command.Parameters.AddWithValue("$task_id", link.TaskId);
            command.Parameters.AddWithValue("$relation", link.Relation);
        }, projectEvent, cancellationToken);

    public async Task<WorkItemTaskLink?> RemoveTaskLinkAsync(Guid projectId, Guid linkId, ProjectEvent projectEvent, CancellationToken cancellationToken = default)
    {
        WorkItemTaskLink? existing = await GetTaskLinkAsync(projectId, linkId, cancellationToken).ConfigureAwait(false);
        if (existing is null) return null;
        await DeleteWithEventAsync("DELETE FROM work_item_task_links WHERE id=$id AND project_id=$project;", projectId, linkId, projectEvent with { BeforeJson = JsonSerializer.Serialize(existing) }, cancellationToken).ConfigureAwait(false);
        return existing;
    }

    public async Task<IReadOnlyList<WorkItemTaskLink>> ListTaskLinksAsync(Guid projectId, Guid workItemId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id,project_id,work_item_id,task_system,task_id,relation,created_at FROM work_item_task_links WHERE project_id=$project AND work_item_id=$item ORDER BY created_at,id;";
        AddScope(command, projectId, workItemId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<WorkItemTaskLink>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(ReadTaskLink(reader));
        return result;
    }

    public Task AddCommitLinkAsync(WorkItemCommitLink link, ProjectEvent projectEvent, CancellationToken cancellationToken = default) =>
        InsertWithEventAsync("INSERT INTO work_item_commits(id,project_id,work_item_id,commit_hash,relation,created_at) VALUES($id,$project,$item,$hash,$relation,$created);", command =>
        {
            AddIdentity(command, link.Id, link.ProjectId, link.CreatedAt);
            command.Parameters.AddWithValue("$item", Format(link.WorkItemId));
            command.Parameters.AddWithValue("$hash", link.CommitHash);
            command.Parameters.AddWithValue("$relation", link.Relation);
        }, projectEvent, cancellationToken);

    public async Task<WorkItemCommitLink?> RemoveCommitLinkAsync(Guid projectId, Guid linkId, ProjectEvent projectEvent, CancellationToken cancellationToken = default)
    {
        WorkItemCommitLink? existing = await GetCommitLinkAsync(projectId, linkId, cancellationToken).ConfigureAwait(false);
        if (existing is null) return null;
        await DeleteWithEventAsync("DELETE FROM work_item_commits WHERE id=$id AND project_id=$project;", projectId, linkId, projectEvent with { BeforeJson = JsonSerializer.Serialize(existing) }, cancellationToken).ConfigureAwait(false);
        return existing;
    }

    public async Task<IReadOnlyList<WorkItemCommitLink>> ListCommitLinksAsync(Guid projectId, Guid workItemId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id,project_id,work_item_id,commit_hash,relation,created_at FROM work_item_commits WHERE project_id=$project AND work_item_id=$item ORDER BY created_at,id;";
        AddScope(command, projectId, workItemId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<WorkItemCommitLink>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(ReadCommitLink(reader));
        return result;
    }

    public async Task<IReadOnlyList<ProjectEvent>> ListHistoryAsync(Guid projectId, Guid workItemId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id,project_id,entity_type,entity_id,event_type,actor_type,actor_name,before_json,after_json,message,created_at
            FROM events WHERE project_id=$project AND (
                entity_id=$item OR
                entity_id IN (SELECT id FROM work_item_comments WHERE work_item_id=$item) OR
                entity_id IN (SELECT id FROM work_item_task_links WHERE work_item_id=$item) OR
                entity_id IN (SELECT id FROM work_item_commits WHERE work_item_id=$item) OR
                entity_id IN (SELECT id FROM work_item_relations WHERE source_work_item_id=$item OR target_work_item_id=$item) OR
                json_extract(before_json,'$.WorkItemId')=$item OR json_extract(after_json,'$.WorkItemId')=$item OR
                json_extract(before_json,'$.SourceWorkItemId')=$item OR json_extract(after_json,'$.SourceWorkItemId')=$item OR
                json_extract(before_json,'$.TargetWorkItemId')=$item OR json_extract(after_json,'$.TargetWorkItemId')=$item
            ) ORDER BY created_at,id;
            """;
        AddScope(command, projectId, workItemId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<ProjectEvent>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(new ProjectEvent(ParseGuid(reader, 0), ParseGuid(reader, 1), reader.GetString(2), ParseGuid(reader, 3), reader.GetString(4), reader.GetString(5), reader.GetString(6), Optional(reader, 7), Optional(reader, 8), Optional(reader, 9), ParseDate(reader, 10)));
        return result;
    }

    private async Task InsertWithEventAsync(string sql, Action<SqliteCommand> addParameters, ProjectEvent projectEvent, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            addParameters(command);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await InsertEventAsync(connection, transaction, projectEvent, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private async Task DeleteWithEventAsync(string sql, Guid projectId, Guid entityId, ProjectEvent projectEvent, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.Parameters.AddWithValue("$id", Format(entityId));
            command.Parameters.AddWithValue("$project", Format(projectId));
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) throw new InvalidOperationException("The collaboration record no longer exists.");
            await InsertEventAsync(connection, transaction, projectEvent, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<WorkItemRelation?> GetRelationAsync(Guid projectId, Guid relationId, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id,project_id,source_work_item_id,target_work_item_id,relation,created_at FROM work_item_relations WHERE project_id=$project AND id=$id;";
        AddKey(command, projectId, relationId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadRelation(reader) : null;
    }

    private async Task<WorkItemTaskLink?> GetTaskLinkAsync(Guid projectId, Guid linkId, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id,project_id,work_item_id,task_system,task_id,relation,created_at FROM work_item_task_links WHERE project_id=$project AND id=$id;";
        AddKey(command, projectId, linkId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadTaskLink(reader) : null;
    }

    private async Task<WorkItemCommitLink?> GetCommitLinkAsync(Guid projectId, Guid linkId, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id,project_id,work_item_id,commit_hash,relation,created_at FROM work_item_commits WHERE project_id=$project AND id=$id;";
        AddKey(command, projectId, linkId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadCommitLink(reader) : null;
    }

    private Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken) => SqliteConnectionFactory.OpenAsync(_options, cancellationToken);

    private static async Task InsertEventAsync(SqliteConnection connection, SqliteTransaction transaction, ProjectEvent value, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO events(id,project_id,entity_type,entity_id,event_type,actor_type,actor_name,before_json,after_json,message,created_at) VALUES($id,$project,$entity_type,$entity,$event_type,$actor_type,$actor_name,$before,$after,$message,$created);";
        command.Parameters.AddWithValue("$id", Format(value.Id));
        command.Parameters.AddWithValue("$project", Format(value.ProjectId));
        command.Parameters.AddWithValue("$entity_type", value.EntityType);
        command.Parameters.AddWithValue("$entity", Format(value.EntityId));
        command.Parameters.AddWithValue("$event_type", value.EventType);
        command.Parameters.AddWithValue("$actor_type", value.ActorType);
        command.Parameters.AddWithValue("$actor_name", value.ActorName);
        command.Parameters.AddWithValue("$before", DbValue(value.BeforeJson));
        command.Parameters.AddWithValue("$after", DbValue(value.AfterJson));
        command.Parameters.AddWithValue("$message", DbValue(value.Message));
        command.Parameters.AddWithValue("$created", Format(value.CreatedAt));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static WorkItemRelation ReadRelation(SqliteDataReader reader) => new(ParseGuid(reader, 0), ParseGuid(reader, 1), ParseGuid(reader, 2), ParseGuid(reader, 3), reader.GetString(4), ParseDate(reader, 5));
    private static WorkItemTaskLink ReadTaskLink(SqliteDataReader reader) => new(ParseGuid(reader, 0), ParseGuid(reader, 1), ParseGuid(reader, 2), reader.GetString(3), reader.GetString(4), reader.GetString(5), ParseDate(reader, 6));
    private static WorkItemCommitLink ReadCommitLink(SqliteDataReader reader) => new(ParseGuid(reader, 0), ParseGuid(reader, 1), ParseGuid(reader, 2), reader.GetString(3), reader.GetString(4), ParseDate(reader, 5));
    private static void AddIdentity(SqliteCommand command, Guid id, Guid projectId, DateTimeOffset createdAt) { command.Parameters.AddWithValue("$id", Format(id)); command.Parameters.AddWithValue("$project", Format(projectId)); command.Parameters.AddWithValue("$created", Format(createdAt)); }
    private static void AddScope(SqliteCommand command, Guid projectId, Guid workItemId) { command.Parameters.AddWithValue("$project", Format(projectId)); command.Parameters.AddWithValue("$item", Format(workItemId)); }
    private static void AddKey(SqliteCommand command, Guid projectId, Guid id) { command.Parameters.AddWithValue("$project", Format(projectId)); command.Parameters.AddWithValue("$id", Format(id)); }
    private static object DbValue(string? value) => value is null ? DBNull.Value : value;
    private static string? Optional(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static Guid ParseGuid(SqliteDataReader reader, int ordinal) => Guid.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture);
    private static DateTimeOffset ParseDate(SqliteDataReader reader, int ordinal) => DateTimeOffset.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    private static string Format(Guid value) => value.ToString("D", CultureInfo.InvariantCulture);
    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
}
