using System.Globalization;
using Microsoft.Data.Sqlite;
using Moyai.Application.Builds;
using Moyai.Application.Projects;
using Moyai.Domain.Builds;
using Moyai.Domain.Events;

namespace Moyai.Infrastructure.Persistence;

/// <summary>Build、Artifact、Audit EventをSQLiteへ永続化します。</summary>
public sealed class SqliteBuildRepository(SqliteDatabaseOptions options) : IBuildRepository
{
    private const string SelectSql = "SELECT id,project_id,provider,status,source_commit,configuration,config_json,started_at,finished_at,actor_type,actor_name,error_code,error_message,created_at,revision FROM builds";

    public Task AddAsync(Build build, ProjectEvent projectEvent, CancellationToken cancellationToken = default) => WriteAsync("INSERT INTO builds(id,project_id,provider,status,source_commit,configuration,config_json,started_at,finished_at,actor_type,actor_name,error_code,error_message,created_at,revision) VALUES($id,$project,$provider,$status,$commit,$configuration,$config,$started,$finished,$actor_type,$actor_name,$error_code,$error_message,$created,$revision);", build, null, projectEvent, cancellationToken);
    public Task UpdateAsync(Build build, long expectedRevision, ProjectEvent projectEvent, CancellationToken cancellationToken = default) => WriteAsync("UPDATE builds SET status=$status,started_at=$started,finished_at=$finished,error_code=$error_code,error_message=$error_message,revision=$revision WHERE id=$id AND project_id=$project AND revision=$expected;", build, expectedRevision, projectEvent, cancellationToken);

    public async Task<Build?> GetAsync(Guid projectId, Guid buildId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await SqliteConnectionFactory.OpenAsync(options, cancellationToken).ConfigureAwait(false); await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"{SelectSql} WHERE project_id=$project AND id=$id;"; command.Parameters.AddWithValue("$project", Format(projectId)); command.Parameters.AddWithValue("$id", Format(buildId));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false); return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Read(reader) : null;
    }

    public async Task<IReadOnlyList<Build>> ListAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await SqliteConnectionFactory.OpenAsync(options, cancellationToken).ConfigureAwait(false); await using SqliteCommand command = connection.CreateCommand(); command.CommandText = $"{SelectSql} WHERE project_id=$project ORDER BY created_at DESC,id DESC;"; command.Parameters.AddWithValue("$project", Format(projectId));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false); var result = new List<Build>(); while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(Read(reader)); return result;
    }

    public async Task<IReadOnlyList<BuildArtifact>> ListArtifactsAsync(Guid projectId, Guid buildId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await SqliteConnectionFactory.OpenAsync(options, cancellationToken).ConfigureAwait(false); await using SqliteCommand command = connection.CreateCommand(); command.CommandText = "SELECT id,project_id,build_id,name,artifact_type,artifact_kind,file_path,file_size,sha256,manifest_sha256,created_at FROM build_artifacts WHERE project_id=$project AND build_id=$build ORDER BY created_at,id;"; command.Parameters.AddWithValue("$project", Format(projectId)); command.Parameters.AddWithValue("$build", Format(buildId));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false); var result = new List<BuildArtifact>(); while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(new(ParseGuid(reader, 0), ParseGuid(reader, 1), ParseGuid(reader, 2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetInt64(7), Text(reader, 8), Text(reader, 9), ParseDate(reader.GetString(10)))); return result;
    }

    public async Task AddArtifactAsync(BuildArtifact artifact, ProjectEvent projectEvent, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await SqliteConnectionFactory.OpenAsync(options, cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO build_artifacts(id,project_id,build_id,name,artifact_type,artifact_kind,file_path,file_size,sha256,manifest_sha256,created_at) VALUES($id,$project,$build,$name,$type,$kind,$path,$size,$sha,$manifest,$created);";
            command.Parameters.AddWithValue("$id", Format(artifact.Id)); command.Parameters.AddWithValue("$project", Format(artifact.ProjectId)); command.Parameters.AddWithValue("$build", Format(artifact.BuildId)); command.Parameters.AddWithValue("$name", artifact.Name); command.Parameters.AddWithValue("$type", artifact.ArtifactType); command.Parameters.AddWithValue("$kind", artifact.ArtifactKind); command.Parameters.AddWithValue("$path", artifact.FilePath); command.Parameters.AddWithValue("$size", artifact.FileSize is null ? DBNull.Value : artifact.FileSize.Value); command.Parameters.AddWithValue("$sha", Db(artifact.Sha256)); command.Parameters.AddWithValue("$manifest", Db(artifact.ManifestSha256)); command.Parameters.AddWithValue("$created", Format(artifact.CreatedAt));
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

    private async Task WriteAsync(string sql, Build build, long? expectedRevision, ProjectEvent projectEvent, CancellationToken token)
    {
        await using SqliteConnection connection = await SqliteConnectionFactory.OpenAsync(options, token).ConfigureAwait(false); await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token).ConfigureAwait(false);
        try { await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql; Add(command, build); if (expectedRevision is not null) command.Parameters.AddWithValue("$expected", expectedRevision.Value); int changed = await command.ExecuteNonQueryAsync(token).ConfigureAwait(false); if (changed != 1) throw new RevisionConflictException(expectedRevision ?? 0); await InsertEventAsync(connection, transaction, projectEvent, token).ConfigureAwait(false); await transaction.CommitAsync(token).ConfigureAwait(false); } catch { await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false); throw; }
    }
    private static void Add(SqliteCommand c, Build b) { c.Parameters.AddWithValue("$id", Format(b.Id)); c.Parameters.AddWithValue("$project", Format(b.ProjectId)); c.Parameters.AddWithValue("$provider", b.Provider); c.Parameters.AddWithValue("$status", b.Status.ToString().ToLowerInvariant()); c.Parameters.AddWithValue("$commit", b.SourceCommit); c.Parameters.AddWithValue("$configuration", b.Configuration); c.Parameters.AddWithValue("$config", Db(b.ConfigJson)); c.Parameters.AddWithValue("$started", Db(b.StartedAt)); c.Parameters.AddWithValue("$finished", Db(b.FinishedAt)); c.Parameters.AddWithValue("$actor_type", b.ActorType); c.Parameters.AddWithValue("$actor_name", b.ActorName); c.Parameters.AddWithValue("$error_code", Db(b.ErrorCode)); c.Parameters.AddWithValue("$error_message", Db(b.ErrorMessage)); c.Parameters.AddWithValue("$created", Format(b.CreatedAt)); c.Parameters.AddWithValue("$revision", b.Revision); }
    private static async Task InsertEventAsync(SqliteConnection connection, SqliteTransaction transaction, ProjectEvent e, CancellationToken token) { await using SqliteCommand c = connection.CreateCommand(); c.Transaction = transaction; c.CommandText = "INSERT INTO events(id,project_id,entity_type,entity_id,event_type,actor_type,actor_name,before_json,after_json,message,created_at) VALUES($id,$project,$entity_type,$entity,$event_type,$actor_type,$actor_name,$before,$after,$message,$created);"; c.Parameters.AddWithValue("$id", Format(e.Id)); c.Parameters.AddWithValue("$project", Format(e.ProjectId)); c.Parameters.AddWithValue("$entity_type", e.EntityType); c.Parameters.AddWithValue("$entity", Format(e.EntityId)); c.Parameters.AddWithValue("$event_type", e.EventType); c.Parameters.AddWithValue("$actor_type", e.ActorType); c.Parameters.AddWithValue("$actor_name", e.ActorName); c.Parameters.AddWithValue("$before", Db(e.BeforeJson)); c.Parameters.AddWithValue("$after", Db(e.AfterJson)); c.Parameters.AddWithValue("$message", Db(e.Message)); c.Parameters.AddWithValue("$created", Format(e.CreatedAt)); await c.ExecuteNonQueryAsync(token).ConfigureAwait(false); }
    private static Build Read(SqliteDataReader r) => Build.Restore(ParseGuid(r, 0), ParseGuid(r, 1), r.GetString(2), Enum.Parse<BuildStatus>(r.GetString(3), true), r.GetString(4), r.GetString(5), Text(r, 6), Date(r, 7), Date(r, 8), r.GetString(9), r.GetString(10), Text(r, 11), Text(r, 12), ParseDate(r.GetString(13)), r.GetInt64(14));
    private static Guid ParseGuid(SqliteDataReader r, int i) => Guid.Parse(r.GetString(i), CultureInfo.InvariantCulture); private static string? Text(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i); private static DateTimeOffset? Date(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : ParseDate(r.GetString(i)); private static object Db(string? v) => v is null ? DBNull.Value : v; private static object Db(DateTimeOffset? v) => v is null ? DBNull.Value : Format(v.Value); private static string Format(Guid v) => v.ToString("D", CultureInfo.InvariantCulture); private static string Format(DateTimeOffset v) => v.ToString("O", CultureInfo.InvariantCulture); private static DateTimeOffset ParseDate(string v) => DateTimeOffset.Parse(v, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
