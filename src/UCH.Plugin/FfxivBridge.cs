// FfxivBridge.cs — 安全对接 FFXIV 解析插件 SDK（国服 _CN 版 / 国际服原版通用）
// 用途：1) 取当前角色 ID（ACT 晚于登录启动时收不到 02 行）  2) 取玩家当前目标，实现"仅当前目标"
// 所有出现 SDK 类型的代码都在 [NoInlining] 方法中，未安装解析插件时只会降级，不会让本插件加载失败。
using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using Advanced_Combat_Tracker;
using FFXIV_ACT_Plugin.Common;

namespace UltimateCombatHelper.Plugin
{
    public sealed class FfxivBridge : IDisposable
    {
        private readonly Action<string> log;
        private readonly Timer retryTimer = new Timer { Interval = 2000 };
        private object repository;   // IDataRepository
        private bool sdkMissing;

        public event Action Attached;

        public FfxivBridge(Action<string> log)
        {
            this.log = log;
            retryTimer.Tick += (s, e) => TryAttach();
        }

        public bool IsAttached { get { return repository != null; } }

        public void Start()
        {
            TryAttach();
            if (!IsAttached && !sdkMissing) retryTimer.Start();
        }

        private void TryAttach()
        {
            if (IsAttached || sdkMissing) { retryTimer.Stop(); return; }
            var data = ActGlobals.oFormActMain.ActPlugins.FirstOrDefault(p =>
                p.cbEnabled.Checked && p.pluginObj != null &&
                p.lblPluginTitle.Text.StartsWith("FFXIV_ACT_Plugin"));
            if (data == null) return;
            try
            {
                AttachImpl(data.pluginObj);
                retryTimer.Stop();
                log("已连接 FFXIV 解析插件：" + DescribeImpl());
                var h = Attached; if (h != null) h();
            }
            catch (FileNotFoundException)
            {
                sdkMissing = true; retryTimer.Stop();
                log("未找到 FFXIV 解析插件 SDK，「仅当前目标」模式与自动识别角色降级，其余功能正常");
            }
            catch (Exception ex) { log("连接解析插件失败：" + ex.Message); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void AttachImpl(object pluginObj)
        {
            repository = (IDataRepository)pluginObj.GetType().GetProperty("DataRepository").GetValue(pluginObj, null);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private string DescribeImpl()
        {
            var repo = (IDataRepository)repository;
            byte region = repo.GetGameRegion();   // 1 国际服, 2 国服, 3 韩服, 4 繁中服
            string r = region == 2 ? "国服" : region == 1 ? "国际服" : region == 3 ? "韩服" : region == 4 ? "繁中服" : "未知";
            return r + "，游戏版本 " + repo.GetGameVersion();
        }

        /// <summary>当前角色对象 ID（十六进制），取不到返回 null。</summary>
        public string GetPlayerId()
        {
            if (!IsAttached) return null;
            try { return GetPlayerIdImpl(); } catch (Exception) { return null; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private string GetPlayerIdImpl()
        {
            uint id = ((IDataRepository)repository).GetCurrentPlayerID();
            return id == 0 ? null : id.ToString("X");
        }

        /// <summary>玩家当前选中目标的 ID（十六进制）；无目标返回 "E0000000"；取不到返回 null（调用方应视为"不过滤"）。</summary>
        public string GetCurrentTargetId(string playerIdHex)
        {
            if (!IsAttached || string.IsNullOrEmpty(playerIdHex)) return null;
            try { return GetCurrentTargetIdImpl(playerIdHex); } catch (Exception) { return null; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private string GetCurrentTargetIdImpl(string playerIdHex)
        {
            uint pid = Convert.ToUInt32(playerIdHex, 16);
            var me = ((IDataRepository)repository).GetCombatantList().FirstOrDefault(c => c.ID == pid);
            if (me == null) return null;
            return me.TargetID == 0 ? "E0000000" : me.TargetID.ToString("X");
        }

        public void Dispose()
        {
            retryTimer.Stop();
            retryTimer.Dispose();
            repository = null;
        }
    }
}
