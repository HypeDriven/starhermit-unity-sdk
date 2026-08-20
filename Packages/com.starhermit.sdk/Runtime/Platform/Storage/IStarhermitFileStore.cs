using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Starhermit
{
    /// <summary>
    /// File access for downloads and cloud saves, abstracted because WebGL and some consoles have no
    /// ordinary filesystem.
    /// </summary>
    /// <remarks>
    /// Writes go through <see cref="IStarhermitAtomicWrite"/>: content lands in a temporary file and is
    /// promoted only after the transfer succeeds and any checksum matches. A download interrupted at
    /// 90% must not leave a half file where the game expects a whole one.
    /// </remarks>
    public interface IStarhermitFileStore
    {
        /// <summary>Opens a file for reading.</summary>
        /// <param name="path">Path within the store's root.</param>
        /// <param name="cancellationToken">Cancels the open.</param>
        /// <returns>A readable stream the caller disposes.</returns>
        Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default);

        /// <summary>Begins an atomic write.</summary>
        /// <param name="path">Final path within the store's root.</param>
        /// <param name="cancellationToken">Cancels the open.</param>
        /// <returns>A handle whose commit publishes the file.</returns>
        Task<IStarhermitAtomicWrite> BeginWriteAsync(string path, CancellationToken cancellationToken = default);

        /// <summary>Tests whether a file exists.</summary>
        /// <param name="path">Path within the store's root.</param>
        /// <param name="cancellationToken">Cancels the check.</param>
        /// <returns>True when the file exists.</returns>
        Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default);

        /// <summary>Deletes a file if it exists.</summary>
        /// <param name="path">Path within the store's root.</param>
        /// <param name="cancellationToken">Cancels the delete.</param>
        /// <returns>A task that completes once the file is gone.</returns>
        Task DeleteAsync(string path, CancellationToken cancellationToken = default);
    }

    /// <summary>A write that becomes visible only when it is committed.</summary>
    public interface IStarhermitAtomicWrite : IDisposable
    {
        /// <summary>The stream to write the content to.</summary>
        Stream Stream { get; }

        /// <summary>Publishes the written content at the final path, replacing anything there.</summary>
        /// <param name="cancellationToken">Cancels the promotion.</param>
        /// <returns>A task that completes once the file is in place.</returns>
        Task CommitAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// A file store rooted at a directory on an ordinary filesystem.
    /// </summary>
    /// <remarks>
    /// Every path is resolved inside the root and rejected if it escapes: a server-supplied name must
    /// never be able to write outside the folder the application chose.
    /// </remarks>
    public sealed class SystemFileStore : IStarhermitFileStore
    {
        private readonly string _root;

        /// <summary>Creates a store rooted at a directory, creating it if needed.</summary>
        /// <param name="rootDirectory">Absolute path of the root directory.</param>
        public SystemFileStore(string rootDirectory)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory)) throw new ArgumentException("A root directory is required.", nameof(rootDirectory));
            _root = Path.GetFullPath(rootDirectory);
            Directory.CreateDirectory(_root);
        }

        /// <summary>The root directory every path is resolved against.</summary>
        public string RootDirectory => _root;

        /// <inheritdoc />
        public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default)
        {
            var full = Resolve(path);
            Stream stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
            return Task.FromResult(stream);
        }

        /// <inheritdoc />
        public Task<IStarhermitAtomicWrite> BeginWriteAsync(string path, CancellationToken cancellationToken = default)
        {
            var full = Resolve(path);
            var directory = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory!);
            IStarhermitAtomicWrite write = new AtomicWrite(full);
            return Task.FromResult(write);
        }

        /// <inheritdoc />
        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(File.Exists(Resolve(path)));

        /// <inheritdoc />
        public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
        {
            var full = Resolve(path);
            if (File.Exists(full)) File.Delete(full);
            return Task.CompletedTask;
        }

        /// <summary>Resolves a relative path inside the root, refusing anything that escapes it.</summary>
        /// <param name="path">Path within the store's root.</param>
        /// <returns>The absolute path.</returns>
        public string Resolve(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            var combined = Path.GetFullPath(Path.Combine(_root, path));
            var rootWithSeparator = _root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? _root
                : _root + Path.DirectorySeparatorChar;
            if (!combined.StartsWith(rootWithSeparator, StringComparison.Ordinal) &&
                !string.Equals(combined, _root, StringComparison.Ordinal))
            {
                throw new StarhermitPathEscapeException(path);
            }

            return combined;
        }

        private sealed class AtomicWrite : IStarhermitAtomicWrite
        {
            private readonly string _finalPath;
            private readonly string _temporaryPath;
            private bool _committed;
            private bool _disposed;

            internal AtomicWrite(string finalPath)
            {
                _finalPath = finalPath;
                _temporaryPath = finalPath + ".starhermit-partial";
                Stream = new FileStream(_temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);
            }

            public Stream Stream { get; }

            public async Task CommitAsync(CancellationToken cancellationToken = default)
            {
                await Stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                Stream.Dispose();
                if (File.Exists(_finalPath)) File.Delete(_finalPath);
                File.Move(_temporaryPath, _finalPath);
                _committed = true;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                Stream.Dispose();
                // An abandoned write leaves nothing behind: a stray .starhermit-partial would be
                // mistaken for a resumable download by the next run.
                if (!_committed && File.Exists(_temporaryPath))
                {
                    try
                    {
                        File.Delete(_temporaryPath);
                    }
                    catch (IOException)
                    {
                    }
                }
            }
        }
    }

    /// <summary>A path would have written outside the file store's root.</summary>
    public sealed class StarhermitPathEscapeException : StarhermitException
    {
        /// <summary>Creates the exception.</summary>
        /// <param name="path">The offending path.</param>
        public StarhermitPathEscapeException(string path)
            : base($"The path '{path}' resolves outside the file store's root directory.")
        {
        }
    }
}
