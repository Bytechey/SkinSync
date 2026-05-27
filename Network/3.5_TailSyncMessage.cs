using LiteNetLib.Utils;

namespace SkinSyncMod
{
    /// <summary>
    /// 子端改动尾巴形变参数时发到服务端，服务端校验后广播全员。
    /// 17 字段对齐 TailDeformConfig；接收端覆盖到全局 static + 写 settings.SetTailOverride（按 skin）。
    /// </summary>
    public struct TailSyncMessage : INetSerializable
    {
        public uint netId;
        public string skinID;
        public bool enabled;
        public bool frontGuard;
        public int segments;
        public int constraintIters;
        public float damping;
        public float stiffness;
        public float maxBendDeg;
        public float anchorFollow;
        public float smoothness;
        public float maxStep;
        public float maxFixedDt;
        public float frontGuardMargin;
        public float gravityX;
        public float gravityY;
        public float windFreq;
        public float windAmp;
        public float speedDisturb;

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(netId);
            writer.Put(skinID ?? "");
            writer.Put(enabled);
            writer.Put(frontGuard);
            writer.Put(segments);
            writer.Put(constraintIters);
            writer.Put(damping);
            writer.Put(stiffness);
            writer.Put(maxBendDeg);
            writer.Put(anchorFollow);
            writer.Put(smoothness);
            writer.Put(maxStep);
            writer.Put(maxFixedDt);
            writer.Put(frontGuardMargin);
            writer.Put(gravityX);
            writer.Put(gravityY);
            writer.Put(windFreq);
            writer.Put(windAmp);
            writer.Put(speedDisturb);
        }

        public void Deserialize(NetDataReader reader)
        {
            netId = reader.GetUInt();
            skinID = reader.GetString();
            enabled = reader.GetBool();
            frontGuard = reader.GetBool();
            segments = reader.GetInt();
            constraintIters = reader.GetInt();
            damping = reader.GetFloat();
            stiffness = reader.GetFloat();
            maxBendDeg = reader.GetFloat();
            anchorFollow = reader.GetFloat();
            smoothness = reader.GetFloat();
            maxStep = reader.GetFloat();
            maxFixedDt = reader.GetFloat();
            frontGuardMargin = reader.GetFloat();
            gravityX = reader.GetFloat();
            gravityY = reader.GetFloat();
            windFreq = reader.GetFloat();
            windAmp = reader.GetFloat();
            speedDisturb = reader.GetFloat();
        }

        public void WriteTo(NetDataWriter writer)
        {
            writer.Put(SkinNetworkIDs.TailSyncMessageId);
            Serialize(writer);
        }
    }
}
