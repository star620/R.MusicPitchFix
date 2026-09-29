using System;
using R.PluginSwitch;
using R.PluginSwitch.Hooks;

namespace R.MidiSoundEngine;

/// <summary>
/// MIDI 竖琴/铃铛/吉他斧卡顿根治插件（NAudio 旁路版）。
/// 原理：拦截 26/35/47 音符，改由 NAudio（独立音频线程）播放【预重采样】好的 PCM，
/// 完全绕开 XNA SoundEffect 实时重采样与混音线程 —— 卡顿根源。
/// 与 R.MusicPitchFix（音高归零+环境音抑制）互补使用。
/// </summary>
[Plugin(20260929, "MIDI旁路音引擎", "MidiPlayer", "harp/bell/guitaraxe 改走 NAudio 预重采样播放，绕开 XNA SRC 卡顿", 1)]
public class ClientMain
{
    static ClientMain()
    {
        try
        {
            Log.Info("MidiSoundEngine 加载");
            BypassPlayer.Init();
            if (BypassPlayer.Ready)
            {
                HSoundEngine.UseBypass = true;
                Log.Info("MidiSoundEngine 旁路已启用");
            }
            GameHooks.PreUpdate.Register(OnPreUpdate);
        }
        catch (Exception e)
        {
            Log.Info("MidiSoundEngine 初始化异常 " + e);
        }
    }

    private static void OnPreUpdate(HandledEventArgs e)
    {
        BypassPlayer.Update();
    }
}