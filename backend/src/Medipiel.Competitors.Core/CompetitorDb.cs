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
       cp.Url,
       b.Name AS BrandName
FROM Products p
LEFT JOIN Brands b
    ON b.Id = p.BrandId
LEFT JOIN CompetitorProducts cp
    ON cp.ProductId = p.Id AND cp.CompetitorId = @CompetitorId
LEFT JOIN PriceSnapshots ps
    ON ps.ProductId = p.Id AND ps.CompetitorId = @CompetitorId AND ps.SnapshotDate = @SnapshotDate
WHERE {(requireEan ? "p.Ean IS NOT NULL" : "1=1")}
  AND (
        @OnlyNew = 0
        OR cp.ProductId IS NULL
        OR NOT (cp.MatchMethod = 'no_match' AND cp.Url IS NULL)
      )
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
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)
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

    public async Task<bool> UpsertCompetitorCatalogAsync(
        int competitorId,
        string url,
        string? name,
        string? description,
        string? ean,
        string? competitorSku,
        string? brand,
        string? categories,
        decimal? listPrice,
        decimal? promoPrice,
        DateTime? extractedAt,
        CancellationToken ct)
    {
        const string sql = @"
IF EXISTS (SELECT 1 FROM CompetitorCatalog WHERE CompetitorId = @CompetitorId AND Url = @Url)
BEGIN
    UPDATE CompetitorCatalog
    SET Name = @Name,
        Description = @Description,
        Ean = @Ean,
        CompetitorSku = @CompetitorSku,
        Brand = @Brand,
        Categories = @Categories,
        ListPrice = @ListPrice,
        PromoPrice = @PromoPrice,
        ExtractedAt = COALESCE(@ExtractedAt, SYSUTCDATETIME()),
        UpdatedAt = SYSUTCDATETIME()
    WHERE CompetitorId = @CompetitorId AND Url = @Url;
END
ELSE
BEGIN
    INSERT INTO CompetitorCatalog (
        CompetitorId,
        Url,
        Name,
        Description,
        Ean,
        CompetitorSku,
        Brand,
        Categories,
        ListPrice,
        PromoPrice,
        ExtractedAt,
        UpdatedAt
    )
    VALUES (
        @CompetitorId,
        @Url,
        @Name,
        @Description,
        @Ean,
        @CompetitorSku,
        @Brand,
        @Categories,
        @ListPrice,
        @PromoPrice,
        COALESCE(@ExtractedAt, SYSUTCDATETIME()),
        SYSUTCDATETIME()
    );
END
";

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@CompetitorId", competitorId);
        command.Parameters.AddWithValue("@Url", url);
        command.Parameters.AddWithValue("@Name", (object?)name ?? DBNull.Value);
        command.Parameters.AddWithValue("@Description", (object?)description ?? DBNull.Value);
        command.Parameters.AddWithValue("@Ean", (object?)ean ?? DBNull.Value);
        command.Parameters.AddWithValue("@CompetitorSku", (object?)competitorSku ?? DBNull.Value);
        command.Parameters.AddWithValue("@Brand", (object?)brand ?? DBNull.Value);
        command.Parameters.AddWithValue("@Categories", (object?)categories ?? DBNull.Value);
        command.Parameters.AddWithValue("@ListPrice", (object?)listPrice ?? DBNull.Value);
        command.Parameters.AddWithValue("@PromoPrice", (object?)promoPrice ?? DBNull.Value);
        command.Parameters.AddWithValue("@ExtractedAt", (object?)extractedAt ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
        return true;
    }

    public async Task<List<CompetitorCatalogRow>> LoadCompetitorCatalogAsync(
        int competitorId,
        CancellationToken ct)
    {
        const string sql = @"
SELECT Url,
       Name,
       Description,
       Ean,
       CompetitorSku,
       Brand,
       Categories,
       ListPrice,
       PromoPrice,
       ExtractedAt
FROM CompetitorCatalog
WHERE CompetitorId = @CompetitorId;
";

        var list = new List<CompetitorCatalogRow>();
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@CompetitorId", competitorId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new CompetitorCatalogRow(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetDecimal(7),
                reader.IsDBNull(8) ? null : reader.GetDecimal(8),
                reader.IsDBNull(9) ? null : reader.GetDateTime(9)
            ));
        }

        return list;
    }

    public sealed record CompetitorCatalogRow(
        string Url,
        string? Name,
        string? Description,
        string? Ean,
        string? CompetitorSku,
        string? Brand,
        string? Categories,
        decimal? ListPrice,
        decimal? PromoPrice,
        DateTime? ExtractedAt
    );

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
