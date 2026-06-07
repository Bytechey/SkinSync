using LiteNetLib.Utils;

namespace SkinSyncMod
{
    /// <summary>承载 SkinPackCodec 打包后的皮肤目录字节流；接收端调用 SkinPackCodec.UnpackAndCache 写缓存。</summary>
    public struct SkinPackDataMessage : INetSerializable
    {
        public byte[] payload;
        public bool allowPersist;

        public void Serialize(NetDataWriter writer)
        {
            writer.PutBytesWithLength(payload ?? new byte[0]);
            writer.Put(allowPersist);
        }

        public void Deserialize(NetDataReader reader)
        {
            payload = reader.GetBytesWithLength();
            allowPersist = reader.GetBool();
        }

        public void WriteTo(NetDataWriter writer)
        {
            writer.Put(SkinNetworkIDs.SkinPackDataMessageId);
            Serialize(writer);
        }
    }
}
