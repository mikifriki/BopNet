using BopNet.Models;
using BopNet.Services.DataBaseService;
using Microsoft.Data.Sqlite;

namespace BopNetTest;

public class DataBaseServiceTest
{
    private string _directory = null!;
    private string _connectionString = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"bopnet-db-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(_directory, "bot.db"), Pooling = false
        }.ToString();
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    [TestCase(false)]
    [TestCase(true)]
    public void SaveAndReopen_PreservesAllFields(bool hasMetadata)
    {
        var track = NewTrack("quoted ' reference; --");
        track.FilePath = hasMetadata ? "tracks/music.final" : null;
        track.SongName = hasMetadata ? "Song's name" : null;
        track.Artist = hasMetadata ? "Artist" : null;
        track.PlayCount = 7;
        using (var database = new DataBaseService(_connectionString))
            Assert.That(database.SaveTrack(track), Is.SameAs(track));

        using var reopened = new DataBaseService(_connectionString);
        var actual = reopened.GetTrack(track.Reference)!;
        Assert.Multiple(() =>
        {
            Assert.That(track.Id, Is.GreaterThan(0));
            Assert.That(actual.Id, Is.EqualTo(track.Id));
            Assert.That(actual.Reference, Is.EqualTo(track.Reference));
            Assert.That(actual.FullUrl, Is.EqualTo(track.FullUrl));
            Assert.That(actual.FilePath, Is.EqualTo(track.FilePath));
            Assert.That(actual.PlayCount, Is.EqualTo(track.PlayCount));
            Assert.That(actual.SongName, Is.EqualTo(track.SongName));
            Assert.That(actual.Artist, Is.EqualTo(track.Artist));
        });
    }

    [Test]
    public void DuplicateReference_IsRejectedWithoutChangingExistingTrack()
    {
        using var database = new DataBaseService(_connectionString);
        var track = database.SaveTrack(NewTrack("same"));
        Assert.Throws<InvalidOperationException>(() => database.SaveTrack(NewTrack("same")));
        Assert.That(database.GetTrack("same")!.Id, Is.EqualTo(track.Id));
        Assert.That(database.SaveTrack(NewTrack("other")).Id, Is.GreaterThan(track.Id));
    }

    [Test]
    public void MissingTrack_ReturnsNullOrThrowsForUpdate()
    {
        using var database = new DataBaseService(_connectionString);
        Assert.That(database.GetTrack("missing"), Is.Null);
        Assert.Throws<InvalidOperationException>(() => database.UpdateTrackPlayCount(NewTrack("missing")));
    }

    [Test]
    public void Increment_UsesPersistedCountAndPreservesMetadata()
    {
        using (var first = new DataBaseService(_connectionString))
        using (var second = new DataBaseService(_connectionString))
        {
            var track = NewTrack("count");
            track.SongName = "Keep me";
            first.SaveTrack(track);
            var stale = second.GetTrack("count")!;
            first.UpdateTrackPlayCount(track);
            stale.SongName = "Do not overwrite persisted metadata";
            var updated = second.UpdateTrackPlayCount(stale)!;
            Assert.That(updated.PlayCount, Is.EqualTo(3));
            Assert.That(stale.PlayCount, Is.EqualTo(3));
            Assert.That(updated.SongName, Is.EqualTo("Keep me"));
        }
        using var reopened = new DataBaseService(_connectionString);
        Assert.That(reopened.GetTrack("count")!.PlayCount, Is.EqualTo(3));
    }

    [Test]
    public void ExistingEfDatabase_IsUsedWithoutMigration()
    {
        using (var connection = new SqliteConnection(_connectionString))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            // Schema captured from the existing EF-created database, not from the new service.
            command.CommandText = """
                CREATE TABLE "Tracks" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_Tracks" PRIMARY KEY AUTOINCREMENT,
                    "Reference" TEXT NOT NULL, "FullUrl" TEXT NOT NULL,
                    "FilePath" TEXT NULL, "PlayCount" INTEGER NOT NULL,
                    "SongName" TEXT NULL, "Artist" TEXT NULL);
                INSERT INTO Tracks VALUES (42, 'existing', 'https://example.invalid/old', NULL, 12, 'Old song', NULL);
                """;
            command.ExecuteNonQuery();
        }
        using var database = new DataBaseService(_connectionString);
        var existing = database.GetTrack("existing")!;
        Assert.That(existing.Id, Is.EqualTo(42));
        Assert.That(existing.SongName, Is.EqualTo("Old song"));
        Assert.That(database.UpdateTrackPlayCount(existing)!.PlayCount, Is.EqualTo(13));
        Assert.That(database.SaveTrack(NewTrack("new")).Id, Is.GreaterThan(42));
    }

    private static Track NewTrack(string reference) => new()
    {
        Reference = reference, FullUrl = "https://example.invalid/track"
    };
}
