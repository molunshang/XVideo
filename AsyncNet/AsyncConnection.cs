using System;
using System.Buffers;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace AsyncNet
{
    class AsyncConnection
    {
        private readonly IPAddress _ipAddress;
        private readonly int _port;
        private Socket _socket;
        private int _state;
        private int _connected;
        private MemoryStream _sendBuffer;
        private SocketAsyncEventArgs _asyncSendArgs;
        private SocketAsyncEventArgs _asyncReceArgs;
        private ConcurrentQueue<string> _sendQueue = new ConcurrentQueue<string>();

        public AsyncConnection(IPAddress ipAddress, int port)
        {
            _ipAddress = ipAddress;
            _port = port;
            _asyncSendArgs = new SocketAsyncEventArgs();
            _asyncSendArgs.Completed += SendComplete;
            _asyncReceArgs = new SocketAsyncEventArgs();
            _asyncReceArgs.SetBuffer(new byte[1024 * 4], 0, 1024 * 4);
            _asyncReceArgs.Completed += ReceiveComplete;
            _sendBuffer = new MemoryStream();
        }

        private void SendComplete(object sender, SocketAsyncEventArgs e)
        {
            DoSend(false);
        }

        private void ReceiveComplete(object sender, SocketAsyncEventArgs e)
        {
            BeginReceive(false);
        }

        private void BeginReceive(bool flag)
        {
            if (flag && _socket.ReceiveAsync(_asyncReceArgs))
            {
                return;
            }

            while (_connected == 1)
            {
                if (ProcessReceive())
                {
                    return;
                }
            }
        }

        private void DoSend(bool flag)
        {
            while (_connected == 1)
            {
                if (flag)
                {
                    while (_sendBuffer.Length < 4096)
                    {
                        if (!_sendQueue.TryDequeue(out var msg) && _sendBuffer.Length <= 0)
                        {
                            _state = 0;
                            return;
                        }

                        if (msg == null)
                        {
                            break;
                        }

                        _sendBuffer.Write(Encoding.UTF8.GetBytes(msg));
                    }

                    var buffer = _sendBuffer.GetBuffer();
                    _asyncSendArgs.SetBuffer(buffer, 0, (int) _sendBuffer.Length);
                    _sendBuffer.SetLength(0);

                    if (_socket.SendAsync(_asyncSendArgs))
                    {
                        return;
                    }
                }

                while (ProcessSend())
                {
                    if (_socket.SendAsync(_asyncSendArgs))
                    {
                        return;
                    }
                }

                flag = true;
            }
        }

        private bool ProcessSend()
        {
            var e = _asyncSendArgs;
            if (e.BytesTransferred == 0 || e.SocketError != SocketError.Success)
            {
                Console.WriteLine("the socket has been close !");
                Interlocked.CompareExchange(ref _connected, 0, 1);
                ReConnect();
                return true;
            }

            if (e.BytesTransferred >= e.Count)
            {
                return false;
            }

            e.SetBuffer(e.BytesTransferred, e.Count - e.BytesTransferred);
            return true;
        }

        private bool ProcessReceive()
        {
            if (_asyncReceArgs.BytesTransferred <= 0 || _asyncReceArgs.SocketError != SocketError.Success)
            {
                Console.WriteLine("socket has been closed !");
                Interlocked.CompareExchange(ref _connected, 0, 1);
                ReConnect();
                return true;
            }

//                _receiveBuffer.Write(_asyncReceArgs.Buffer, _asyncReceArgs.Offset, _asyncReceArgs.BytesTransferred);
//                _asyncReceArgs.SetBuffer(0, _asyncReceArgs.Buffer.Length);
//                var buffer = _receiveBuffer.GetBuffer();
//                for (int i = 0, start = 0; i < buffer.Length; i++)
//                {
//                    if (buffer[i] != '\n')
//                        continue;
//                    var resMsg = Encoding.UTF8.GetString(buffer, start, i - start);
//                    Console.WriteLine("receive :" + resMsg);
//                    start = i + 1;
//                }

            int start = 0, count = _asyncReceArgs.Offset + _asyncReceArgs.BytesTransferred;
            for (var i = 0; i < count; i++)
            {
                if (_asyncReceArgs.Buffer[i] != '\n')
                    continue;
                var resMsg = Encoding.UTF8.GetString(_asyncReceArgs.Buffer, start, i - start);
                Console.WriteLine("receive :" + resMsg);
                start = i + 1;
                if (resMsg == "null")
                {
                    Console.WriteLine("over");
                }
            }

            if (start == 0)
            {
                var buffer = new byte[_asyncReceArgs.Buffer.Length * 2];
                Array.Copy(_asyncReceArgs.Buffer, buffer, _asyncReceArgs.BytesTransferred);
                _asyncReceArgs.SetBuffer(buffer, _asyncReceArgs.BytesTransferred,
                    buffer.Length - _asyncReceArgs.BytesTransferred);
            }
            else
            {
                var leave = count - start;
                if (leave > 0)
                {
                    Array.Copy(_asyncReceArgs.Buffer, start, _asyncReceArgs.Buffer, 0, leave);
                    _asyncReceArgs.SetBuffer(leave, _asyncReceArgs.Buffer.Length - leave);
                }
                else
                {
                    _asyncReceArgs.SetBuffer(0, _asyncReceArgs.Buffer.Length);
                }
            }

            return _socket.ReceiveAsync(_asyncReceArgs);
        }

        public void Connect()
        {
            _socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            _socket.Connect(_ipAddress, _port);
            _connected = 1;
            BeginReceive(true);
        }

        private void ReConnect()
        {
            if (_connected == 1)
            {
                return;
            }

            lock (this)
            {
                if (_connected == 1)
                {
                    return;
                }

                Connect();
            }
        }

        public void Send(string msg)
        {
            _sendQueue.Enqueue(msg);
            if (_state != 0 || Interlocked.CompareExchange(ref _state, 1, 0) != 0)
            {
                return;
            }

            ThreadPool.QueueUserWorkItem(obj => { DoSend(true); });
        }
    }
}