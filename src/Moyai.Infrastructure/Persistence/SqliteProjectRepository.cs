using System.Globalization;
using Microsoft.Data.Sqlite;
using Moyai.Application.Projects;
using Moyai.Domain.Events;
using Moyai.Domain.Projects;

namespace Moyai.Infrastructure.Persistence;

/// <summary>Projectと対応EventをSQLiteへTransaction保存します。</summary>
public sealed class SqliteProjectRepository : IProjectRepository
{
    private readonly SqliteDatabaseOptions _options;

    public SqliteProjectRepository(SqliteDatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public async Task AddAsync(Project project, ProjectEvent projectEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(projectEvent);
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await InsertProjectAsync(connection, transaction, project, cancellationToken).ConfigureAwait(false);
            await InsertEventAsync(connection, transaction, projectEvent, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException exception) when (exception.SqliteExtendedErrorCode == 2067)
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw new ProjectNameConflictException(project.Name);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<Project?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"{SelectSql} WHERE name = $name COLLATE NOCASE;";
        command.Parameters.AddWithValue("$name", name);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Read(reader) : null;
    }

    public async Task<IReadOnlyList<Project>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = includeArchived ? $"{SelectSql} ORDER BY name COLLATE NOCASE;" : $"{SelectSql} WHERE archived_at IS NULL ORDER BY name COLLATE NOCASE;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var projects = new List<Project>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) projects.Add(Read(reader));
        return projects;
    }

    public async Task UpdateAsync(Project project, long expectedRevision, ProjectEvent projectEvent, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE projects SET name=$name, description=$description, build_config_json=$build_config,
                    git_user_name=$git_name, git_user_email=$git_email, git_remote_name=$remote,
                    git_default_branch=$default_branch, updated_at=$updated, archived_at=$archived,
                    revision=$revision
                WHERE id=$id AND revision=$expected_revision;
                """;
            AddMutableParameters(command, project);
            command.Parameters.AddWithValue("$id", Format(project.Id));
            command.Parameters.AddWithValue("$expected_revision", expectedRevision);
            int affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (affected == 0) throw new RevisionConflictException(expectedRevision);
            await InsertEventAsync(connection, transaction, projectEvent, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException exception) when (exception.SqliteExtendedErrorCode == 2067)
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw new ProjectNameConflictException(project.Name);
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

    private static async Task InsertProjectAsync(SqliteConnection connection, SqliteTransaction transaction, Project project, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO projects(id,name,description,source_path,install_path,repository_url,repository_provider,build_provider,build_config_json,deploy_mode,git_user_name,git_user_email,git_remote_name,git_default_branch,created_at,updated_at,archived_at,revision)
            VALUES($id,$name,$description,$source,$install,$repository_url,$repository_provider,$build_provider,$build_config,$deploy_mode,$git_name,$git_email,$remote,$default_branch,$created,$updated,$archived,$revision);
            """;
        AddMutableParameters(command, project);
        command.Parameters.AddWithValue("$id", Format(project.Id));
        command.Parameters.AddWithValue("$source", project.SourcePath);
        command.Parameters.AddWithValue("$install", Value(project.InstallPath));
        command.Parameters.AddWithValue("$repository_url", project.RepositoryUrl);
        command.Parameters.AddWithValue("$repository_provider", project.RepositoryProvider);
        command.Parameters.AddWithValue("$build_provider", project.BuildProvider);
        command.Parameters.AddWithValue("$deploy_mode", project.DeployMode);
        command.Parameters.AddWithValue("$created", Format(project.CreatedAt));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddMutableParameters(SqliteCommand command, Project project)
    {
        command.Parameters.AddWithValue("$name", project.Name);
        command.Parameters.AddWithValue("$description", Value(project.Description));
        command.Parameters.AddWithValue("$build_config", Value(project.BuildConfigJson));
        command.Parameters.AddWithValue("$git_name", Value(project.GitUserName));
        command.Parameters.AddWithValue("$git_email", Value(project.GitUserEmail));
        command.Parameters.AddWithValue("$remote", project.GitRemoteName);
        command.Parameters.AddWithValue("$default_branch", Value(project.GitDefaultBranch));
        command.Parameters.AddWithValue("$updated", Format(project.UpdatedAt));
        command.Parameters.AddWithValue("$archived", project.ArchivedAt is null ? DBNull.Value : Format(project.ArchivedAt.Value));
        command.Parameters.AddWithValue("$revision", project.Revision);
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

    private static Project Read(SqliteDataReader reader) => Project.RestoreState(
        Guid.Parse(reader.GetString(0), CultureInfo.InvariantCulture), reader.GetString(1), Optional(reader, 2), reader.GetString(3), Optional(reader, 4), reader.GetString(5), reader.GetString(6), reader.GetString(7), Optional(reader, 8), reader.GetString(9), Optional(reader, 10), Optional(reader, 11), reader.GetString(12), Optional(reader, 13), Parse(reader.GetString(14)), Parse(reader.GetString(15)), reader.IsDBNull(16) ? null : Parse(reader.GetString(16)), reader.GetInt64(17));

    private static object Value(string? value) => value is null ? DBNull.Value : value;
    private static string? Optional(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static string Format(Guid value) => value.ToString("D", CultureInfo.InvariantCulture);
    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    private const string SelectSql = "SELECT id,name,description,source_path,install_path,repository_url,repository_provider,build_provider,build_config_json,deploy_mode,git_user_name,git_user_email,git_remote_name,git_default_branch,created_at,updated_at,archived_at,revision FROM projects";
}
