// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using Bicep.IO.Abstraction;

namespace Bicep.Cli.IntegrationTests;

/// <summary>
/// Counts how often each file's contents are read, so that a test can assert how much work a run
/// repeated. Reading a source file is the observable trace a compilation leaves, which makes this
/// the way to tell one compilation of a target from several without reaching into the compiler.
/// </summary>
internal sealed class CountingFileExplorer(IFileExplorer inner) : IFileExplorer
{
    private readonly ConcurrentDictionary<string, int> readsByFileName = new(StringComparer.OrdinalIgnoreCase);

    public int GetReadCount(string fileName) => readsByFileName.TryGetValue(fileName, out var count) ? count : 0;

    public IDirectoryHandle GetDirectory(IOUri uri) => inner.GetDirectory(uri);

    public IFileHandle GetFile(IOUri uri) => new CountingFileHandle(inner.GetFile(uri), Record);

    private void Record(IOUri uri) => readsByFileName.AddOrUpdate(uri.GetFileName(), 1, (_, count) => count + 1);

    private sealed class CountingFileHandle(IFileHandle inner, Action<IOUri> record) : IFileHandle
    {
        public IOUri Uri => inner.Uri;

        public bool Exists() => inner.Exists();

        public string ReadAllText()
        {
            record(inner.Uri);

            return inner.ReadAllText();
        }

        public Task<string> ReadAllTextAsync(CancellationToken cancellationToken = default)
        {
            record(inner.Uri);

            return inner.ReadAllTextAsync(cancellationToken);
        }

        public Stream OpenRead()
        {
            record(inner.Uri);

            return inner.OpenRead();
        }

        public bool Equals(IIOHandle? other) => inner.Equals(other);

        public IDirectoryHandle GetParent() => inner.GetParent();

        public IFileHandle EnsureExists() => inner.EnsureExists();

        public Stream OpenWrite() => inner.OpenWrite();

        public void WriteAllText(string text) => inner.WriteAllText(text);

        public Task WriteAllTextAsync(string text, CancellationToken cancellationToken = default) =>
            inner.WriteAllTextAsync(text, cancellationToken);

        public void Delete() => inner.Delete();

        public void MakeExecutable() => inner.MakeExecutable();

        public IFileLock? TryLock() => inner.TryLock();
    }
}
