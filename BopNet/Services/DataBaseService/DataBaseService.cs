using BopNet.Models;
using Microsoft.Data.Sqlite;

namespace BopNet.Services.DataBaseService;

public sealed class DataBaseService : IDatabase
{
    private readonly SqliteConnection _connection;

    public DataBaseService(string connectionString)
    {
        _connection = new SqliteConnection(connectionString);
        try
        {
            _connection.Open();
            using var command = _connection.CreateCommand();
            // Preserve the original EF Core schema and existing databases.
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS "Tracks" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_Tracks" PRIMARY KEY AUTOINCREMENT,
                    "Reference" TEXT NOT NULL,
                    "FullUrl" TEXT NOT NULL,
                    "FilePath" TEXT NULL,
                    "PlayCount" INTEGER NOT NULL,
                    "SongName" TEXT NULL,
                    "Artist" TEXT NULL
                );
                """;
            command.ExecuteNonQuery();
        }
        catch
        {
            _connection.Dispose();
            throw;
        }
    }

    public Track SaveTrack(Track track)
    {
        // Serialize duplicate checking and insertion without changing the schema.
        using var transaction = _connection.BeginTransaction(deferred: false);
        using var check = _connection.CreateCommand();
        check.Transaction = transaction;
        check.CommandText = "SELECT 1 FROM Tracks WHERE Reference = $reference LIMIT 1";
        check.Parameters.AddWithValue("$reference", track.Reference);
        if (check.ExecuteScalar() is not null)
            throw new InvalidOperationException($"Track already exists: {track.Reference}");

        using var insert = _connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO Tracks (Reference, FullUrl, FilePath, PlayCount, SongName, Artist)
            VALUES ($reference, $url, $path, $count, $name, $artist)
            RETURNING Id;
            """;
        insert.Parameters.AddWithValue("$reference", track.Reference);
        insert.Parameters.AddWithValue("$url", track.FullUrl);
        insert.Parameters.AddWithValue("$path", (object?)track.FilePath ?? DBNull.Value);
        insert.Parameters.AddWithValue("$count", track.PlayCount);
        insert.Parameters.AddWithValue("$name", (object?)track.SongName ?? DBNull.Value);
        insert.Parameters.AddWithValue("$artist", (object?)track.Artist ?? DBNull.Value);
        var id = checked((int)(long)insert.ExecuteScalar()!);
        transaction.Commit();
        track.Id = id;
        return track;
    }

    public Track? UpdateTrackPlayCount(Track track)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            UPDATE Tracks SET PlayCount = PlayCount + 1
            WHERE Id = (SELECT Id FROM Tracks WHERE Reference = $reference ORDER BY Id LIMIT 1)
            RETURNING Id, Reference, FullUrl, FilePath, PlayCount, SongName, Artist;
            """;
        command.Parameters.AddWithValue("$reference", track.Reference);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            throw new InvalidOperationException($"Track does not exist: {track.Reference}");
        var updated = ReadTrack(reader);
        track.PlayCount = updated.PlayCount;
        return updated;
    }

    public Track? GetTrack(string trackReference)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Reference, FullUrl, FilePath, PlayCount, SongName, Artist
            FROM Tracks WHERE Reference = $reference ORDER BY Id LIMIT 1;
            """;
        command.Parameters.AddWithValue("$reference", trackReference);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadTrack(reader) : null;
    }

    private static Track ReadTrack(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        Reference = reader.GetString(1),
        FullUrl = reader.GetString(2),
        FilePath = reader.IsDBNull(3) ? null : reader.GetString(3),
        PlayCount = reader.GetInt32(4),
        SongName = reader.IsDBNull(5) ? null : reader.GetString(5),
        Artist = reader.IsDBNull(6) ? null : reader.GetString(6)
    };

    public void Dispose() => _connection.Dispose();
}
