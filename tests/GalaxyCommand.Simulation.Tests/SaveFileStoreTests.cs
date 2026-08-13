using System.Text.Json;
using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class SaveFileStoreTests : IDisposable
{
    private static readonly SaveJsonLimits Limits =
        new(
            maximumDocumentBytes: 16_384,
            maximumDepth: 16,
            maximumStringLength: 1_024,
            maximumContainerEntries: 128);

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"galaxy-command-save-tests-{Guid.NewGuid():N}");

    [Fact]
    public void CommitCreatesReadablePrimaryWithoutLeavingTemporaryFile()
    {
        var store = CreateStore();

        SaveWriteResult write = store.Commit("campaign-1", Document("first"));
        SaveReadResult read = store.Load("campaign-1", SaveFileSelection.Primary);

        Assert.True(write.IsSuccess);
        Assert.True(read.IsSuccess);
        Assert.Equal("first", read.Document!.Root.GetProperty("value").GetString());
        Assert.Equal(
            ["campaign-1.json"],
            Directory.GetFiles(_directory).Select(path => Path.GetFileName(path)!).ToArray());
    }

    [Fact]
    public void RecommitAtomicallyPublishesNewPrimaryAndRetainsOneExplicitBackup()
    {
        var store = CreateStore();
        Assert.True(store.Commit("campaign-1", Document("first")).IsSuccess);

        SaveWriteResult second = store.Commit("campaign-1", Document("second"));
        SaveReadResult primary = store.Load("campaign-1", SaveFileSelection.Primary);
        SaveReadResult backup = store.Load("campaign-1", SaveFileSelection.Backup);

        Assert.True(second.IsSuccess);
        Assert.Equal("second", primary.Document!.Root.GetProperty("value").GetString());
        Assert.Equal("first", backup.Document!.Root.GetProperty("value").GetString());
        Assert.Equal(
            ["campaign-1.backup.json", "campaign-1.json"],
            Directory.GetFiles(_directory)
                .Select(path => Path.GetFileName(path)!)
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public void ThirdCommitRetainsOnlyImmediatelyPreviousBackup()
    {
        var store = CreateStore();
        Assert.True(store.Commit("campaign-1", Document("first")).IsSuccess);
        Assert.True(store.Commit("campaign-1", Document("second")).IsSuccess);

        SaveWriteResult third = store.Commit("campaign-1", Document("third"));

        Assert.True(third.IsSuccess);
        Assert.Equal(
            "third",
            store.Load("campaign-1", SaveFileSelection.Primary)
                .Document!.Root.GetProperty("value").GetString());
        Assert.Equal(
            "second",
            store.Load("campaign-1", SaveFileSelection.Backup)
                .Document!.Root.GetProperty("value").GetString());
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("nested/save")]
    [InlineData("nested\\save")]
    [InlineData("campaign.json")]
    [InlineData(" white-space")]
    public void CommitRejectsInvalidSlotNamesWithoutTouchingFilesystem(string slotId)
    {
        var store = CreateStore();

        SaveWriteResult result = store.Commit(slotId, Document("value"));

        Assert.False(result.IsSuccess);
        Assert.Equal(SaveFormatFailureKind.InvalidSlot, result.Failure!.Kind);
        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public void LoadReportsMissingFileSeparatelyFromUnreadableStorage()
    {
        var store = CreateStore();

        SaveReadResult result = store.Load("missing", SaveFileSelection.Primary);

        Assert.False(result.IsSuccess);
        Assert.Equal(SaveFormatFailureKind.FileMissing, result.Failure!.Kind);
    }

    [Fact]
    public void CorruptPrimaryIsRejectedWithoutSilentlyLoadingValidBackup()
    {
        var store = CreateStore();
        Assert.True(store.Commit("campaign-1", Document("first")).IsSuccess);
        Assert.True(store.Commit("campaign-1", Document("second")).IsSuccess);
        File.WriteAllText(Path.Combine(_directory, "campaign-1.json"), "not json");

        SaveReadResult primary = store.Load("campaign-1", SaveFileSelection.Primary);
        SaveReadResult backup = store.Load("campaign-1", SaveFileSelection.Backup);

        Assert.False(primary.IsSuccess);
        Assert.Equal(SaveFormatFailureKind.InvalidJson, primary.Failure!.Kind);
        Assert.Equal("first", backup.Document!.Root.GetProperty("value").GetString());
    }

    [Fact]
    public void StoreRejectsPrimarySymbolicLinkWithoutChangingItsTarget()
    {
        Directory.CreateDirectory(_directory);
        string external = Path.Combine(Path.GetTempPath(), $"galaxy-save-target-{Guid.NewGuid():N}");
        File.WriteAllText(external, "outside");
        File.CreateSymbolicLink(Path.Combine(_directory, "campaign-1.json"), external);
        try
        {
            var store = CreateStore();

            SaveWriteResult result = store.Commit("campaign-1", Document("replacement"));

            Assert.False(result.IsSuccess);
            Assert.Equal(SaveFormatFailureKind.StorageAccess, result.Failure!.Kind);
            Assert.Equal("outside", File.ReadAllText(external));
        }
        finally
        {
            File.Delete(external);
        }
    }

    [Theory]
    [InlineData(SaveFailurePoint.ShortWrite)]
    [InlineData(SaveFailurePoint.Flush)]
    [InlineData(SaveFailurePoint.Publish)]
    public void FailedCommitLeavesPriorPrimaryIntactAndCleansItsTemporaryFile(
        SaveFailurePoint failurePoint)
    {
        var store = CreateStore();
        Assert.True(store.Commit("campaign-1", Document("first")).IsSuccess);
        var fileSystem = new FaultInjectingFileSystem(failurePoint);
        var failingStore = CreateStore(fileSystem);

        SaveWriteResult failed = failingStore.Commit("campaign-1", Document("second"));

        Assert.False(failed.IsSuccess);
        Assert.Equal(
            "first",
            store.Load("campaign-1", SaveFileSelection.Primary)
                .Document!.Root.GetProperty("value").GetString());
        Assert.DoesNotContain(
            Directory.GetFiles(_directory),
            path => Path.GetExtension(path) == ".tmp");
    }

    [Fact]
    public void SuccessfulCommitSynchronizesContainingDirectoryAfterPublication()
    {
        var fileSystem = new FaultInjectingFileSystem(SaveFailurePoint.None);
        var store = CreateStore(fileSystem);

        SaveWriteResult result = store.Commit("campaign-1", Document("value"));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, fileSystem.DirectorySynchronizations);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private SaveFileStore CreateStore(ISaveFileSystem? fileSystem = null) =>
        new(
            _directory,
            Limits,
            new SaveMigrationRegistry(
                SaveFormat.CurrentSchemaVersion,
                Array.Empty<ISaveSchemaMigration>()),
            fileSystem ?? new PhysicalSaveFileSystem());

    private static InertSaveDocument Document(string value)
    {
        using JsonDocument parsed = JsonDocument.Parse($$"""
            {
              "format": "galaxy-command-save",
              "schemaVersion": 1,
              "value": "{{value}}"
            }
            """);
        return new InertSaveDocument(1, parsed.RootElement);
    }

    public enum SaveFailurePoint
    {
        None,
        ShortWrite,
        Flush,
        Publish,
    }

    private sealed class FaultInjectingFileSystem : ISaveFileSystem
    {
        private readonly PhysicalSaveFileSystem _inner = new();
        private readonly SaveFailurePoint _failurePoint;

        internal FaultInjectingFileSystem(SaveFailurePoint failurePoint)
        {
            _failurePoint = failurePoint;
        }

        internal int DirectorySynchronizations { get; private set; }

        public void CreateDirectory(string path) => _inner.CreateDirectory(path);

        public bool FileExists(string path) => _inner.FileExists(path);

        public bool IsSymbolicLink(string path) => _inner.IsSymbolicLink(path);

        public ISaveTemporaryFile CreateTemporaryFile(string path) =>
            new FaultInjectingTemporaryFile(
                _inner.CreateTemporaryFile(path),
                _failurePoint);

        public Stream OpenRead(string path) => _inner.OpenRead(path);

        public void ReplaceFile(string source, string destination, string backup)
        {
            if (_failurePoint == SaveFailurePoint.Publish)
            {
                throw new IOException("Injected replacement failure.");
            }

            _inner.ReplaceFile(source, destination, backup);
        }

        public void MoveFile(string source, string destination)
        {
            if (_failurePoint == SaveFailurePoint.Publish)
            {
                throw new IOException("Injected rename failure.");
            }

            _inner.MoveFile(source, destination);
        }

        public void DeleteFile(string path) => _inner.DeleteFile(path);

        public void SynchronizeDirectory(string path)
        {
            DirectorySynchronizations++;
        }
    }

    private sealed class FaultInjectingTemporaryFile : ISaveTemporaryFile
    {
        private readonly ISaveTemporaryFile _inner;
        private readonly SaveFailurePoint _failurePoint;

        internal FaultInjectingTemporaryFile(
            ISaveTemporaryFile inner,
            SaveFailurePoint failurePoint)
        {
            _inner = inner;
            _failurePoint = failurePoint;
        }

        public int Write(ReadOnlySpan<byte> bytes)
        {
            if (_failurePoint == SaveFailurePoint.ShortWrite)
            {
                return bytes.Length - 1;
            }

            return _inner.Write(bytes);
        }

        public void FlushToDisk()
        {
            if (_failurePoint == SaveFailurePoint.Flush)
            {
                throw new IOException("Injected flush failure.");
            }

            _inner.FlushToDisk();
        }

        public void Dispose() => _inner.Dispose();
    }
}
