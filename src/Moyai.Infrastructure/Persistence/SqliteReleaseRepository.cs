using System.Globalization;
using Microsoft.Data.Sqlite;
using Moyai.Application.Projects;
using Moyai.Application.Releases;
using Moyai.Domain.Events;
using Moyai.Domain.Releases;

namespace Moyai.Infrastructure.Persistence;

/// <summary>Releaseと監査EventをSQLiteで原子的に永続化します。</summary>
public sealed class SqliteReleaseRepository : IReleaseRepository
{
    private const string SelectSql = "SELECT id,project_id,version,channel,status,tag_name,commit_hash,release_notes,planned_at,released_at,created_at,updated_at,deleted_at,revision FROM releases";
    private readonly SqliteDatabaseOptions _options;

    public SqliteReleaseRepository(SqliteDatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public async Task AddAsync(Release release, ProjectEvent projectEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(projectEvent);
        await using SqliteConnection connection = await SqliteConnectionFactory.OpenAsync(_options, cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO releases(id,project_id,version,channel,status,tag_name,commit_hash,release_notes,planned_at,released_at,created_at,updated_at,deleted_at,revision)
                VALUES($id,$project,$version,$channel,$status,$tag,$commit,$notes,$planned,$released,$created,$updated,$deleted,$revision);
                """;
            AddParameters(command, release, includeIdentity: true);
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

    public async Task<Release?> GetAsync(Guid projectId, string version, bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        await using SqliteConnection connection = await SqliteConnectionFactory.OpenAsync(_options, cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"{SelectSql} WHERE project_id=$project AND version=$version{(includeDeleted ? string.Empty : " AND deleted_at IS NULL")};";
        command.Parameters.AddWithValue("$project", Format(projectId));
        command.Parameters.AddWithValue("$version", version);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Read(reader) : null;
    }

    public async Task<IReadOnlyList<Release>> ListAsync(Guid projectId, bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await SqliteConnectionFactory.OpenAsync(_options, cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"{SelectSql} WHERE project_id=$project{(includeDeleted ? string.Empty : " AND deleted_at IS NULL")} ORDER BY created_at DESC,version;";
        command.Parameters.AddWithValue("$project", Format(projectId));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var releases = new List<Release>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) releases.Add(Read(reader));
        return releases;
    }

    public async Task UpdateAsync(Release release, long expectedRevision, ProjectEvent projectEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(projectEvent);
        await using SqliteConnection connection = await SqliteConnectionFactory.OpenAsync(_options, cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE releases SET channel=$channel,status=$status,tag_name=$tag,commit_hash=$commit,release_notes=$notes,planned_at=$planned,released_at=$released,updated_at=$updated,deleted_at=$deleted,revision=$revision WHERE id=$id AND revision=$expected;";
            AddParameters(command, release, includeIdentity: false);
            command.Parameters.AddWithValue("$id", Format(release.Id));
            command.Parameters.AddWithValue("$expected", expectedRevision);
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 0) throw new RevisionConflictException(expectedRevision);
            await InsertEventAsync(connection, transaction, projectEvent, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static void AddParameters(SqliteCommand command, Release release, bool includeIdentity)
    {
        if (includeIdentity)
        {
            command.Parameters.AddWithValue("$id", Format(release.Id));
            command.Parameters.AddWithValue("$project", Format(release.ProjectId));
            command.Parameters.AddWithValue("$version", release.Version);
            command.Parameters.AddWithValue("$created", Format(release.CreatedAt));
        }
        command.Parameters.AddWithValue("$channel", release.Channel.ToString().ToLowerInvariant());
        command.Parameters.AddWithValue("$status", release.Status.ToString().ToLowerInvariant());
        command.Parameters.AddWithValue("$tag", Value(release.TagName));
        command.Parameters.AddWithValue("$commit", Value(release.CommitHash));
        command.Parameters.AddWithValue("$notes", Value(release.ReleaseNotes));
        command.Parameters.AddWithValue("$planned", release.PlannedAt is null ? DBNull.Value : Format(release.PlannedAt.Value));
        command.Parameters.AddWithValue("$released", release.ReleasedAt is null ? DBNull.Value : Format(release.ReleasedAt.Value));
        command.Parameters.AddWithValue("$updated", Format(release.UpdatedAt));
        command.Parameters.AddWithValue("$deleted", release.DeletedAt is null ? DBNull.Value : Format(release.DeletedAt.Value));
        command.Parameters.AddWithValue("$revision", release.Revision);
    }

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
        command.Parameters.AddWithValue("$before", Value(value.BeforeJson));
        command.Parameters.AddWithValue("$after", Value(value.AfterJson));
        command.Parameters.AddWithValue("$message", Value(value.Message));
        command.Parameters.AddWithValue("$created", Format(value.CreatedAt));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static Release Read(SqliteDataReader reader) => Release.RestoreState(Guid.Parse(reader.GetString(0), CultureInfo.InvariantCulture), Guid.Parse(reader.GetString(1), CultureInfo.InvariantCulture), reader.GetString(2), Enum.Parse<ReleaseChannel>(reader.GetString(3), true), Enum.Parse<ReleaseStatus>(reader.GetString(4), true), Optional(reader, 5), Optional(reader, 6), Optional(reader, 7), Date(reader, 8), Date(reader, 9), ParseDate(reader.GetString(10)), ParseDate(reader.GetString(11)), Date(reader, 12), reader.GetInt64(13));
    private static string? Optional(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static DateTimeOffset? Date(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : ParseDate(reader.GetString(ordinal));
    private static object Value(string? value) => value is null ? DBNull.Value : value;
    private static string Format(Guid value) => value.ToString("D", CultureInfo.InvariantCulture);
    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset ParseDate(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
