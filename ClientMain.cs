using System;
using R.PluginSwitch;
using R.PluginSwitch.Hooks;
using Terraria;

namespace R.MusicPitchFix;

/// <summary>
/// MIDI 音高污染 + 播放卡顿修复插件（客户端 · 最终版）。
///
/// 1) 音高归零：每帧 PreUpdate 把 Main.musicPitch 置 0（已验证有效）。
///
/// 2) 卡顿抑制（基于隔离测试结论：主因是 XNA 音频引擎总混音预算超载，
///    XACT 音乐/环境音与 MIDI 音符争抢同一混音线程）：
///    检测到 132 包（MIDI 音符）播放时自动把环境音音量压到 0，停止 1.5 秒后恢复。
///    不压音乐音量（避免与 RuYouMusicPlayer 的 MCI 音量联动冲突）、不静音其它声音。
///
/// 残余 harp 卡顿 = XNA 引擎播放 26.wav 样本的引擎级限制（控制实验确认），
/// 后续将由 NAudio 旁路方案单独处理，与本插件解耦。
/// </summary>
[Plugin(20260929, "MIDI音高修复", "MidiPlayer", "音高归零 + MIDI播放期间自动静音环境音", 0)]
public class ClientMain
{
    private static DateTime _lastMidi = DateTime.MinValue;
    private static readonly TimeSpan MidiInactiveAfter = TimeSpan.FromSeconds(1.5);

    private static bool _suppressing;
    private static float _userAmbient = 1f;

    static ClientMain()
    {
        Log.Info("MusicPitchFix 自动环境音抑制版已加载");
        GameHooks.PreUpdate.Register(OnPreUpdate);
        GameHooks.NetGetData.Register(OnNetGetData);
    }

    private static void OnNetGetData(HGetData e)
    {
        // 132 = PlayLegacySound，MIDI 音符走的就是这个包
        if (e.MSGType == 132)
        {
            _lastMidi = DateTime.UtcNow;
        }
    }

    private static void OnPreUpdate(HandledEventArgs e)
    {
        Main.musicPitch = 0f;

        bool midiActive = !Main.gameMenu && DateTime.UtcNow - _lastMidi < MidiInactiveAfter;
        if (midiActive)
        {
            if (!_suppressing)
            {
                _userAmbient = Main.ambientVolume; // 记住进入播放时的玩家设置
                _suppressing = true;
                Log.Info("MIDI 播放中：环境音静音");
            }
            Main.ambientVolume = 0f;
        }
        else if (_suppressing)
        {
            Main.ambientVolume = _userAmbient; // 恢复
            _suppressing = false;
            Log.Info($"MIDI 结束：环境音恢复 {_userAmbient:F2}");
        }
    }
}
