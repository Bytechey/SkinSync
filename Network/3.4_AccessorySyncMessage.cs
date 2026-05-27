using LiteNetLib.Utils;

namespace SkinSyncMod
{
    /// <summary>
    /// 子端改动配件覆盖时发到服务端，服务端校验后广播全员。
    /// 字段对齐 SkinSyncSettings.AccessoryOverride（5 个字段任选其一改动 + enabled 必填）。
    /// </summary>
    public struct AccessorySyncMessage : INetSerializable
    {
        public uint netId;
        public string skinID;
        public string accId;
        public bool enabled;
        public int offX;
        public int offY;
        public float rotation;
        public int zOrder;

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(netId);
            writer.Put(skinID ?? "");
            writer.Put(accId ?? "");
            writer.Put(enabled);
            writer.Put(offX);
            writer.Put(offY);
            writer.Put(rotation);
            writer.Put(zOrder);
        }

        public void Deserialize(NetDataReader reader)
        {
            netId = reader.GetUInt();
            skinID = reader.GetString();
            accId = reader.GetString();
            enabled = reader.GetBool();
            offX = reader.GetInt();
            offY = reader.GetInt();
            rotation = reader.GetFloat();
            zOrder = reader.GetInt();
        }

        public void WriteTo(NetDataWriter writer)
        {
            writer.Put(SkinNetworkIDs.AccessorySyncMessageId);
            Serialize(writer);
        }
    }
}
