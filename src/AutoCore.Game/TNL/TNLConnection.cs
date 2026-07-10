using System.Buffers;
using System.IO;

using TNL.Data;
using TNL.Entities;
using TNL.Structures;
using TNL.Types;
using TNL.Utils;

namespace AutoCore.Game.TNL;

using AutoCore.Database.Char.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Diagnostics;
using AutoCore.Game.Entities;
using AutoCore.Game.Extensions;
using AutoCore.Game.Inventory;
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

    /// <summary>
    /// Coids of foreign global vehicles for which an in-world <c>CreateVehicle</c> has already been
    /// sent this map session. A guaranteed-ordered CreateVehicle plants the object in the client's
    /// table for the whole session; TNL later ghost-kills and re-scopes it without any DestroyObject,
    /// so scope must send the create at most once per coid per session. Otherwise driving past the
    /// scope-drop radius and back delivers a duplicate in-world create for an object the client still
    /// holds — the duplicate-create / 'Invalid Packet' failure class. Cleared by
    /// <see cref="ResetGhosting"/> (map transfer), which is exactly when the client discards its
    /// local object table (rpcEndGhosting → DeleteLocalGhosts).
    /// </summary>
    private readonly HashSet<long> _globalVehicleCreatesSent = new();

    public Account Account { get; set; }
    public Character CurrentCharacter { get; set; }

    /// <summary>
    /// When set (unit tests), world-state logout persistence uses this instead of the EF singleton.
    /// </summary>
    internal static ICharacterWorldStatePersistence WorldStatePersistenceForTests { get; set; }

    private static ICharacterWorldStatePersistence WorldStatePersistence =>
        WorldStatePersistenceForTests ?? CharacterWorldStatePersistence.Instance;

    private SFragmentData FragmentGuaranteed { get; } = new();
    private SFragmentData FragmentNonGuaranteed { get; } = new();
    private SFragmentData FragmentGuaranteedOrdered { get; } = new();

    /// <summary>
    /// Records that a foreign global-vehicle <c>CreateVehicle</c> is being sent for <paramref name="coid"/>
    /// this map session. Returns <c>true</c> the first time (caller should send the create), <c>false</c>
    /// on repeat scopes so the create is never duplicated for an object the client still holds.
    /// </summary>
    public bool TryMarkGlobalVehicleCreateSent(long coid) => _globalVehicleCreatesSent.Add(coid);

    /// <summary>
    /// Forgets which foreign global-vehicle creates were sent. Called when the client discards its
    /// local object table on a map transfer (see <see cref="EnsureGhostsAndScopeAfterMapTransfer"/>),
    /// so the next map session legitimately re-sends creates.
    /// </summary>
    internal void ClearGlobalVehicleCreateTracking() => _globalVehicleCreatesSent.Clear();

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

        if (Account != null)
            Logger.WriteLog(LogType.Network, "Client ({0} | {1}) disconnected", Account.Id, Account.Name);
        else
            Logger.WriteLog(LogType.Network, "Client ({0}) disconnected", PlayerCoid);
    }

    /// <summary>
    /// When set (unit tests), invoked instead of the real TNL RPC send path.
    /// </summary>
    internal static Action<TNLConnection, BasePacket> TestPacketSink { get; set; }

    public void SendGamePacket(BasePacket packet, RPCGuaranteeType type = RPCGuaranteeType.RPCGuaranteedOrdered, bool skipOpcode = false)
    {
        Logger.WriteLog(LogType.Network, "Outgoing Packet: {0}", packet.Opcode);

        if (TestPacketSink != null)
        {
            if (WireDiag.Enabled)
            {
                // Tests often short-circuit before payload serialization; still record identity.
                WireDiag.RecordGamePacket(
                    packet.Opcode.ToString(),
                    coid: CurrentCharacter?.ObjectId.Coid ?? GetPlayerCOID(),
                    bytes: -1,
                    playerCoid: CurrentCharacter?.ObjectId.Coid ?? GetPlayerCOID());
            }

            TestPacketSink(this, packet);
            return;
        }

        byte[] arr;

        using (var stream = new MemoryStream(0x4000))
        using (var writer = new BinaryWriter(stream))
        {
            if (!skipOpcode)
                writer.Write(packet.Opcode);

            packet.Write(writer);

            // Many packets intentionally "skip" reserved bytes via `writer.BaseStream.Position += N`.
            // With a MemoryStream, advancing Position does NOT automatically extend Length unless we do it.
            // If we don't extend Length, `ToArray()` will return fewer bytes than `Position`, and
            // downstream fragmentation will throw (and clients will see garbage/uninitialized fields).
            stream.SetLength(stream.Position);

            arr = stream.ToArray();
        }

        if (WireDiag.Enabled)
        {
            var previewLen = Math.Min(arr.Length, 48);
            var hex = previewLen > 0 ? Convert.ToHexString(arr.AsSpan(0, previewLen)) : null;
            long objectCoid = 0;
            if (packet is AutoCore.Game.Packets.Sector.CreateVehiclePacket cv)
                objectCoid = cv.ObjectId.Coid;
            else if (packet is AutoCore.Game.Packets.Sector.CreateSimpleObjectPacket so)
                objectCoid = so.ObjectId.Coid;

            WireDiag.RecordGamePacket(
                packet.Opcode.ToString(),
                coid: objectCoid != 0 ? objectCoid : (CurrentCharacter?.ObjectId.Coid ?? GetPlayerCOID()),
                bytes: arr.Length,
                playerCoid: CurrentCharacter?.ObjectId.Coid ?? GetPlayerCOID(),
                hexPreview: hex);
        }

        if (packet.Opcode == GameOpcode.InventoryGrabResponse)
            InventoryGrabDebugLog.RecordOutgoing(arr);
        else
            InventoryDropDebugLog.RecordOutgoingIfTossRelated(packet.Opcode, arr);

        if (packet.Opcode is GameOpcode.CreateSimpleObject
            or GameOpcode.CreateArmor
            or GameOpcode.CreatePowerPlant
            or GameOpcode.CreateWeapon
            or GameOpcode.CreateWheelSet
            or GameOpcode.InventoryCargoSendAll
            or GameOpcode.InventoryAddItem
            or GameOpcode.InventoryGrabResponse
            or GameOpcode.InventoryDropResponse)
        {
            var previewLength = Math.Min(arr.Length, 96);
            Logger.WriteLog(
                LogType.Debug,
                $"Outgoing InventoryFlow Packet: {packet.Opcode} bytes={arr.Length} preview={Convert.ToHexString(arr.AsSpan(0, previewLength))}");
        }

        var arrLength = (uint)arr.Length;
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

                Array.Copy(arr, (int)doneSize, tempBuff, 0, (int)buffSize);

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
        var rawBytes = new byte[buffer.GetBufferSize()];
        Array.Copy(buffer.GetBuffer(), rawBytes, rawBytes.Length);

        var reader = new BinaryReader(new MemoryStream(rawBytes));
        var rawOpcode = reader.ReadUInt32();
        var gameOpcode = (GameOpcode)rawOpcode;

        if (gameOpcode == GameOpcode.InventoryGrab)
            InventoryGrabDebugLog.RecordIncoming(rawBytes);
        else
            InventoryDropDebugLog.RecordIncomingIfTossRelated(gameOpcode, rawBytes);

        // Check if the opcode is a valid enum value
        if (!Enum.IsDefined(typeof(GameOpcode), gameOpcode))
        {
            Logger.WriteLog(LogType.Error, "Unknown GameOpcode received from client: 0x{0:X} ({1})", rawOpcode, rawOpcode);
        }

        switch (gameOpcode)
        {
            case GameOpcode.CreatureMoved:
            case GameOpcode.VehicleMoved:
                break;

            default:
                Logger.WriteLog(LogType.Network, "Incoming Packet: {0}", gameOpcode);
                break;
        }

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
                    HandleNewsPacket(reader);
                    break;

                case GameOpcode.Login:
                    HandleLoginPacket(reader);
                    break;

                case GameOpcode.Disconnect:
                    HandleDisconnectPacket(reader);
                    break;

                case GameOpcode.Chat:
                    ChatManager.Instance.HandleChatPacket(this, reader);
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
                    HandleConvoyMissionsRequest(reader);
                    break;

                case GameOpcode.RequestClanName:
                    ClanManager.Instance.HandleRequestClanNamePacket(CurrentCharacter, reader);
                    break;

                case GameOpcode.ClanUpdate:
                    ClanManager.Instance.HandleClanUpdatePacket(CurrentCharacter, reader);
                    break;

                // Sector
                case GameOpcode.TransferFromGlobal:
                    HandleTransferFromGlobalPacket(reader);
                    break;

                case GameOpcode.TransferFromGlobalStage2:
                    HandleTransferFromGlobalStage2Packet(reader);
                    break;

                case GameOpcode.TransferFromGlobalStage3:
                    HandleTransferFromGlobalStage3Packet(reader);
                    break;

                case GameOpcode.UpdateFirstTimeFlagsRequest:
                    HandleUpdateFirstTimeFlagsRequest(reader);
                    break;

                case GameOpcode.Broadcast:
                    ChatManager.Instance.HandleBroadcastPacket(this, reader);
                    break;

                case GameOpcode.CreatureMoved:
                    HandleCreatureMovedPacket(reader);
                    break;

                case GameOpcode.VehicleMoved:
                    HandleVehicleMovedPacket(reader);
                    break;

                case GameOpcode.MapTransferRequest:
                    MapManager.Instance.HandleTransferRequestPacket(CurrentCharacter, reader);
                    break;

                case GameOpcode.RespawnInSector:
                    RespawnManager.Instance.HandleRespawnInSectorPacket(CurrentCharacter, reader);
                    break;
                
                case GameOpcode.UseObject:
                    HandleUseObjectPacket(reader);
                    break;

                case GameOpcode.AutoPatrol:
                    HandleAutoPatrolPacket(reader);
                    break;

                case GameOpcode.MissionDialogResponse:
                    HandleMissionDialogResponse(reader);
                    break;

                case GameOpcode.ChangeCombatModeRequest:
                    MapManager.Instance.HandleChangeCombatModeRequest(CurrentCharacter, reader);
                    break;

                case GameOpcode.ItemPickup:
                    HandleItemPickupPacket(reader);
                    break;

                case GameOpcode.ItemDrop:
                    HandleItemDropPacket(reader);
                    break;

                case GameOpcode.InventoryGrab:
                    HandleInventoryGrabPacket(reader);
                    break;

                case GameOpcode.InventoryDrop:
                    HandleInventoryDropPacket(reader);
                    break;

                case GameOpcode.InventoryDropMM:
                    HandleInventoryDropMMPacket(reader);
                    break;

                case GameOpcode.InventoryDestroyItem:
                    HandleInventoryDestroyItemPacket(reader);
                    break;

                case GameOpcode.Firing:
                    Logger.WriteLog(LogType.Error, "Unhandled Opcode: {0}", gameOpcode);
                    break;

                case GameOpcode.Damage:
                    Logger.WriteLog(LogType.Error, "Unhandled Opcode: {0}", gameOpcode);
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
        {
            Logger.WriteLog(LogType.Error, $"ReadConnectRequest: Base class validation failed: {errorString}");
            return false;
        }

        if (!stream.Read(out int version))
        {
            errorString = "Incorrect Version";
            Logger.WriteLog(LogType.Error, "ReadConnectRequest: Failed to read version");
            return false;
        }

        var expectedVersion = TNLInterface.Version;
        if (Interface is TNLInterface tInterface)
        {
            expectedVersion = tInterface.ExpectedVersion;
        }

        if (version != expectedVersion)
        {
            if (Interface is TNLInterface tInterface2 && tInterface2.AllowVersionMismatch)
            {
                Logger.WriteLog(LogType.Network, $"ReadConnectRequest: Version mismatch allowed. Expected: {expectedVersion}, Got: {version}");
            }
            else
            {
                errorString = "Incorrect Version";
                Logger.WriteLog(LogType.Error, $"ReadConnectRequest: Version mismatch. Expected: {expectedVersion}, Got: {version}");
                return false;
            }
        }

        if (!stream.Read(out uint key))
        {
            errorString = "Unknown Key";
            Logger.WriteLog(LogType.Error, "ReadConnectRequest: Failed to read key");
            return false;
        }

        if (!stream.Read(out long playerCoid))
        {
            errorString = "Unknown player ID";
            Logger.WriteLog(LogType.Error, "ReadConnectRequest: Failed to read playerCoid");
            return false;
        }

        Key = key;
        PlayerCoid = playerCoid;

        Logger.WriteLog(LogType.Network, $"ReadConnectRequest: Successfully read connection request. Version: {version}, Key: {key}, PlayerCoid: {playerCoid}");
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

        Logger.WriteLog(LogType.Network, $"Client ({PlayerCoid}) connected from {GetNetAddressString()}");
    }

    public override void OnConnectionTerminated(TerminationReason reason, string reasonString)
    {
        EndCharacterSession();

        var accountInfo = Account != null ? $"Account: {Account.Id} ({Account.Name})" : "Not authenticated";
        var address = SafeNetAddressString();
        Logger.WriteLog(LogType.Network, $"Client ({PlayerCoid}) disconnected from {address}. Reason: {reason}, Details: {reasonString}, {accountInfo}");
    }

    /// <summary>Net address for logging; unit tests may construct connections without a bound address.</summary>
    private string SafeNetAddressString()
    {
        try
        {
            return GetNetAddressString() ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    /// <summary>
    /// Sector (owning) disconnect: persist map/pose, then tear down session (SS-03/SS-04).
    /// Global (non-owning) disconnect: only drop this connection's CurrentCharacter reference.
    /// </summary>
    internal void EndCharacterSession()
    {
        if (CurrentCharacter == null)
            return;

        var character = CurrentCharacter;
        var ownsSession = character.OwningConnection == null
            || ReferenceEquals(character.OwningConnection, this);

        if (!ownsSession)
        {
            CurrentCharacter = null;
            return;
        }

        // Persist before SetMap(null) so Map.ContinentId is still available.
        try
        {
            CharacterWorldStatePersistence.PersistFromCharacter(character, WorldStatePersistence);
        }
        catch (Exception ex)
        {
            Logger.WriteLog(LogType.Error,
                $"EndCharacterSession: failed to persist world state for coid {character.ObjectId.Coid}: {ex.Message}");
        }

        // SS-04: drop ownership before teardown so chat/send paths cannot use a dead connection.
        character.SetOwningConnection(null);
        character.SetMap(null);
        character.CurrentVehicle?.SetMap(null);
        character.ClearGhost();
        character.CurrentVehicle?.ClearGhost();
        // Drop living entities from the global registry before clearing the connection
        // binding so reconnect loads a fresh character/vehicle (SS-03).
        ObjectManager.Instance.UnregisterCharacterSession(character);
        CurrentCharacter = null;
    }

    public override void PrepareWritePacket()
    {
        base.PrepareWritePacket();

        if (Ghosting && CurrentCharacter != null && CurrentCharacter.CurrentVehicle != null && CurrentCharacter.CurrentVehicle.Map != null)
            TriggerManager.Instance.CheckTriggersFor(CurrentCharacter?.CurrentVehicle);
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
