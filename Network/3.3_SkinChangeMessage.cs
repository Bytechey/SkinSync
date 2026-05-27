using LiteNetLib.Utils;

namespace SkinSyncMod
{
    public struct SkinChangeMessage : INetSerializable
    {
        public string skinID;   // 皮肤标识，比如 "st1"
        public uint netId;      // 玩家的 netId

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(skinID);
            writer.Put(netId);
        }

        public void Deserialize(NetDataReader reader)
        {
            skinID = reader.GetString();
            netId = reader.GetUInt();
        }

        // 辅助写入：先写入消息ID，再写入自身
        public void WriteTo(NetDataWriter writer)
        {
            writer.Put(SkinNetworkIDs.SkinChangeMessageId);
            Serialize(writer);
        }
    }
    public static class SkinNetworkIDs
    {
        public const ushort SkinChangeMessageId = 50000; // 选择一个未被占用的ID
        public const ushort AccessorySyncMessageId = 50001;
        public const ushort TailSyncMessageId = 50002;
    }

}