using System.Runtime.InteropServices;
using BopNet.Models;
using BopNet.Services.DataBaseService;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetCord.Gateway.Voice;
using NetCord.Gateway.Voice.Encryption;
using NetCord.Hosting.Services.ApplicationCommands;

namespace BopNet;

internal static partial class SelfTest
{
    public static int Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"bopnet-self-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(directory);
            var connectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(directory, "bot.db"), Pooling = false
            }.ToString();
            using (var database = new DataBaseService(connectionString))
            {
                var track = database.SaveTrack(new Track { Reference = "self-test", FullUrl = "https://example.invalid/" });
                Require(track.Id > 0, "SQLite generated ID");
                database.UpdateTrackPlayCount(track);
            }
            using (var database = new DataBaseService(connectionString))
                Require(database.GetTrack("self-test")?.PlayCount == 2, "SQLite persistence");

            // Construct command metadata without reading configuration or starting Discord.
            var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
            builder.Services.AddApplicationCommands();
            using (var host = builder.Build())
                host.AddApplicationCommandModule<Interactions>();

            using var encoder = new OpusEncoder(VoiceChannels.Stereo, OpusApplication.Audio);
            Span<byte> encoded = stackalloc byte[4096];
            Require(encoder.Encode(new short[960 * 2], 960, encoded) > 0, "Opus encoding");

            using var encryption = new XChaCha20Poly1305RtpSizeEncryption();
            encryption.SetKey(new byte[32]);
            ReadOnlySpan<byte> plaintext = "BopNet native dependency check"u8;
            var datagram = new byte[12 + plaintext.Length + encryption.Expansion];
            datagram[0] = 0x80;
            encryption.Encrypt(plaintext, new RtpPacketWriter(datagram));
            Span<byte> decrypted = stackalloc byte[plaintext.Length];
            encryption.Decrypt(new RtpPacket(datagram), decrypted);
            Require(plaintext.SequenceEqual(decrypted), "libsodium encryption round trip");

            Require(DaveMaxSupportedProtocolVersion() > 0, "libdave protocol support");
            var encryptor = DaveEncryptorCreate();
            Require(encryptor != 0, "libdave encryptor creation");
            DaveEncryptorDestroy(encryptor);

            Console.WriteLine("Self-test passed: SQLite, command registration, Opus, libsodium, libdave.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Self-test failed: {exception}");
            return 1;
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static void Require(bool condition, string operation)
    {
        if (!condition) throw new InvalidOperationException($"Failed: {operation}");
    }

    [LibraryImport("libdave", EntryPoint = "daveMaxSupportedProtocolVersion")]
    private static partial ushort DaveMaxSupportedProtocolVersion();

    [LibraryImport("libdave", EntryPoint = "daveEncryptorCreate")]
    private static partial nint DaveEncryptorCreate();

    [LibraryImport("libdave", EntryPoint = "daveEncryptorDestroy")]
    private static partial void DaveEncryptorDestroy(nint encryptor);
}
