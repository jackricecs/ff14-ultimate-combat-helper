using System;
using System.Collections.Generic;

namespace UltimateCombatHelper.Notify
{
    /// <summary>提醒输出。插件里实现为 ACT TTS；测试里实现为记录列表。</summary>
    public interface INotifier
    {
        void Speak(string text);
        void Log(string text);
    }

    /// <summary>同一个 key 在冷却时间内只播一次，避免多模块/多行同时触发刷屏。</summary>
    public sealed class ThrottledNotifier : INotifier
    {
        private readonly INotifier inner;
        private readonly Func<DateTime> clock;
        private readonly Dictionary<string, DateTime> last = new Dictionary<string, DateTime>();
        public TimeSpan Cooldown = TimeSpan.FromSeconds(1.5);

        public ThrottledNotifier(INotifier inner, Func<DateTime> clock = null)
        {
            this.inner = inner;
            this.clock = clock ?? (() => DateTime.Now);
        }

        public void Speak(string text) { Speak(text, text); }

        public bool Speak(string text, string key)
        {
            if (string.IsNullOrEmpty(text)) return false;
            DateTime now = clock(), t;
            lock (last)
            {
                if (last.TryGetValue(key, out t) && now - t < Cooldown) return false;
                last[key] = now;
            }
            inner.Speak(text);
            return true;
        }

        public void Log(string text) { inner.Log(text); }
    }
}
