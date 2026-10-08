using System.Runtime.Serialization;

namespace UltimateCombatHelper.Diagnostics
{
    /// <summary>诊断日志与导出设置。</summary>
    [DataContract]
    public sealed class DiagnosticsConfig
    {
        /// <summary>保存哪些级别的运行日志：Info / Debug / Trace。</summary>
        [DataMember(Order = 0)] public string Level;
        /// <summary>记录战斗日志行，导出诊断包时附上。</summary>
        [DataMember(Order = 1)] public bool RecordLines;
        /// <summary>也记录其他玩家的技能/状态行（排查他人相关问题时开，文件更大）。</summary>
        [DataMember(Order = 2)] public bool RecordOthers;
        /// <summary>最多保留的战斗日志行数（环形缓冲）。</summary>
        [DataMember(Order = 3)] public int MaxLines;
        /// <summary>每次脱战自动保存一份战斗记录到日志目录。</summary>
        [DataMember(Order = 4)] public bool AutoSaveFights;
        /// <summary>自动保存的战斗记录最多保留几份。</summary>
        [DataMember(Order = 5)] public int KeepFightFiles;
        /// <summary>导出时把玩家名替换成"我/玩家1…"。</summary>
        [DataMember(Order = 6)] public bool MaskNames;
        /// <summary>短于这个秒数的战斗不自动保存（拉怪、误进战斗）。</summary>
        [DataMember(Order = 7)] public double MinFightSeconds;

        public DiagnosticsConfig() { Defaults(); }
        [OnDeserializing] private void OnDeserializing(StreamingContext c) { Defaults(); }

        private void Defaults()
        {
            Level = DiagLevel.Debug.ToString();
            RecordLines = true;
            RecordOthers = false;
            MaxLines = 20000;
            AutoSaveFights = true;
            KeepFightFiles = 20;
            MaskNames = true;
            MinFightSeconds = 15;
        }

        public DiagLevel ParsedLevel() { return DiagLog.ParseLevel(Level); }
    }
}
