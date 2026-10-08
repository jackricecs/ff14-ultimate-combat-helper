using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using UltimateCombatHelper.Diagnostics;
using UltimateCombatHelper.Modules.Dot;

namespace UltimateCombatHelper.Config
{
    /// <summary>
    /// 根配置（JSON）。每个模块一个配置节；新增模块时在此加一个 [DataMember]。
    /// DataContractJsonSerializer 反序列化时不调用构造函数，默认值统一放在 [OnDeserializing] 里。
    /// </summary>
    [DataContract]
    public sealed class UchConfig
    {
        public const int CurrentVersion = 1;

        [DataMember(Order = 0)] public int Version;
        [DataMember(Order = 1)] public GeneralConfig General;
        [DataMember(Order = 2)] public DotConfig Dot;
        [DataMember(Order = 3)] public DiagnosticsConfig Diagnostics;

        public UchConfig() { Defaults(); }
        [OnDeserializing] private void OnDeserializing(StreamingContext c) { Defaults(); }
        [OnDeserialized] private void OnDeserialized(StreamingContext c)
        {
            if (General == null) General = new GeneralConfig();
            if (Dot == null) Dot = DotConfig.CreateDefault();
            if (Diagnostics == null) Diagnostics = new DiagnosticsConfig();
        }

        private void Defaults()
        {
            Version = CurrentVersion;
            General = new GeneralConfig();
            Dot = DotConfig.CreateDefault();
            Diagnostics = new DiagnosticsConfig();
        }

        /// <summary>当前配置的 JSON 文本（诊断包里附带）。</summary>
        public string ToJson()
        {
            using (var ms = new MemoryStream())
            {
                using (var w = JsonReaderWriterFactory.CreateJsonWriter(ms, new UTF8Encoding(false), false, true, "  "))
                {
                    new DataContractJsonSerializer(typeof(UchConfig)).WriteObject(w, this);
                    w.Flush();
                }
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }
    }

    [DataContract]
    public sealed class GeneralConfig
    {
        [DataMember(Order = 0)] public bool Enabled;
        /// <summary>测试模式：忽略各模块的副本难度过滤（打木人用）。</summary>
        [DataMember(Order = 1)] public bool TestModeAnywhere;
        /// <summary>同一句话的最短重复间隔（秒）。</summary>
        [DataMember(Order = 2)] public double SpeechCooldownSeconds;

        public GeneralConfig() { Defaults(); }
        [OnDeserializing] private void OnDeserializing(StreamingContext c) { Defaults(); }
        private void Defaults() { Enabled = true; TestModeAnywhere = false; SpeechCooldownSeconds = 1.5; }
    }

    public static class ConfigStore
    {
        private static DataContractJsonSerializer Serializer()
        {
            return new DataContractJsonSerializer(typeof(UchConfig));
        }

        /// <summary>读取配置；文件不存在则写入默认值；解析失败则备份原文件并使用默认值。</summary>
        public static UchConfig Load(string path, Action<string> log = null)
        {
            if (!File.Exists(path))
            {
                var d = new UchConfig();
                Save(path, d);
                return d;
            }
            try
            {
                using (var fs = File.OpenRead(path))
                {
                    var c = (UchConfig)Serializer().ReadObject(fs);
                    return c ?? new UchConfig();
                }
            }
            catch (Exception ex)
            {
                string bak = path + "." + DateTime.Now.ToString("yyyyMMddHHmmss") + ".bak";
                try { File.Copy(path, bak, true); } catch (Exception) { }
                if (log != null) log("配置文件解析失败，已备份为 " + bak + "，改用默认配置：" + ex.Message);
                return new UchConfig();
            }
        }

        public static void Save(string path, UchConfig config)
        {
            string tmp = path + ".tmp";
            using (var fs = File.Create(tmp))
            using (var w = JsonReaderWriterFactory.CreateJsonWriter(fs, new UTF8Encoding(false), false, true, "  "))
            {
                Serializer().WriteObject(w, config);
                w.Flush();
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);   // 先写临时文件再替换，避免写一半崩溃导致配置损坏
        }
    }
}
