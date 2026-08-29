using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Moyai.Application.Releases;
using Moyai.Domain.Events;
using Moyai.Domain.Releases;

namespace Moyai.Infrastructure.Persistence;

/// <summary>Release WorkItem関連とArtifact MetadataをSQLiteへ永続化します。</summary>
public sealed class SqliteReleaseContentRepository(SqliteDatabaseOptions options) : IReleaseContentRepository
{
    public Task AddWorkItemAsync(ReleaseWorkItem item, ProjectEvent projectEvent, CancellationToken cancellationToken = default) =>
        InsertAsync("INSERT INTO release_work_items(id,project_id,release_id,work_item_id,relation,created_at) VALUES($id,$project,$release,$item,$relation,$created);", command =>
        {
            command.Parameters.AddWithValue("$id", Format(item.Id));
            command.Parameters.AddWithValue("$project", Format(item.ProjectId));
            command.Parameters.AddWithValue("$release", Format(item.ReleaseId));
            command.Parameters.AddWithValue("$item", Format(item.WorkItemId));
            command.Parameters.AddWithValue("$relation", item.Relation);
            command.Parameters.AddWithValue("$created", Format(item.CreatedAt));
        }, projectEvent, cancellationToken);

    public Task<bool> RemoveWorkItemAsync(Guid projectId, Guid releaseId, Guid relationId, ProjectEvent projectEvent, CancellationToken cancellationToken = default) =>
        DeleteAsync("DELETE FROM release_work_items WHERE id=$id AND project_id=$project AND release_id=$release;", projectId, releaseId, relationId, projectEvent, cancellationToken);

    public async Task<IReadOnlyList<ReleaseWorkItem>> ListWorkItemsAsync(Guid projectId, Guid releaseId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id,project_id,release_id,work_item_id,relation,created_at FROM release_work_items WHERE project_id=$project AND release_id=$release ORDER BY created_at,id;";
        AddScope(command, projectId, releaseId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<ReleaseWorkItem>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(new(ParseGuid(reader, 0), ParseGuid(reader, 1), ParseGuid(reader, 2), ParseGuid(reader, 3), reader.GetString(4), ParseDate(reader.GetString(5))));
        return result;
    }

    public Task AddArtifactAsync(ReleaseArtifact artifact, ProjectEvent projectEvent, CancellationToken cancellationToken = default) =>
        InsertAsync("""
            INSERT INTO release_artifacts(id,project_id,release_id,build_artifact_id,name,artifact_type,platform,architecture,file_name,file_path,download_url,file_size,sha256,signature_path,signature_url,created_at,updated_at)
            VALUES($id,$project,$release,$build,$name,$type,$platform,$architecture,$file_name,$file_path,$url,$size,$sha,$signature_path,$signature_url,$created,$updated);
            """, command => AddArtifactParameters(command, artifact), projectEvent, cancellationToken);

    public Task<bool> RemoveArtifactAsync(Guid projectId, Guid releaseId, Guid artifactId, ProjectEvent projectEvent, CancellationToken cancellationToken = default) =>
        DeleteAsync("DELETE FROM release_artifacts WHERE id=$id AND project_id=$project AND release_id=$release;", projectId, releaseId, artifactId, projectEvent, cancellationToken);

    public async Task<IReadOnlyList<ReleaseArtifact>> ListArtifactsAsync(Guid projectId, Guid releaseId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id,project_id,release_id,build_artifact_id,name,artifact_type,platform,architecture,file_name,file_path,download_url,file_size,sha256,signature_path,signature_url,created_at,updated_at FROM release_artifacts WHERE project_id=$project AND release_id=$release ORDER BY created_at,id;";
        AddScope(command, projectId, releaseId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<ReleaseArtifact>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(ReadArtifact(reader));
        return result;
    }

    private async Task InsertAsync(string sql, Action<SqliteCommand> parameters, ProjectEvent projectEvent, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            parameters(command);
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

    private async Task<bool> DeleteAsync(string sql, Guid projectId, Guid releaseId, Guid id, ProjectEvent projectEvent, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            AddScope(command, projectId, releaseId);
            command.Parameters.AddWithValue("$id", Format(id));
            bool removed = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
            if (removed) await InsertEventAsync(connection, transaction, projectEvent, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return removed;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken) => await SqliteConnectionFactory.OpenAsync(options, cancellationToken).ConfigureAwait(false);
    private static void AddScope(SqliteCommand command, Guid projectId, Guid releaseId) { command.Parameters.AddWithValue("$project", Format(projectId)); command.Parameters.AddWithValue("$release", Format(releaseId)); }
    private static void AddArtifactParameters(SqliteCommand command, ReleaseArtifact value)
    {
        command.Parameters.AddWithValue("$id", Format(value.Id)); AddScope(command, value.ProjectId, value.ReleaseId);
        command.Parameters.AddWithValue("$build", value.BuildArtifactId is null ? DBNull.Value : Format(value.BuildArtifactId.Value));
        command.Parameters.AddWithValue("$name", value.Name); command.Parameters.AddWithValue("$type", value.ArtifactType); command.Parameters.AddWithValue("$platform", value.Platform); command.Parameters.AddWithValue("$architecture", value.Architecture); command.Parameters.AddWithValue("$file_name", value.FileName);
        command.Parameters.AddWithValue("$file_path", Db(value.FilePath)); command.Parameters.AddWithValue("$url", Db(value.DownloadUrl)); command.Parameters.AddWithValue("$size", value.FileSize is null ? DBNull.Value : value.FileSize.Value); command.Parameters.AddWithValue("$sha", Db(value.Sha256)); command.Parameters.AddWithValue("$signature_path", Db(value.SignaturePath)); command.Parameters.AddWithValue("$signature_url", Db(value.SignatureUrl)); command.Parameters.AddWithValue("$created", Format(value.CreatedAt)); command.Parameters.AddWithValue("$updated", Format(value.UpdatedAt));
    }
    private static async Task InsertEventAsync(SqliteConnection connection, SqliteTransaction transaction, ProjectEvent value, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT INTO events(id,project_id,entity_type,entity_id,event_type,actor_type,actor_name,before_json,after_json,message,created_at) VALUES($id,$project,$entity_type,$entity,$event_type,$actor_type,$actor_name,$before,$after,$message,$created);";
        command.Parameters.AddWithValue("$id", Format(value.Id)); command.Parameters.AddWithValue("$project", Format(value.ProjectId)); command.Parameters.AddWithValue("$entity_type", value.EntityType); command.Parameters.AddWithValue("$entity", Format(value.EntityId)); command.Parameters.AddWithValue("$event_type", value.EventType); command.Parameters.AddWithValue("$actor_type", value.ActorType); command.Parameters.AddWithValue("$actor_name", value.ActorName); command.Parameters.AddWithValue("$before", Db(value.BeforeJson)); command.Parameters.AddWithValue("$after", Db(value.AfterJson)); command.Parameters.AddWithValue("$message", Db(value.Message)); command.Parameters.AddWithValue("$created", Format(value.CreatedAt));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
    private static ReleaseArtifact ReadArtifact(SqliteDataReader r) => new(ParseGuid(r, 0), ParseGuid(r, 1), ParseGuid(r, 2), r.IsDBNull(3) ? null : ParseGuid(r, 3), r.GetString(4), r.GetString(5), r.GetString(6), r.GetString(7), r.GetString(8), Text(r, 9), Text(r, 10), r.IsDBNull(11) ? null : r.GetInt64(11), Text(r, 12), Text(r, 13), Text(r, 14), ParseDate(r.GetString(15)), ParseDate(r.GetString(16)));
    private static Guid ParseGuid(SqliteDataReader reader, int ordinal) => Guid.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture);
    private static string? Text(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static object Db(string? value) => value is null ? DBNull.Value : value;
    private static string Format(Guid value) => value.ToString("D", CultureInfo.InvariantCulture);
    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset ParseDate(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
