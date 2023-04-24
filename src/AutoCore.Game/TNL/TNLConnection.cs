using System.Buffers;

using TNL.Data;
using TNL.Entities;
using TNL.Structures;
using TNL.Types;
using TNL.Utils;

namespace AutoCore.Game.TNL;

using AutoCore.Database.Char.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Extensions;
using AutoCore.Game.Packets;
using AutoCore.Utils;

public partial class TNLConnection : GhostConnection
{
    private static NetClassRepInstance<TNLConnection> _dynClassRep;
#pragma warning disable IDE0052 // Remove unread private members
    private static NetConnectionRep _connRep;
#pragma warning restore IDE0052 // Remove unread private members

    private uint _key;
    //private uint _oneTimeKey;
    private long _playerCOID;
    private ushort _fragmentCounter;

    public Account Account { get; set; }
    //public Character CurrentCharacter { get; set; }

    private readonly SFragmentData _fragmentGuaranteed;
    private readonly SFragmentData _fragmentNonGuaranteed;
    private readonly SFragmentData _fragmentGuaranteedOrdered;

    public new static void RegisterNetClassReps()
    {
        ImplementNetConnection(out _dynClassRep, out _connRep, true);

        NetEvent.ImplementNetEvent(out RPCMsgGuaranteed.DynClassRep,                  "RPC_TNLConnection_rpcMsgGuaranteed",                  NetClassMask.NetClassGroupGameMask, 0);
        NetEvent.ImplementNetEvent(out RPCMsgGuaranteedOrdered.DynClassRep,           "RPC_TNLConnection_rpcMsgGuaranteedOrdered",           NetClassMask.NetClassGroupGameMask, 0);
        NetEvent.ImplementNetEvent(out RPCMsgNonGuaranteed.DynClassRep,               "RPC_TNLConnection_rpcMsgNonGuaranteed",               NetClassMask.NetClassGroupGameMask, 0);
        NetEvent.ImplementNetEvent(out RPCMsgGuaranteedFragmented.DynClassRep,        "RPC_TNLConnection_rpcMsgGuaranteedFragmented",        NetClassMask.NetClassGroupGameMask, 0);
        NetEvent.ImplementNetEvent(out RPCMsgGuaranteedOrderedFragmented.DynClassRep, "RPC_TNLConnection_rpcMsgGuaranteedOrderedFragmented", NetClassMask.NetClassGroupGameMask, 0);
        NetEvent.ImplementNetEvent(out RPCMsgNonGuaranteedFragmented.DynClassRep,     "RPC_TNLConnection_rpcMsgNonGuaranteedFragmented",     NetClassMask.NetClassGroupGameMask, 0);
    }

    public TNLConnection()
    {
        _key = 0U;
        //_oneTimeKey = 0U;
        _playerCOID = 0L;
        _fragmentCounter = 1;

        SetFixedRateParameters(50, 50, 40000, 40000);
        SetPingTimeouts(7000, 6);

        _fragmentGuaranteed = new SFragmentData();
        _fragmentNonGuaranteed = new SFragmentData();
        _fragmentGuaranteedOrdered = new SFragmentData();
    }

    ~TNLConnection()
    {
        DeleteLocalGhosts();

        //CharacterManager.LogoutCharacter(this);

        Logger.WriteLog(LogType.Network, "Client ({0} | {1}) disconnected", Account.Id, Account.Name);
    }

    public void SendGamePacket(BasePacket packet, RPCGuaranteeType type = RPCGuaranteeType.RPCGuaranteedOrdered)
    {
        Logger.WriteLog(LogType.Network, "Outgoing Packet: {0}", packet.Opcode);

        var dest = ArrayPool<byte>.Shared.Rent(0x4000);

        int packetLength;

        using (var writer = new BinaryWriter(new MemoryStream(dest)))
        {
            writer.Write(packet.Opcode);

            packet.Write(writer);

            packetLength = (int)writer.BaseStream.Position;
        }

        var arr = new byte[packetLength];
        Buffer.BlockCopy(dest, 0, arr, 0, packetLength);

        ArrayPool<byte>.Shared.Return(dest);

        /*using (var sw = new StreamWriter("sent.txt", true, Encoding.UTF8))
        {
            sw.WriteLine(BitConverter.ToString(arr));
            sw.WriteLine();
        }*/

        var arrLength = (uint)packetLength;
        if (arrLength > 1400U)
        {
            ++_fragmentCounter;

            var doneSize = 0U;
            var count = (ushort)Math.Ceiling(arrLength / 220.0);
            for (ushort i = 0; i < count; ++i)
            {
                var buffSize = 220U;
                if (buffSize >= arrLength - doneSize)
                    buffSize = arrLength - doneSize;

                var tempBuff = new byte[buffSize];

                Array.Copy(arr, i * 220, tempBuff, 0, buffSize);

                var stream = new ByteBuffer(tempBuff, buffSize);

                doneSize += buffSize;

                switch (type)
                {
                    case RPCGuaranteeType.RPCGuaranteed:
                        rpcMsgGuaranteedFragmented((uint)packet.Opcode, _fragmentCounter, i, count, stream);
                        break;

                    case RPCGuaranteeType.RPCGuaranteedOrdered:
                        rpcMsgGuaranteedOrderedFragmented((uint)packet.Opcode, _fragmentCounter, i, count, stream);
                        break;

                    case RPCGuaranteeType.RPCUnguaranteed:
                        rpcMsgNonGuaranteedFragmented((uint)packet.Opcode, _fragmentCounter, i, count, stream);
                        break;
                }
            }
        }
        else
        {
            var stream = new ByteBuffer(arr, arrLength);

            switch (type)
            {
                case RPCGuaranteeType.RPCGuaranteed:
                    rpcMsgGuaranteed((uint)packet.Opcode, stream);
                    break;

                case RPCGuaranteeType.RPCGuaranteedOrdered:
                    rpcMsgGuaranteedOrdered((uint)packet.Opcode, stream);
                    break;

                case RPCGuaranteeType.RPCUnguaranteed:
                    rpcMsgNonGuaranteed((uint)packet.Opcode, stream);
                    break;
            }
        }
    }
    #region Handler

    private void HandlePacket(ByteBuffer buffer)
    {
        var reader = new BinaryReader(new MemoryStream(buffer.GetBuffer()));
        var gameOpcode = reader.ReadGameOpcode();

        Logger.WriteLog(LogType.Network, "Incoming Packet: {0}", gameOpcode);

        try
        {
            switch (gameOpcode)
            {
                // Global
                case GameOpcode.LoginRequest:
                    HandleLoginRequestPacket(reader);
                    break;

                case GameOpcode.LoginNewCharacter:
                    HandleNewCharacterPacket(reader);
                    break;

                case GameOpcode.LoginDeleteCharacter:
                    HandleDeleteCharacterPacket(reader);
                    break;

                case GameOpcode.News:
                    HandleNews(reader);
                    break;

                case GameOpcode.Login:
                    HandleGlobalLoginPacket(reader);
                    break;

                case GameOpcode.Disconnect:
                    //HandleDisconnect(reader);
                    break;

                case GameOpcode.Chat:
                    //ChatManager.HandleChat(this, reader);
                    break;

                case GameOpcode.GetFriends:
                    //SocialManager.GetFriends(this);
                    break;

                case GameOpcode.GetEnemies:
                    //SocialManager.GetEnemies(this);
                    break;

                case GameOpcode.GetIgnored:
                    //SocialManager.GetIgnored(this);
                    break;

                case GameOpcode.AddFriend:
                    //SocialManager.AddEntry(this, reader, SocialType.Friend);
                    break;

                case GameOpcode.AddEnemy:
                    //SocialManager.AddEntry(this, reader, SocialType.Enemy);
                    break;

                case GameOpcode.AddIgnore:
                    //SocialManager.AddEntry(this, reader, SocialType.Ignore);
                    break;

                case GameOpcode.RemoveFriend:
                    //SocialManager.RemoveEntry(this, reader.ReadPadding(4).ReadLong(), SocialType.Friend);
                    break;

                case GameOpcode.RemoveEnemy:
                    //SocialManager.RemoveEntry(this, reader.ReadPadding(4).ReadLong(), SocialType.Enemy);
                    break;

                case GameOpcode.RemoveIgnore:
                    //SocialManager.RemoveEntry(this, reader.ReadPadding(4).ReadLong(), SocialType.Ignore);
                    break;

                case GameOpcode.RequestClanInfo:
                    //ClanManager.RequestInfo(this);
                    break;

                case GameOpcode.ConvoyMissionsRequest:
                    //ConvoyManager.MissionsRequest(this);
                    break;

                // Sector
                case GameOpcode.TransferFromGlobal:
                    HandleTransferFromGlobal(reader);
                    break;

                case GameOpcode.TransferFromGlobalStage2:
                    HandleTransferFromGlobalStage2(reader);
                    break;

                case GameOpcode.TransferFromGlobalStage3:
                    HandleTransferFromGlobalStage3(reader);
                    break;

                case GameOpcode.UpdateFirstTimeFlagsRequest:
                    //HandleUpdateFirstTimeFlagsRequest(reader);
                    break;

                case GameOpcode.Broadcast:
                    //ChatManager.HandleBroadcast(this, reader);
                    break;

                default:
                    Logger.WriteLog(LogType.Error, "Unhandled Opcode: {0}", gameOpcode);
                    break;
            }
        }
        catch (Exception e)
        {
            Logger.WriteLog(LogType.Error, "Caught exception while handling packets!");
            Logger.WriteLog(LogType.Error, "Exception: {0}", e);
        }
    }
    #endregion

    #region RPC Calls
#pragma warning disable IDE0051 // Remove unused private members
#pragma warning disable IDE1006 // Naming Styles
    public void rpcMsgGuaranteed(uint type, ByteBuffer data)
    #region rpcMsgGuaranteed
    {
        var rpcEvent = new RPCMsgGuaranteed();
        rpcEvent.Functor.Set(new object[] { type, data });

        PostNetEvent(rpcEvent);
    }

    private void rpcMsgGuaranteed_remote(uint _, ByteBuffer data)
    #endregion
    {
        HandlePacket(data);
    }

    public void rpcMsgGuaranteedOrdered(uint type, ByteBuffer data)
    #region rpcMsgGuaranteedOrdered
    {
        var rpcEvent = new RPCMsgGuaranteedOrdered();
        rpcEvent.Functor.Set(new object[] { type, data });

        PostNetEvent(rpcEvent);
    }

    private void rpcMsgGuaranteedOrdered_remote(uint _, ByteBuffer data)
    #endregion
    {
        HandlePacket(data);
    }

    public void rpcMsgNonGuaranteed(uint type, ByteBuffer data)
    #region rpcMsgNonGuaranteed
    {
        var rpcEvent = new RPCMsgNonGuaranteed();
        rpcEvent.Functor.Set(new object[] { type, data });

        PostNetEvent(rpcEvent);
    }

    private void rpcMsgNonGuaranteed_remote(uint _, ByteBuffer data)
    #endregion
    {
        HandlePacket(data);
    }

    public void rpcMsgGuaranteedFragmented(uint type, ushort fragment, ushort fragmentId, ushort fragmentCount, ByteBuffer data)
    #region rpcMsgGuaranteedFragmented
    {
        var rpcEvent = new RPCMsgGuaranteedFragmented();
        rpcEvent.Functor.Set(new object[] { type, fragment, fragmentId, fragmentCount, data });

        PostNetEvent(rpcEvent);
    }

    private void rpcMsgGuaranteedFragmented_remote(uint type, ushort fragment, ushort fragmentId, ushort fragmentCount, ByteBuffer data)
    #endregion
    {
        Console.WriteLine("MsgGuaranteedFragmented | Type: {0} | Fragment: {1} | FragmentId: {2} | FragmentCount: {3}", type, fragment, fragmentId, fragmentCount);

        ProcessFragment(data, _fragmentGuaranteed, type, fragment, fragmentId, fragmentCount);
    }

    public void rpcMsgGuaranteedOrderedFragmented(uint type, ushort fragment, ushort fragmentId, ushort fragmentCount, ByteBuffer data)
    #region rpcMsgGuaranteedOrderedFragmented
    {
        var rpcEvent = new RPCMsgGuaranteedOrderedFragmented();
        rpcEvent.Functor.Set(new object[] { type, fragment, fragmentId, fragmentCount, data });

        PostNetEvent(rpcEvent);
    }

    private void rpcMsgGuaranteedOrderedFragmented_remote(uint type, ushort fragment, ushort fragmentId, ushort fragmentCount, ByteBuffer data)
    #endregion
    {
        Console.WriteLine("MsgGuaranteedOrderedFragmented | Type: {0} | Fragment: {1} | FragmentId: {2} | FragmentCount: {3}", type, fragment, fragmentId, fragmentCount);

        ProcessFragment(data, _fragmentGuaranteedOrdered, type, fragment, fragmentId, fragmentCount);
    }

    public void rpcMsgNonGuaranteedFragmented(uint type, ushort fragment, ushort fragmentId, ushort fragmentCount, ByteBuffer data)
    #region rpcMsgNonGuaranteedFragmented
    {
        var rpcEvent = new RPCMsgNonGuaranteedFragmented();
        rpcEvent.Functor.Set(new object[] { type, fragment, fragmentId, fragmentCount, data });

        PostNetEvent(rpcEvent);
    }

    private void rpcMsgNonGuaranteedFragmented_remote(uint type, ushort fragment, ushort fragmentId, ushort fragmentCount, ByteBuffer data)
    #endregion
    {
        Console.WriteLine("MsgNonGuaranteedFragmented | Type: {0} | Fragment: {1} | FragmentId: {2} | FragmentCount: {3}", type, fragment, fragmentId, fragmentCount);

        ProcessFragment(data, _fragmentNonGuaranteed, type, fragment, fragmentId, fragmentCount);
    }
#pragma warning restore IDE1006 // Naming Styles
#pragma warning restore IDE0051 // Remove unused private members
    #endregion RPC Calls

    public override NetClassRep GetClassRep()
    {
        return _dynClassRep;
    }

    public void SetPlayerCOID(long connId)
    {
        _playerCOID = connId;
    }

    public long GetPlayerCOID()
    {
        return _playerCOID;
    }

    public override NetClassGroup GetNetClassGroup()
    {
        return NetClassGroup.NetClassGroupGame;
    }

    public override void PrepareWritePacket()
    {
        
    }
    
    public void DoScoping()
    {
        base.PrepareWritePacket();
    }

    public void GetFixedRateParameters(out uint minPacketSendPeriod, out uint minPacketRecvPeriod, out uint maxSendBandwidth, out uint maxRecvBandwidth)
    {
        minPacketSendPeriod = LocalRate.MinPacketSendPeriod;
        minPacketRecvPeriod = LocalRate.MinPacketRecvPeriod;
        maxSendBandwidth    = LocalRate.MaxSendBandwidth;
        maxRecvBandwidth    = LocalRate.MaxRecvBandwidth;
    }

    public override void WriteConnectRequest(BitStream stream)
    {
        base.WriteConnectRequest(stream);

        if (Interface is not TNLInterface tInterface)
            return;

        stream.Write(tInterface.Version);
        stream.Write(_key);
        stream.Write(_playerCOID);
    }

    public override bool ReadConnectRequest(BitStream stream, ref string errorString)
    {
        if (!base.ReadConnectRequest(stream, ref errorString))
            return false;

        if (Interface is not TNLInterface tInterface)
            return false;

        if (!stream.Read(out int version) || version != tInterface.Version)
        {
            errorString = "Incorrect Version";
            return false;
        }

        if (!stream.Read(out _key))
        {
            errorString = "Unknown Key";
            return false;
        }

        if (!stream.Read(out _playerCOID))
        {
            errorString = "Unknown player ID";
            return false;
        }

        return true;
    }

    public override void OnConnectionEstablished()
    {
        if (Interface is TNLInterface tInterface && !tInterface.Adaptive)
        {
            SetGhostTo(false);
            SetGhostFrom(true);
            ActivateGhosting();
        }

        SetIsAdaptive();
        SetIsConnectionToClient();

        Logger.WriteLog(LogType.Network, "Client ({1}) connected from {0}", GetNetAddressString(), _playerCOID);
    }

    protected override void ComputeNegotiatedRate()
    {
        if (Interface is TNLInterface tnlInterface && tnlInterface.UnlimitedBandwith)
        {
            CurrentPacketSendSize = 1490U;
            CurrentPacketSendPeriod = 1U;
        }
        else
            base.ComputeNegotiatedRate();
    }

    public NetObject GetGhost()
    {
        return GetScopeObject();
    }

    public int GetTimeSinceLastMessage()
    {
        return Interface.GetCurrentTime() - LastPacketRecvTime;
    }

    private void ProcessFragment(ByteBuffer theData, SFragmentData sFragment, uint _, ushort fragment, ushort fragmentId, ushort fragmentCount)
    {
        if (sFragment.FragmentId != fragment)
        {
            if (fragment > 0)
                Console.WriteLine("Dropped fragment: {0} vs {1}", sFragment.FragmentId, fragment);

            sFragment.FragmentId = fragment;
            sFragment.TotalSize = 0;
            sFragment.MapFragments.Clear();
        }

        sFragment.MapFragments.Add(fragmentId, theData);
        sFragment.TotalSize += theData.GetBufferSize();

        if (sFragment.MapFragments.Count == fragmentCount)
        {
            Console.WriteLine("Reassembling fragment {0} ({1} fragments", sFragment.FragmentId, fragmentCount);

            var combined = new ByteBuffer(sFragment.TotalSize);

            var off = 0U;

            for (var i = 0; i < sFragment.MapFragments.Count; ++i)
            {
                if (!sFragment.MapFragments.ContainsKey(i))
                {
                    Console.WriteLine("Big error! Fragment doesn't contain a buffer! Fragment: {0} | Index: {1}", fragment, i);
                    return;
                }

                var buff = sFragment.MapFragments[i];

                Array.Copy(buff.GetBuffer(), 0, combined.GetBuffer(), off, buff.GetBufferSize());
                off += buff.GetBufferSize();
            }

            sFragment.FragmentId = 0;
            sFragment.TotalSize = 0;
            sFragment.MapFragments.Clear();

            HandlePacket(combined);
        }
    }

    private class SFragmentData
    {
        public uint FragmentId { get; set; }
        public uint TotalSize { get; set; }
        public readonly Dictionary<int, ByteBuffer> MapFragments;

        public SFragmentData()
        {
            FragmentId = 0;
            TotalSize = 0;
            MapFragments = new Dictionary<int, ByteBuffer>();
        }
    }

    #region RPC Classes
    private class RPCMsgGuaranteed : RPCEvent
    {
        public static NetClassRepInstance<RPCMsgGuaranteed> DynClassRep;
        public RPCMsgGuaranteed()
            : base(RPCGuaranteeType.RPCGuaranteed, RPCDirection.RPCDirAny)
        { Functor = new FunctorDecl<TNLConnection>("rpcMsgGuaranteed_remote", new[] { typeof(uint), typeof(ByteBuffer) }); }
        public override bool CheckClassType(object obj) { return (obj as TNLConnection) != null; }
        public override NetClassRep GetClassRep() { return DynClassRep; }
    }

    private class RPCMsgGuaranteedOrdered : RPCEvent
    {
        public static NetClassRepInstance<RPCMsgGuaranteedOrdered> DynClassRep;
        public RPCMsgGuaranteedOrdered()
            : base(RPCGuaranteeType.RPCGuaranteedOrdered, RPCDirection.RPCDirAny)
        { Functor = new FunctorDecl<TNLConnection>("rpcMsgGuaranteedOrdered_remote", new[] { typeof(uint), typeof(ByteBuffer) }); }
        public override bool CheckClassType(object obj) { return (obj as TNLConnection) != null; }
        public override NetClassRep GetClassRep() { return DynClassRep; }
    }

    private class RPCMsgNonGuaranteed : RPCEvent
    {
        public static NetClassRepInstance<RPCMsgNonGuaranteed> DynClassRep;
        public RPCMsgNonGuaranteed()
            : base(RPCGuaranteeType.RPCUnguaranteed, RPCDirection.RPCDirAny)
        { Functor = new FunctorDecl<TNLConnection>("rpcMsgNonGuaranteed_remote", new[] { typeof(uint), typeof(ByteBuffer) }); }
        public override bool CheckClassType(object obj) { return (obj as TNLConnection) != null; }
        public override NetClassRep GetClassRep() { return DynClassRep; }
    }

    private class RPCMsgGuaranteedFragmented : RPCEvent
    {
        public static NetClassRepInstance<RPCMsgGuaranteedFragmented> DynClassRep;
        public RPCMsgGuaranteedFragmented()
            : base(RPCGuaranteeType.RPCGuaranteed, RPCDirection.RPCDirAny)
        { Functor = new FunctorDecl<TNLConnection>("rpcMsgGuaranteedFragmented_remote", new[] { typeof(uint), typeof(ushort), typeof(ushort), typeof(ushort), typeof(ByteBuffer) }); }
        public override bool CheckClassType(object obj) { return (obj as TNLConnection) != null; }
        public override NetClassRep GetClassRep() { return DynClassRep; }
    }

    private class RPCMsgGuaranteedOrderedFragmented : RPCEvent
    {
        public static NetClassRepInstance<RPCMsgGuaranteedOrderedFragmented> DynClassRep;
        public RPCMsgGuaranteedOrderedFragmented()
            : base(RPCGuaranteeType.RPCGuaranteedOrdered, RPCDirection.RPCDirAny)
        { Functor = new FunctorDecl<TNLConnection>("rpcMsgGuaranteedOrderedFragmented_remote", new[] { typeof(uint), typeof(ushort), typeof(ushort), typeof(ushort), typeof(ByteBuffer) }); }
        public override bool CheckClassType(object obj) { return (obj as TNLConnection) != null; }
        public override NetClassRep GetClassRep() { return DynClassRep; }
    }

    private class RPCMsgNonGuaranteedFragmented : RPCEvent
    {
        public static NetClassRepInstance<RPCMsgNonGuaranteedFragmented> DynClassRep;
        public RPCMsgNonGuaranteedFragmented()
            : base(RPCGuaranteeType.RPCUnguaranteed, RPCDirection.RPCDirAny)
        { Functor = new FunctorDecl<TNLConnection>("rpcMsgNonGuaranteedFragmented_remote", new[] { typeof(uint), typeof(ushort), typeof(ushort), typeof(ushort), typeof(ByteBuffer) }); }
        public override bool CheckClassType(object obj) { return (obj as TNLConnection) != null; }
        public override NetClassRep GetClassRep() { return DynClassRep; }
    }
    #endregion
}
