// UchPlugin.cs — ACT 插件入口。负责：生命周期、把 ACT 日志行交给 ModuleManager、驱动定时器、承载界面。
// 业务逻辑全部在 UCH.Core（与 ACT 无关、可单元测试）。
using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Advanced_Combat_Tracker;
using UltimateCombatHelper.Config;
using UltimateCombatHelper.Logs;
using UltimateCombatHelper.Modules;
using UltimateCombatHelper.Modules.Dot;
using UltimateCombatHelper.Notify;
using UltimateCombatHelper.Plugin.UI;

[assembly: AssemblyTitle("FF14 Ultimate Combat Helper")]
[assembly: AssemblyDescription("FF14 战斗辅助（DoT 监控等），ACT 插件")]
[assembly: AssemblyProduct("UltimateCombatHelper")]
[assembly: AssemblyVersion("0.2.0.0")]
[assembly: AssemblyInformationalVersion("0.2.0")]

namespace UltimateCombatHelper.Plugin
{
    public sealed class UchPlugin : IActPluginV1, INotifier
    {
        private Label statusLabel;
        private MainPanel panel;
        private FfxivBridge bridge;
        private ModuleManager manager;
        private DotModule dot;
        private System.Threading.Timer tickTimer;

        internal UchConfig Config { get; private set; }
        internal string ConfigPath { get; private set; }
        internal ModuleManager Manager { get { return manager; } }
        internal DotModule Dot { get { return dot; } }

        public static string Version
        {
            get { return typeof(UchPlugin).Assembly.GetName().Version.ToString(3); }
        }

        public void InitPlugin(TabPage pluginScreenSpace, Label pluginStatusText)
        {
            statusLabel = pluginStatusText;

            string dir = Path.Combine(ActGlobals.oFormActMain.AppDataFolder.FullName, "Config");
            Directory.CreateDirectory(dir);
            ConfigPath = Path.Combine(dir, "UltimateCombatHelper.json");

            panel = new MainPanel(this) { Dock = DockStyle.Fill };
            pluginScreenSpace.Controls.Add(panel);
            pluginScreenSpace.Text = "战斗辅助";

            Config = ConfigStore.Load(ConfigPath, Log);

            bridge = new FfxivBridge(Log);
            manager = new ModuleManager(this, GetCurrentTarget);
            manager.Notifier.Cooldown = TimeSpan.FromSeconds(Config.General.SpeechCooldownSeconds);
            manager.Context.MyName = ActGlobals.charName;
            bridge.Attached += () => manager.Context.SetMyId(bridge.GetPlayerId());

            dot = new DotModule(Config.Dot, Config.General.TestModeAnywhere);
            dot.Warned += (text, ws) => Log(string.Format("[{0:HH:mm:ss}] 提醒：{1}（{2}）", DateTime.Now, text, ws[0].Entry.TargetName));
            manager.Add(dot);

            panel.LoadFromConfig();
            bridge.Start();
            ActGlobals.oFormActMain.OnLogLineRead += OnLogLineRead;
            tickTimer = new System.Threading.Timer(_ => OnTick(), null, 100, 100);

            Log("Ultimate Combat Helper v" + Version + " 已启动。配置文件：" + ConfigPath);
            statusLabel.Text = "已启动 v" + Version;
        }

        public void DeInitPlugin()
        {
            ActGlobals.oFormActMain.OnLogLineRead -= OnLogLineRead;
            if (tickTimer != null) { tickTimer.Dispose(); tickTimer = null; }
            if (manager != null) manager.DetachAll();
            if (bridge != null) bridge.Dispose();
            if (panel != null) panel.StopTimers();
            statusLabel.Text = "已停止";
        }

        private void OnLogLineRead(bool isImport, LogLineEventArgs logInfo)
        {
            if (isImport || !Config.General.Enabled || !ModuleManager.WantsType(logInfo.detectedType)) return;
            manager.Dispatch(LogLine.Parse(logInfo.originalLogLine, DateTime.Now));
        }

        private void OnTick()
        {
            try
            {
                var ctx = manager.Context;
                ctx.InCombat = ActGlobals.oFormActMain.InCombat;
                if (ctx.MyId.Length == 0)
                {
                    string id = bridge.GetPlayerId();
                    if (id != null) ctx.SetMyId(id); else ctx.MyName = ActGlobals.charName;
                }
                if (Config.General.Enabled) manager.Tick();
            }
            catch (Exception ex) { Log("定时器出错：" + ex.Message); }
        }

        private string GetCurrentTarget()
        {
            string t = bridge.GetCurrentTargetId(manager.Context.MyId);
            return (t == null || t == "E0000000") ? null : t;
        }

        /// <summary>界面修改配置后调用：保存并下发给各模块。</summary>
        internal void ApplyConfig()
        {
            try { ConfigStore.Save(ConfigPath, Config); }
            catch (Exception ex) { Log("保存配置失败：" + ex.Message); }
            manager.Notifier.Cooldown = TimeSpan.FromSeconds(Config.General.SpeechCooldownSeconds);
            dot.Configure(Config.Dot, Config.General.TestModeAnywhere);
        }

        internal void ReloadConfig()
        {
            Config = ConfigStore.Load(ConfigPath, Log);
            ApplyConfig();
            panel.LoadFromConfig();
            Log("配置已重新加载");
        }

        // ---- INotifier ----
        public void Speak(string text) { ActGlobals.oFormActMain.TTS(text); }
        public void Log(string text) { if (panel != null) panel.AppendLog(text); }
    }
}
