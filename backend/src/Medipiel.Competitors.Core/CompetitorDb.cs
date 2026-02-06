using Microsoft.Data.SqlClient;

namespace Medipiel.Competitors.Core;

public sealed class CompetitorDb
{
    private readonly string _connectionString;

    public CompetitorDb(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public async Task<List<ProductRow>> LoadProductsAsync(
        int competitorId,
        DateTime snapshotDate,
        bool onlyNew,
        int batchSize,
        bool requireEan,
        CancellationToken ct)
    {
        var sql = $@"
SELECT TOP (@BatchSize)
       p.Id,
       p.Ean,
       p.Description,
       cp.Url
FROM Products p
LEFT JOIN CompetitorProducts cp
    ON cp.ProductId = p.Id AND cp.CompetitorId = @CompetitorId
LEFT JOIN PriceSnapshots ps
    ON ps.ProductId = p.Id AND ps.CompetitorId = @CompetitorId AND ps.SnapshotDate = @SnapshotDate
WHERE {(requireEan ? "p.Ean IS NOT NULL" : "1=1")}
  AND NOT (cp.MatchMethod = 'no_match' AND cp.Url IS NULL)
  AND (@OnlyNew = 0 OR ps.Id IS NULL);
";

        var list = new List<ProductRow>();
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@CompetitorId", competitorId);
        command.Parameters.AddWithValue("@SnapshotDate", snapshotDate.Date);
        command.Parameters.AddWithValue("@OnlyNew", onlyNew ? 1 : 0);
        command.Parameters.AddWithValue("@BatchSize", batchSize > 0 ? batchSize : int.MaxValue);

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new ProductRow(
                reader.GetInt32(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)
            ));
        }

        return list;
    }

    public async Task<bool> UpsertCompetitorProductAsync(
        int productId,
        int competitorId,
        string? url,
        string? name,
        string? matchMethod,
        decimal? matchScore,
        DateTime? lastMatchedAt,
        CancellationToken ct)
    {
        const string sql = @"
IF EXISTS (SELECT 1 FROM CompetitorProducts WHERE ProductId = @ProductId AND CompetitorId = @CompetitorId)
BEGIN
    UPDATE CompetitorProducts
    SET Url = @Url,
        Name = COALESCE(@Name, Name),
        MatchMethod = COALESCE(@MatchMethod, MatchMethod),
        MatchScore = COALESCE(@MatchScore, MatchScore),
        LastMatchedAt = COALESCE(@LastMatchedAt, SYSUTCDATETIME())
    WHERE ProductId = @ProductId AND CompetitorId = @CompetitorId;
END
ELSE
BEGIN
    INSERT INTO CompetitorProducts (ProductId, CompetitorId, Url, Name, MatchMethod, MatchScore, LastMatchedAt)
    VALUES (@ProductId, @CompetitorId, @Url, @Name, @MatchMethod, @MatchScore, COALESCE(@LastMatchedAt, SYSUTCDATETIME()));
END
";

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ProductId", productId);
        command.Parameters.AddWithValue("@CompetitorId", competitorId);
        command.Parameters.AddWithValue("@Url", (object?)url ?? DBNull.Value);
        command.Parameters.AddWithValue("@Name", (object?)name ?? DBNull.Value);
        command.Parameters.AddWithValue("@MatchMethod", (object?)matchMethod ?? DBNull.Value);
        command.Parameters.AddWithValue("@MatchScore", (object?)matchScore ?? DBNull.Value);
        command.Parameters.AddWithValue("@LastMatchedAt", (object?)lastMatchedAt ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
        return true;
    }

    public Task<bool> MarkNoMatchAsync(int productId, int competitorId, CancellationToken ct)
    {
        return UpsertCompetitorProductAsync(
            productId,
            competitorId,
            null,
            null,
            "no_match",
            null,
            DateTime.UtcNow,
            ct
        );
    }

    public async Task<bool> UpsertPriceSnapshotAsync(
        int productId,
        int competitorId,
        DateTime snapshotDate,
        decimal? listPrice,
        decimal? promoPrice,
        CancellationToken ct)
    {
        const string sql = @"
IF EXISTS (
    SELECT 1 FROM PriceSnapshots
    WHERE ProductId = @ProductId
      AND CompetitorId = @CompetitorId
      AND SnapshotDate = @SnapshotDate
)
BEGIN
    UPDATE PriceSnapshots
    SET ListPrice = @ListPrice,
        PromoPrice = @PromoPrice
    WHERE ProductId = @ProductId
      AND CompetitorId = @CompetitorId
      AND SnapshotDate = @SnapshotDate;
END
ELSE
BEGIN
    INSERT INTO PriceSnapshots (ProductId, CompetitorId, SnapshotDate, ListPrice, PromoPrice)
    VALUES (@ProductId, @CompetitorId, @SnapshotDate, @ListPrice, @PromoPrice);
END
";

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ProductId", productId);
        command.Parameters.AddWithValue("@CompetitorId", competitorId);
        command.Parameters.AddWithValue("@SnapshotDate", snapshotDate.Date);
        command.Parameters.AddWithValue("@ListPrice", (object?)listPrice ?? DBNull.Value);
        command.Parameters.AddWithValue("@PromoPrice", (object?)promoPrice ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
        return true;
    }
}
