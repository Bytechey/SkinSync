using LiteNetLib.Utils;

namespace SkinSyncMod
{
    /// <summary>子端向服务端请求拉取指定皮肤包；服务端再选一个持有该皮肤的子端单播请求。</summary>
    public struct SkinPackRequestMessage : INetSerializable
    {
        public string skinID;

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(skinID ?? "");
        }

        public void Deserialize(NetDataReader reader)
        {
            skinID = reader.GetString();
        }

        public void WriteTo(NetDataWriter writer)
        {
            writer.Put(SkinNetworkIDs.SkinPackRequestMessageId);
            Serialize(writer);
        }
    }
}
