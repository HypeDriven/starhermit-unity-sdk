using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Starhermit
{
    /// <summary>
    /// Wraps a stream and reports how many bytes have passed through it.
    /// </summary>
    /// <remarks>
    /// Progress is measured at the stream rather than by buffering the payload, which is what keeps a
    /// multi-gigabyte upload's memory flat.
    /// </remarks>
    internal sealed class ProgressStream : Stream
    {
        private readonly Stream _inner;
        private readonly IProgress<StarhermitTransferProgress> _progress;
        private readonly long? _total;
        private readonly bool _isUpload;
        private readonly bool _leaveOpen;
        private long _transferred;
        private long _lastReported;

        internal ProgressStream(
            Stream inner,
            IProgress<StarhermitTransferProgress> progress,
            long? total,
            bool isUpload,
            bool leaveOpen = false)
        {
            _inner = inner;
            _progress = progress;
            _total = total;
            _isUpload = isUpload;
            _leaveOpen = leaveOpen;
        }

        public override bool CanRead => _inner.CanRead;

        public override bool CanSeek => false;

        public override bool CanWrite => _inner.CanWrite;

        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => throw new NotSupportedException();
        }

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = _inner.Read(buffer, offset, count);
            Advance(read);
            return read;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var read = await _inner.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
            Advance(read);
            return read;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _inner.Write(buffer, offset, count);
            Advance(count);
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await _inner.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
            Advance(count);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Report(force: true);
                if (!_leaveOpen) _inner.Dispose();
            }

            base.Dispose(disposing);
        }

        private void Advance(int count)
        {
            if (count <= 0) return;
            _transferred += count;
            Report(force: false);
        }

        private void Report(bool force)
        {
            // Report at most every 64 KB: a callback per 8 KB chunk on a 2 GB upload is a quarter of a
            // million main-thread dispatches nobody asked for.
            if (!force && _transferred - _lastReported < 64 * 1024) return;
            _lastReported = _transferred;
            _progress.Report(new StarhermitTransferProgress(_transferred, _total, _isUpload));
        }
    }
}
