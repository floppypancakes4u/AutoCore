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
using AutoCore.Game.Managers;
using AutoCore.Game.Packets;
using AutoCore.Utils;

public partial class TNLConnection : GhostConnection
{
    private static NetClassRepInstance<TNLConnection> _dynClassRep;
#pragma warning disable IDE0052 // Remove unread private members
    private static NetConnectionRep _connRep;
#pragma warning restore IDE0052 // Remove unread private members

    private uint Key { get; set; }
    private long PlayerCoid { get; set; }
    private ushort FragmentCounter { get; set; } = 1;

    public Account Account { get; set; }
    //public Character CurrentCharacter { get; set; }

    private SFragmentData FragmentGuaranteed { get; } = new();
    private SFragmentData FragmentNonGuaranteed { get; } = new();
    private SFragmentData FragmentGuaranteedOrdered { get; } = new();

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
        SetFixedRateParameters(50, 50, 40000, 40000);
        SetPingTimeouts(7000, 6);
    }

    ~TNLConnection()
    {
        DeleteLocalGhosts();

        //CharacterManager.LogoutCharacter(this);

        Logger.WriteLog(LogType.Network, "Client ({0} | {1}) disconnected", Account.Id, Account.Name);
    }

    public void SendGamePacket(BasePacket packet, RPCGuaranteeType type = RPCGuaranteeType.RPCGuaranteedOrdered, bool skipOpcode = false)
    {
        Logger.WriteLog(LogType.Network, "Outgoing Packet: {0}", packet.Opcode);

        var dest = ArrayPool<byte>.Shared.Rent(0x4000);

        int packetLength;

        using (var writer = new BinaryWriter(new MemoryStream(dest)))
        {
            if (!skipOpcode)
                writer.Write(packet.Opcode);

            packet.Write(writer);

            packetLength = (int)writer.BaseStream.Position;
        }

        var arr = new byte[packetLength];
        Buffer.BlockCopy(dest, 0, arr, 0, packetLength);

        ArrayPool<byte>.Shared.Return(dest);

        var arrLength = (uint)packetLength;
        if (arrLength > 1400U)
        {
            ++FragmentCounter;

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
                        rpcMsgGuaranteedFragmented((uint)packet.Opcode, FragmentCounter, i, count, stream);
                        break;

                    case RPCGuaranteeType.RPCGuaranteedOrdered:
                        rpcMsgGuaranteedOrderedFragmented((uint)packet.Opcode, FragmentCounter, i, count, stream);
                        break;

                    case RPCGuaranteeType.RPCUnguaranteed:
                        rpcMsgNonGuaranteedFragmented((uint)packet.Opcode, FragmentCounter, i, count, stream);
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
                    HandleLoginNewCharacterPacket(reader);
                    break;

                case GameOpcode.LoginDeleteCharacter:
                    HandleLoginDeleteCharacterPacket(reader);
                    break;

                case GameOpcode.News:
                    HandleNews(reader);
                    break;

                case GameOpcode.Login:
                    HandleLoginPacket(reader);
                    break;

                case GameOpcode.Disconnect:
                    //HandleDisconnect(reader);
                    break;

                case GameOpcode.Chat:
                    ChatManager.Instance.HandleChat(this, reader);
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
                    ChatManager.Instance.HandleBroadcast(this, reader);
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

        ProcessFragment(data, FragmentGuaranteed, type, fragment, fragmentId, fragmentCount);
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

        ProcessFragment(data, FragmentGuaranteedOrdered, type, fragment, fragmentId, fragmentCount);
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

        ProcessFragment(data, FragmentNonGuaranteed, type, fragment, fragmentId, fragmentCount);
    }
#pragma warning restore IDE1006 // Naming Styles
    #endregion RPC Calls

    public override NetClassRep GetClassRep()
    {
        return _dynClassRep;
    }

    public void SetPlayerCOID(long connId)
    {
        PlayerCoid = connId;
    }

    public long GetPlayerCOID()
    {
        return PlayerCoid;
    }

    public override NetClassGroup GetNetClassGroup()
    {
        return NetClassGroup.NetClassGroupGame;
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

        stream.Write(TNLInterface.Version);
        stream.Write(Key);
        stream.Write(PlayerCoid);
    }

    public override bool ReadConnectRequest(BitStream stream, ref string errorString)
    {
        if (!base.ReadConnectRequest(stream, ref errorString))
            return false;

        if (!stream.Read(out int version) || version != TNLInterface.Version)
        {
            errorString = "Incorrect Version";
            return false;
        }

        if (!stream.Read(out uint key))
        {
            errorString = "Unknown Key";
            return false;
        }

        if (!stream.Read(out long playerCoid))
        {
            errorString = "Unknown player ID";
            return false;
        }

        Key = key;
        PlayerCoid = playerCoid;

        return true;
    }

    public override void OnConnectionEstablished()
    {
        base.OnConnectionEstablished();

        if (Interface is TNLInterface tInterface && tInterface.DoGhosting)
        {
            SetGhostTo(false);
            SetGhostFrom(true);
            ActivateGhosting();
        }

        Logger.WriteLog(LogType.Network, "Client ({1}) connected from {0}", GetNetAddressString(), PlayerCoid);
    }

    public NetObject GetGhost() => GetScopeObject();

    public int GetTimeSinceLastMessage() => Interface.GetCurrentTime() - LastPacketRecvTime;

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
        public uint FragmentId { get; set; } = 0;
        public uint TotalSize { get; set; } = 0;
        public Dictionary<int, ByteBuffer> MapFragments { get; } = new();
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
