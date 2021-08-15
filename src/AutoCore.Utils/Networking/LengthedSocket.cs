using System;
using System.Buffers;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace AutoCore.Utils.Networking
{
    using Memory;

    public enum SizeType : byte
    {
        None = 0,
        Char = 1,
        Word = 2,
        Dword = 4
    }

    public class LengthedSocket
    {
        public const int ReceiveBufferSize = 2048;

        public delegate void AcceptHandler(LengthedSocket acceptedSocket);
        public delegate void AsyncHandler(SocketAsyncEventArgs args);
        public delegate void ReceiveHandler(NonContiguousMemoryStream dataStream, int length);
        public delegate void DisconnectHandler();

        private NonContiguousMemoryStream BufferStream { get; }
        private static Stack<SocketAsyncEventArgs> _socketAsyncEventArgsPool;
        private static readonly object ArgsInitLock = new();

        public SizeType SizeHeaderLength { get; }
        public bool CountSize { get; }
        public int LengthSize => (int)SizeHeaderLength;
        public Socket Socket { get; }
        public bool Connected => Socket.Connected;
        public IPAddress RemoteAddress => ((IPEndPoint)Socket.RemoteEndPoint).Address;

        public AsyncHandler OnConnect;
        public DisconnectHandler OnDisconnect;
        public AcceptHandler OnAccept;
        public AsyncHandler OnSend;
        public ReceiveHandler OnReceive;
        public AsyncHandler OnError;

        public LengthedSocket(SizeType sizeHeaderLen, bool countSize = true)
           : this(new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp), sizeHeaderLen, countSize)
        {
        }

        public LengthedSocket(Socket s, SizeType sizeHeaderLen, bool countSize)
        {
            Socket = s;
            SizeHeaderLength = sizeHeaderLen;
            CountSize = countSize;
            BufferStream = new();
        }

        public void Bind(EndPoint ep)
        {
            Socket.Bind(ep);
        }

        public void Listen(int backlog)
        {
            Socket.Listen(backlog);
        }

        #region SocketAsyncEventArgs
        public static void InitializeEventArgsPool(int eventArgsPoolCount)
        {
            if (_socketAsyncEventArgsPool != null)
                return;

            lock (ArgsInitLock)
            {
                if (_socketAsyncEventArgsPool != null)
                    return;

                _socketAsyncEventArgsPool = new Stack<SocketAsyncEventArgs>(eventArgsPoolCount);

                for (var i = 0; i < eventArgsPoolCount; ++i)
                {
                    _socketAsyncEventArgsPool.Push(new SocketAsyncEventArgs
                    {
                        UserToken = new ArrayPoolBuffer(null, 0)
                    });
                }
            }
        }

        private SocketAsyncEventArgs SetupEventArgs(SocketAsyncOperation operation)
        {
            SocketAsyncEventArgs args;

            lock (_socketAsyncEventArgsPool)
                args = _socketAsyncEventArgsPool.Count > 0 ? _socketAsyncEventArgsPool.Pop() : null;

            if (args == null)
                throw new OutOfMemoryException("All of the SocketAsyncEventArgs are being used!");

            switch (operation)
            {
                case SocketAsyncOperation.Receive:
                case SocketAsyncOperation.Send:
                    var data = args.UserToken as ArrayPoolBuffer;

                    data.Buffer = ArrayPool<byte>.Shared.Rent(ReceiveBufferSize);
                    data.Length = data.Buffer.Length;

                    args.SetBuffer(data.Buffer, 0, data.Length);
                    break;

                case SocketAsyncOperation.Connect:
                    args.AcceptSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    break;
            }

            args.Completed += OperationCompleted;

            return args;
        }

        private void TeardownEventArgs(SocketAsyncEventArgs args)
        {
            switch (args.LastOperation)
            {
                case SocketAsyncOperation.Receive:
                case SocketAsyncOperation.Send:
                    var data = args.UserToken as ArrayPoolBuffer;

                    data.Buffer = null;
                    data.Length = 0;

                    args.SetBuffer(null, 0, 0);
                    break;

                case SocketAsyncOperation.Connect:
                    args.RemoteEndPoint = null;
                    break;
            }

            args.AcceptSocket = null;
            args.Completed -= OperationCompleted;

            lock (_socketAsyncEventArgsPool)
                _socketAsyncEventArgsPool.Push(args);
        }
        
        private void OperationCompleted(object o, SocketAsyncEventArgs args)
        {
            if (args.SocketError != SocketError.Success || (args.LastOperation == SocketAsyncOperation.Receive && args.BytesTransferred == 0))
            {
                OnError?.Invoke(args);

                TeardownEventArgs(args);

                return;
            }
            
            var data = args.UserToken as ArrayPoolBuffer;

            switch (args.LastOperation)
            {
                case SocketAsyncOperation.Send:
                    if (args.BytesTransferred < data.Length)
                    {
                        OnError?.Invoke(args);
                        return;
                    }
                        
                    OnSend?.Invoke(args);
                    break;

                case SocketAsyncOperation.Receive:
                    data.Length = args.BytesTransferred;

                    BufferStream.AddArrayPoolBuffer(data);

                    if (BufferStream.Length <= LengthSize)
                        break;

                    var length = ReadSize(BufferStream);

                    if (BufferStream.Length < length)
                        break;

                    BufferStream.RemoveBytes(LengthSize);

                    OnReceive?.Invoke(BufferStream, length - LengthSize);
                    break;

                case SocketAsyncOperation.Connect:
                    OnConnect?.Invoke(args);
                    break;

                case SocketAsyncOperation.Accept:
                    OnAccept?.Invoke(new LengthedSocket(args.AcceptSocket, SizeHeaderLength, CountSize));
                    break;
            }

            TeardownEventArgs(args);
        }

        private int ReadSize(NonContiguousMemoryStream dataStream)
        {
            var headerSize = !CountSize ? LengthSize : 0;

            if (SizeHeaderLength == SizeType.Char)
            {
                var data = new byte[1];
                var pos = dataStream.Position;

                dataStream.Read(data, 0, data.Length);

                dataStream.Position = pos;

                return headerSize + data[0];
            }

            if (SizeHeaderLength == SizeType.Word)
            {
                var data = new byte[2];
                var pos = dataStream.Position;

                dataStream.Read(data, 0, data.Length);

                dataStream.Position = pos;

                return headerSize + BitConverter.ToInt16(data, 0);
            }

            if (SizeHeaderLength == SizeType.Dword)
            {
                var data = new byte[4];
                var pos = dataStream.Position;

                dataStream.Read(data, 0, data.Length);

                dataStream.Position = pos;

                return headerSize + BitConverter.ToInt32(data, 0);
            }

            throw new NotImplementedException($"Only 1, 2 and 4 byte headers are supported! {SizeHeaderLength} is not!");
        }
#endregion

        private void SendAsync(SocketAsyncEventArgs args)
        {
            if (!Socket.SendAsync(args))
                OperationCompleted(Socket, args);
        }

        public void Close()
        {
            try
            {
                OnDisconnect?.Invoke();

                Socket.Shutdown(SocketShutdown.Both);
            }
            catch
            {
            }
        }
    }
}
