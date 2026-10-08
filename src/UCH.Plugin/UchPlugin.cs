// UchPlugin.cs — ACT 插件入口。负责：生命周期、把 ACT 日志行交给 ModuleManager、驱动定时器、承载界面。
// 业务逻辑全部在 UCH.Core（与 ACT 无关、可单元测试）；诊断信息的 ACT 侧收集在 UchPlugin.Diagnostics.cs。
using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Advanced_Combat_Tracker;
using UltimateCombatHelper.Config;
using UltimateCombatHelper.Data;
using UltimateCombatHelper.Diagnostics;
using UltimateCombatHelper.Logs;
using UltimateCombatHelper.Modules;
using UltimateCombatHelper.Modules.Dot;
using UltimateCombatHelper.Notify;
using UltimateCombatHelper.Plugin.UI;

[assembly: AssemblyTitle("FF14 Ultimate Combat Helper")]
[assembly: AssemblyDescription("FF14 战斗辅助（DoT 监控等），ACT 插件")]
[assembly: AssemblyProduct("UltimateCombatHelper")]
[assembly: AssemblyVersion("0.3.0.0")]
[assembly: AssemblyInformationalVersion("0.3.0")]

namespace UltimateCombatHelper.Plugin
{
    public sealed partial class UchPlugin : IActPluginV1, INotifier
    {
        private static readonly TimeSpan JobPollInterval = TimeSpan.FromSeconds(2);

        private Label statusLabel;
        private MainPanel panel;
        private FfxivBridge bridge;
        private ModuleManager manager;
        private DotModule dot;
        private DiagnosticsService diagnostics;
        private System.Threading.Timer tickTimer;
        private DateTime lastJobPoll = DateTime.MinValue;
        private bool wasInCombat;
        private int ticking;

        internal UchConfig Config { get; private set; }
        internal string ConfigPath { get; private set; }
        internal ModuleManager Manager { get { return manager; } }
        internal DotModule Dot { get { return dot; } }
        internal DiagnosticsService Diagnostics { get { return diagnostics; } }

        public static string Version
        {
            get { return typeof(UchPlugin).Assembly.GetName().Version.ToString(3); }
        }

        public void InitPlugin(TabPage pluginScreenSpace, Label pluginStatusText)
        {
            statusLabel = pluginStatusText;

            string appData = ActGlobals.oFormActMain.AppDataFolder.FullName;
            string dir = Path.Combine(appData, "Config");
            Directory.CreateDirectory(dir);
            ConfigPath = Path.Combine(dir, "UltimateCombatHelper.json");

            diagnostics = new DiagnosticsService { LogsDir = Path.Combine(appData, "UltimateCombatHelper", "logs"), Fill = FillReport };

            panel = new MainPanel(this) { Dock = DockStyle.Fill };
            pluginScreenSpace.Controls.Add(panel);
            pluginScreenSpace.Text = "战斗辅助";

            Config = ConfigStore.Load(ConfigPath, Log);
            diagnostics.Apply(Config.Diagnostics);

            bridge = new FfxivBridge(Log);
            manager = new ModuleManager(this, GetCurrentTarget, null, diagnostics.Log);
            manager.Notifier.Cooldown = TimeSpan.FromSeconds(Config.General.SpeechCooldownSeconds);
            manager.Context.MyName = ActGlobals.charName;
            bridge.Attached += () => manager.Context.SetMyId(bridge.GetPlayerId());
            WatchContext();

            dot = new DotModule(Config.Dot, Config.General.TestModeAnywhere);
            manager.Add(dot);

            panel.LoadFromConfig();
            bridge.Start();
            ActGlobals.oFormActMain.OnLogLineRead += OnLogLineRead;
            tickTimer = new System.Threading.Timer(_ => OnTick(), null, 100, 100);

            Log("Ultimate Combat Helper v" + Version + " 已启动。配置文件：" + ConfigPath);
            diagnostics.Log.Info("plugin", string.Format("ACT {0}，角色名「{1}」，测试模式 {2}，日志目录 {3}",
                ActGlobals.oFormActMain.GetType().Assembly.GetName().Version, ActGlobals.charName,
                Config.General.TestModeAnywhere ? "开" : "关", diagnostics.LogsDir));
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

        private void WatchContext()
        {
            var ctx = manager.Context;
            var log = diagnostics.Log;
            ctx.ZoneChanged += z => log.Info("context", string.Format("进入区域：{0}（ID {1}，难度：{2}）{3}",
                z.Id == 0 ? "未知" : z.NameCn, z.Id, ZoneTable.DisplayName(z.Tier),
                !Config.General.TestModeAnywhere && !Config.Dot.TierAllowed(z.Tier) ? "——此难度不在 DoT 提醒列表，不会播报" : ""));
            ctx.PlayerChanged += id => log.Info("context", "角色 ID：" + id);
            ctx.JobChanged += (job, src) => log.Info("context", string.Format("当前职业：{0}（来源：{1}）", job == Jobs.Unknown ? "未知" : Jobs.Describe(job), src));
        }

        private void OnLogLineRead(bool isImport, LogLineEventArgs logInfo)
        {
            if (isImport) return;
            try
            {
                string raw = logInfo.originalLogLine;
                int type = LogLine.PeekType(raw);   // 不依赖 detectedType：按行首类型号过滤
                diagnostics.Lines.Observe(logInfo.detectedType, type, logInfo.logLine, raw);
                NoteLineFormat(logInfo.detectedType, type, logInfo.logLine);
                if (type < 0 || !Config.General.Enabled) return;
                bool dispatch = ModuleManager.WantsType(type);
                if (!dispatch && !LineRecorder.RecordTypes.Contains(type)) return;
                var line = LogLine.Parse(raw, DateTime.Now);
                if (dispatch) manager.Dispatch(line);
                diagnostics.Lines.Offer(line, raw, manager.Context.MyId);
            }
            catch (Exception ex) { NoteLineError(ex, logInfo); }
        }

        private void OnTick()
        {
            if (System.Threading.Interlocked.Exchange(ref ticking, 1) == 1) return;   // 上一次还没跑完（SDK/ACT 调用慢）就跳过
            try
            {
                var ctx = manager.Context;
                bool inCombat = ActGlobals.oFormActMain.InCombat;
                ctx.InCombat = inCombat;
                if (inCombat != wasInCombat)
                {
                    wasInCombat = inCombat;
                    if (inCombat) ResetHeartbeat();
                    if (inCombat) diagnostics.CombatStarted(ctx.Zone.Id == 0 ? "未知区域" : ctx.Zone.NameCn, ZoneTable.DisplayName(ctx.Zone.Tier), Jobs.Describe(ctx.MyJob));
                    else diagnostics.CombatEnded(EnrichFight);
                }
                if (ctx.MyId.Length == 0)
                {
                    string id = bridge.GetPlayerId();
                    if (id != null) ctx.SetMyId(id); else ctx.MyName = ActGlobals.charName;
                }
                PollJob();
                Heartbeat();
                if (Config.General.Enabled) manager.Tick();
            }
            catch (Exception ex)
            {
                Log("定时器出错：" + ex.Message);
                diagnostics.Log.Error("plugin", "定时器出错：" + ex);
            }
            finally { System.Threading.Interlocked.Exchange(ref ticking, 0); }
        }

        /// <summary>解析插件 SDK 能直接给出职业；没有 SDK 时靠 03/12 行（CombatContext 里处理）。</summary>
        private void PollJob()
        {
            var ctx = manager.Context;
            DateTime now = DateTime.Now;
            if (ctx.MyId.Length == 0 || now - lastJobPoll < JobPollInterval) return;
            lastJobPoll = now;
            int? job = bridge.GetPlayerJob(ctx.MyId);
            if (job.HasValue && job.Value > 0) ctx.SetMyJob(job.Value, "SDK");
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
            diagnostics.Apply(Config.Diagnostics);
        }

        internal void ReloadConfig()
        {
            Config = ConfigStore.Load(ConfigPath, Log);
            ApplyConfig();
            panel.LoadFromConfig();
            Log("配置已重新加载");
        }

        // ---- INotifier ----
        /// <summary>切到 ACT 界面线程再调 TTS，与「试听」按钮走完全相同的路径（定时器线程直接调用在部分 TTS 设置下可能无声）。</summary>
        public void Speak(string text)
        {
            var form = ActGlobals.oFormActMain;
            try
            {
                if (form.InvokeRequired) form.BeginInvoke(new Action<string>(SpeakOnUiThread), text);
                else SpeakOnUiThread(text);
            }
            catch (Exception ex) { diagnostics.Log.Error("tts", "提交 TTS 失败：" + ex.Message); }
        }

        private void SpeakOnUiThread(string text)
        {
            try
            {
                ActGlobals.oFormActMain.TTS(text);
                diagnostics.Log.Debug("tts", "已交给 ACT TTS：" + text);
            }
            catch (Exception ex) { diagnostics.Log.Error("tts", "ACT TTS 出错：" + ex); }
        }

        public void Log(string text)
        {
            if (diagnostics != null) diagnostics.Log.Info("plugin", text);
        }
    }
}
