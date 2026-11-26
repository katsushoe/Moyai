using System.Globalization;
using Microsoft.Data.Sqlite;
using Moyai.Application.Providers;

namespace Moyai.Infrastructure.Persistence;

/// <summary>署名鍵とは独立して、本文を持たない操作監査を保存します。</summary>
public sealed class SqliteRepositoryOperationAudit(SqliteDatabaseOptions options, TimeProvider clock) : IRepositoryOperationAudit
{
    public async Task WriteAsync(RepositoryOperationAuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var existing = options with { ConnectionString = new SqliteConnectionStringBuilder(options.ConnectionString) { Mode = SqliteOpenMode.ReadWrite }.ToString() };
        await using var connection = await SqliteConnectionFactory.OpenAsync(existing, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO repository_operation_audit(operation_id,project_id,provider,operation,branch_fingerprint,outcome,result_code,created_at)
            VALUES($id,$project,$provider,$operation,$branch,$outcome,$code,$created);
            """;
        command.Parameters.AddWithValue("$id", entry.OperationId);
        command.Parameters.AddWithValue("$project", entry.ProjectId.ToString("D"));
        command.Parameters.AddWithValue("$provider", entry.Provider);
        command.Parameters.AddWithValue("$operation", entry.Operation);
        command.Parameters.AddWithValue("$branch", entry.BranchFingerprint);
        command.Parameters.AddWithValue("$outcome", entry.Outcome);
        command.Parameters.AddWithValue("$code", entry.Code);
        command.Parameters.AddWithValue("$created", clock.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
