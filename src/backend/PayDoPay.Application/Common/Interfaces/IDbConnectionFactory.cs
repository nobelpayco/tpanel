using System.Data;

namespace PayDoPay.Application.Common.Interfaces;

/// <summary>
/// Dapper tabanlı ağır rapor sorguları için ham MySQL bağlantısı üretir.
/// </summary>
public interface IDbConnectionFactory
{
    Task<IDbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default);
}
