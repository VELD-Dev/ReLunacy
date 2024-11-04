using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace LibLunacy.Legacy
{
    public class SubStream : Stream
    {
        public override bool CanRead => _base.CanRead;
        public override bool CanSeek => _base.CanSeek;
        public override bool CanWrite => _base.CanWrite;
        public override long Length => _length;

        public override long Position
        {
            get => _base.Position - _basePosition;
            set
            {
                _base.Position = value + _basePosition;
            }
        }
        private long _basePosition;
        private long _position;
        private long _length;

        public Stream _base;

        public SubStream(Stream baseStream, long offset, long length)
        {
            _base = baseStream;
            _basePosition = offset;
            Position = 0;
            _length = length;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Position = _position;
            _base.Write(buffer, offset, count);
        }
        public override int Read(byte[] buffer, int offset, int count)
        {
            Position = _position;
            return _base.Read(buffer, offset, count);
        }
        public override void Flush()
        {
            _base.Flush();
        }
        public override long Seek(long offset, SeekOrigin loc)
        {
            if (loc == SeekOrigin.Begin)
            {
                return _base.Seek(_basePosition + offset, SeekOrigin.Begin);
            }
            else if (loc == SeekOrigin.Current)
            {
                return _base.Seek(offset, SeekOrigin.Current);
            }
            else
            {
                return _base.Seek(_basePosition + _length - offset, SeekOrigin.Begin);
            }
        }
        public override void SetLength(long value) => _base.SetLength(value);
    }
}