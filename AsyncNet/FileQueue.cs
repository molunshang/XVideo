using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AsyncNet
{
    public class FileQueue<T> : IDisposable
    {
        private const int MaxFilesize = 1024 * 1024 * 64;
        private readonly BinaryWriter writer;
        private readonly BinaryReader reader;
        private readonly Func<T, byte[]> encoder;
        private readonly Func<byte[], T> decoder;
        private long _readIndex;
        private long _writeIndex;
        private long _count;

        public FileQueue(string filePath, Func<T, byte[]> encoder, Func<byte[], T> decoder)
        {
            this.encoder = encoder;
            this.decoder = decoder;
            var fileStream = new FileStream(filePath, FileMode.OpenOrCreate);
            writer = new BinaryWriter(fileStream);
            reader = new BinaryReader(fileStream);
            if (fileStream.Length <= 0)
            {
                _readIndex = _writeIndex = 24;
                writer.Write(_count);
                writer.Write(_readIndex);
                writer.Write(_writeIndex);
            }
            else
            {
                _count = reader.ReadInt64();
                _readIndex = reader.ReadInt64();
                _writeIndex = reader.ReadInt64();
            }
        }

        private void SetIndex(long wIndex, long rIndex)
        {
            writer.BaseStream.Seek(0, SeekOrigin.Begin);
            writer.Write(_count);
            writer.Write(rIndex);
            writer.Write(wIndex);
            this._writeIndex = wIndex;
            this._readIndex = rIndex;
            writer.Flush();
        }

        public long Count
        {
            get
            {
                lock (this)
                {
                    return _count;
                }
            }
        }

        public long Position
        {
            get
            {
                lock (this)
                {
                    return writer.BaseStream.Position;
                }
            }
        }

        public bool CanRead
        {
            get
            {
                lock (this)
                {
                    return _readIndex < _writeIndex;
                }
            }
        }

        public bool CanWrite
        {
            get
            {
                lock (this)
                {
                    return _writeIndex < MaxFilesize;
                }
            }
        }

        public bool Enqueue(T item)
        {
            if (!CanWrite)
            {
                return false;
            }

            var byteArray = encoder(item);
            lock (this)
            {
                _count++;
                writer.BaseStream.Seek(_writeIndex, SeekOrigin.Begin);
                writer.Write(byteArray.Length);
                writer.Write(byteArray);
                SetIndex(Position, _readIndex);
            }

            return true;
        }

        public bool Enqueue(IEnumerable<T> items)
        {
            lock (this)
            {
                if (!CanWrite)
                {
                    return false;
                }

                writer.BaseStream.Seek(_writeIndex, SeekOrigin.Begin);
                foreach (var item in items)
                {
                    var byteArray = encoder(item);
                    _count++;
                    writer.Write(byteArray.Length);
                    writer.Write(byteArray);
                }

                SetIndex(Position, _readIndex);
            }

            return true;
        }

        public IList<T> Dequeue(int num)
        {
            lock (this)
            {
                if (_count <= 0)
                    return null;
                reader.BaseStream.Seek(_readIndex, SeekOrigin.Begin);
                var items = new List<T>();
                for (int i = 0; i < num && _count > 0; i++)
                {
                    _count--;
                    var length = reader.ReadInt32();
                    var byteArray =new byte[length];
                    var index = 0;
                    while (index < length)
                    {
                        index += reader.Read(byteArray, index, length - index);
                    }
                    items.Add(decoder(byteArray));
                }

                SetIndex(_writeIndex, Position);
                return items;
            }
        }


        public void Dispose()
        {
            writer.Dispose();
            reader.Dispose();
        }
    }
}