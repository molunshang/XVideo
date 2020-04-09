using System;
using System.Linq;

namespace AsyncNet
{
    public class Speed
    {
        private readonly int[] _bucket = new int[2 << 9];
        private int _lastIndex;
        private long _lastMillSecond;

        public Speed()
        {
            _lastMillSecond = CurrentMillisecond();
            _lastIndex = (int) (_lastMillSecond & (_bucket.Length - 1));
        }

        private void ClearBucket(int start, int end)
        {
            for (; start < end; start++)
            {
                _bucket[start] = 0;
            }
        }

        private static long CurrentMillisecond()
        {
            return DateTime.Now.Ticks / 10000;
        }

        public void Add(int size)
        {
            var mills = CurrentMillisecond();
            var index = (int) (mills & (_bucket.Length - 1));
            lock (this)
            {
                if (index == _lastIndex)
                {
                    _bucket[index] += size;
                    _lastIndex = index;
                    return;
                }

                if (index < _lastIndex)
                {
                    ClearBucket(0, index);
                    ClearBucket(_lastIndex + 1, _bucket.Length);
                }
                else if (index > _lastIndex + 1)
                {
                    ClearBucket(_lastIndex + 1, index);
                }

                _bucket[index] = size;
                _lastIndex = index;
                _lastMillSecond = mills;
            }
        }

        public int AddAndGet(int size)
        {
            lock (this)
            {
                Add(size);
                return Get();
            }
        }

        public int Get()
        {
            var mills = CurrentMillisecond();
            lock (this)
            {
                var clearLength = (int) (mills - _lastMillSecond);
                if (clearLength == 0)
                {
                    return _bucket.Sum();
                }

                if (clearLength >= _bucket.Length)
                {
                    return 0;
                }

                int result = 0, clearEnd = _lastIndex + clearLength;
                if (clearEnd < _bucket.Length)
                {
                    for (var i = 0; i < _bucket.Length; i++)
                    {
                        if (_lastIndex >= i)
                        {
                            result += _bucket[i];
                        }
                        else
                        {
                            _bucket[i] = 0;
                        }
                    }
                }
                else
                {
                    var clearStart = clearEnd - _bucket.Length;
                    for (var i = 0; i < _bucket.Length; i++)
                    {
                        if (_lastIndex >= i && clearStart < i)
                        {
                            result += _bucket[i];
                        }
                        else
                        {
                            _bucket[i] = 0;
                        }
                    }
                }

                return result;
            }
        }
    }
}