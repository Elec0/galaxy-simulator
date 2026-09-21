namespace GalaxyCommand.Simulation;

internal enum SaveFileSelection
{
    Primary,
    Backup,
}

internal interface ISaveTemporaryFile : IDisposable
{
    int Write(ReadOnlySpan<byte> bytes);

    void FlushToDisk();
}

internal interface ISaveFileSystem
{
    void CreateDirectory(string path);

    bool FileExists(string path);

    bool IsSymbolicLink(string path);

    ISaveTemporaryFile CreateTemporaryFile(string path);

    Stream OpenRead(string path);

    void ReplaceFile(string source, string destination, string backup);

    void MoveFile(string source, string destination);

    void DeleteFile(string path);

    void SynchronizeDirectory(string path);
}

internal sealed class PhysicalSaveFileSystem : ISaveFileSystem
{
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public bool FileExists(string path) => File.Exists(path);

    /// <summary>
    /// Treats absent final names as ordinary non-links while allowing access
    /// failures to reach the store's typed storage-error boundary.
    /// </summary>
    public bool IsSymbolicLink(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    public ISaveTemporaryFile CreateTemporaryFile(string path) =>
        new PhysicalSaveTemporaryFile(path);

    public Stream OpenRead(string path) => new FileStream(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read);

    public void ReplaceFile(string source, string destination, string backup) =>
        File.Replace(source, destination, backup, ignoreMetadataErrors: true);

    public void MoveFile(string source, string destination) =>
        File.Move(source, destination);

    public void DeleteFile(string path) => File.Delete(path);

    public void SynchronizeDirectory(string path) =>
        DirectoryDurability.Synchronize(path);

    private sealed class PhysicalSaveTemporaryFile : ISaveTemporaryFile
    {
        private readonly FileStream _stream;

        internal PhysicalSaveTemporaryFile(string path)
        {
            _stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough);
        }

        public int Write(ReadOnlySpan<byte> bytes)
        {
            _stream.Write(bytes);
            return bytes.Length;
        }

        public void FlushToDisk() => _stream.Flush(flushToDisk: true);

        public void Dispose() => _stream.Dispose();
    }
}

internal sealed class SaveFileStore
{
    private readonly string _directory;
    private readonly SaveJsonLimits _limits;
    private readonly SaveMigrationRegistry _migrations;
    private readonly ISaveFileSystem _fileSystem;

    internal SaveFileStore(
        string directory,
        SaveJsonLimits limits,
        SaveMigrationRegistry migrations,
        ISaveFileSystem? fileSystem = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(migrations);
        _directory = Path.GetFullPath(directory);
        _limits = limits;
        _migrations = migrations;
        _fileSystem = fileSystem ?? new PhysicalSaveFileSystem();
    }

    /// <summary>
    /// Encodes and durably flushes a complete document before atomically
    /// publishing it as the selected slot's primary save.
    /// </summary>
    internal SaveWriteResult Commit(string slotId, InertSaveDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        SaveFormatFailure? slotFailure = ValidateSlot(slotId);
        if (slotFailure is not null)
        {
            return SaveWriteResult.Rejected(slotFailure);
        }

        using var encoded = new MemoryStream();
        SaveWriteResult encoding = SaveJsonCodec.Write(encoded, document, _limits);
        if (!encoding.IsSuccess)
        {
            return encoding;
        }

        string destination = Path.Combine(_directory, $"{slotId}.json");
        string backup = Path.Combine(_directory, $"{slotId}.backup.json");
        string temporary = Path.Combine(
            _directory,
            $".{slotId}.{Guid.NewGuid():N}.tmp");
        try
        {
            _fileSystem.CreateDirectory(_directory);
            if (_fileSystem.IsSymbolicLink(destination)
                || _fileSystem.IsSymbolicLink(backup))
            {
                return SaveWriteResult.Rejected(StorageFailure());
            }

            using (ISaveTemporaryFile output = _fileSystem.CreateTemporaryFile(temporary))
            {
                int written = output.Write(
                    encoded.GetBuffer().AsSpan(0, encoding.BytesWritten));
                if (written != encoding.BytesWritten)
                {
                    return SaveWriteResult.Rejected(StorageFailure());
                }

                output.FlushToDisk();
            }

            if (_fileSystem.FileExists(destination))
            {
                _fileSystem.ReplaceFile(temporary, destination, backup);
            }
            else
            {
                _fileSystem.MoveFile(temporary, destination);
            }

            _fileSystem.SynchronizeDirectory(_directory);
            return encoding;
        }
        catch (IOException)
        {
            return SaveWriteResult.Rejected(StorageFailure());
        }
        catch (UnauthorizedAccessException)
        {
            return SaveWriteResult.Rejected(StorageFailure());
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    /// <summary>
    /// Reads only the explicitly selected primary or backup file and never
    /// substitutes another candidate after validation fails.
    /// </summary>
    internal SaveReadResult Load(string slotId, SaveFileSelection selection)
    {
        SaveFormatFailure? slotFailure = ValidateSlot(slotId);
        if (slotFailure is not null)
        {
            return SaveReadResult.Rejected(slotFailure);
        }

        string suffix = selection == SaveFileSelection.Primary
            ? ".json"
            : ".backup.json";
        string path = Path.Combine(_directory, $"{slotId}{suffix}");
        try
        {
            if (_fileSystem.IsSymbolicLink(path))
            {
                return SaveReadResult.Rejected(StorageFailure());
            }

            if (!_fileSystem.FileExists(path))
            {
                return SaveReadResult.Rejected(MissingFailure());
            }

            using Stream source = _fileSystem.OpenRead(path);
            return SaveJsonCodec.Read(source, _limits, _migrations);
        }
        catch (FileNotFoundException)
        {
            return SaveReadResult.Rejected(MissingFailure());
        }
        catch (DirectoryNotFoundException)
        {
            return SaveReadResult.Rejected(MissingFailure());
        }
        catch (IOException)
        {
            return SaveReadResult.Rejected(StorageFailure());
        }
        catch (UnauthorizedAccessException)
        {
            return SaveReadResult.Rejected(StorageFailure());
        }
    }

    /// <summary>
    /// Keeps slot identifiers as application data by accepting only a bounded
    /// portable name alphabet, never a path, extension, or whitespace variant.
    /// </summary>
    private static SaveFormatFailure? ValidateSlot(string? slotId)
    {
        if (string.IsNullOrEmpty(slotId)
            || slotId.Length > 64
            || !char.IsAsciiLetterOrDigit(slotId[0])
            || !char.IsAsciiLetterOrDigit(slotId[^1])
            || slotId.Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character != '-'
                && character != '_'))
        {
            return new SaveFormatFailure(
                SaveFormatFailureKind.InvalidSlot,
                "$.slotId",
                "The save slot identifier is invalid.");
        }

        return null;
    }

    private static SaveFormatFailure MissingFailure() =>
        new(
            SaveFormatFailureKind.FileMissing,
            "$",
            "The selected save file does not exist.");

    private static SaveFormatFailure StorageFailure() =>
        new(
            SaveFormatFailureKind.StorageAccess,
            "$",
            "The save file could not be accessed.");

    /// <summary>
    /// Removes only this commit's uniquely named temporary file while
    /// preserving the primary failure when best-effort cleanup also fails.
    /// </summary>
    private void TryDelete(string path)
    {
        try
        {
            _fileSystem.DeleteFile(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
