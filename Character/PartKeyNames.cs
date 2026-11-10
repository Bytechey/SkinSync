namespace SkinSyncMod.Character
{
    /// <summary>
    /// 部位语义键的固定定义。
    /// 肢体键直接复用游戏自己的 GameObject 名（vanilla 也用它们做键：Item.desiredWearLimb + Body.LimbByName），
    /// 表情键对应 FacialExpression 的字段名——字段语义由游戏代码写死，不随角色变化。
    /// 这里是整个模组唯一允许"写死部位名"的地方。
    /// </summary>
    internal static class PartKeyNames
    {
        /// <summary>尾巴。三个角色的尾巴 GameObject 都叫 experimentTail，所以单独用语义键。</summary>
        internal const string Tail = "tail";

        // —— 肢体（与 GameObject 名一致，为便于阅读在此登记）—— //
        internal const string Head = "Head";
        internal const string UpTorso = "UpTorso";
        internal const string DownTorso = "DownTorso";

        // —— 表情：FacialExpression 的 Sprite 字段 —— //
        internal const string DefaultHead = "defaultHead";
        internal const string DefaultHeadMouth = "defaultHeadMouth";
        internal const string DefaultHeadMouthHalf = "defaultHeadMouthHalf";
        internal const string EyesGone = "eyesGone";
        internal const string EyesGoneHealed = "eyesGoneHealed";

        /// <summary>毁容头数组元素键，例：disfiguredHead[0]。</summary>
        internal static string DisfiguredHead(int index) => "disfiguredHead[" + index + "]";

        /// <summary>毁容愈合头数组元素键。</summary>
        internal static string DisfiguredHeadHeal(int index) => "disfiguredHeadHeal[" + index + "]";

        /// <summary>眼睛表情的正/背面键，例：eyeList[0].front。</summary>
        internal static string EyeFront(int index) => "eyeList[" + index + "].front";

        /// <summary>眼睛表情的背面键。</summary>
        internal static string EyeBack(int index) => "eyeList[" + index + "].back";
    }
}
