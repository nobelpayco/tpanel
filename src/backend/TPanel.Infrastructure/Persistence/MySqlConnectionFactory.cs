using System.Data;
using MySqlConnector;
using TPanel.Application.Common.Interfaces;

namespace TPanel.Infrastructure.Persistence;

/// <summary>Dapper için açık MySQL bağlantısı üretir.</summary>
public class MySqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public MySqlConnectionFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IDbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
