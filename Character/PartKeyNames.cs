namespace SkinSyncMod.Character
{
    /// <summary>定义跨角色共享的部位语义键常量与构造函数。</summary>
    internal static class PartKeyNames
    {
        internal const string Tail = "tail";

        internal const string Head = "Head";
        internal const string UpTorso = "UpTorso";
        internal const string DownTorso = "DownTorso";

        internal const string DefaultHead = "defaultHead";
        internal const string DefaultHeadMouth = "defaultHeadMouth";
        internal const string DefaultHeadMouthHalf = "defaultHeadMouthHalf";
        internal const string EyesGone = "eyesGone";
        internal const string EyesGoneHealed = "eyesGoneHealed";

        internal static string DisfiguredHead(int index) => "disfiguredHead[" + index + "]";
        internal static string DisfiguredHeadHeal(int index) => "disfiguredHeadHeal[" + index + "]";
        internal static string EyeFront(int index) => "eyeList[" + index + "].front";
        internal static string EyeBack(int index) => "eyeList[" + index + "].back";
    }
}
