using System;
using R.PluginSwitch;
using R.PluginSwitch.Hooks;
using Terraria;

namespace R.MusicPitchFix;

/// <summary>
/// MIDI 音高污染 + 播放卡顿修复插件（客户端 · 自动环境音抑制版）。
///
/// 1) 音高归零：每帧 PreUpdate 把 Main.musicPitch 置 0（已验证有效）。
///
/// 2) 卡顿抑制（基于隔离测试结论）：
///    原版 XNA 客户端里，XACT 音乐/环境音 cue 与 MIDI 音符（XNA SoundEffect）争抢
///    同一音频混音预算。实测把环境音+音乐音量归零后卡顿大幅缓解。
///    本插件自动检测：客户端收到 132 包（PlayLegacySound，MIDI 音符）即视为"播放中"，
///    期间强制 Main.ambientVolume = 0；停止后恢复玩家原设置。
///    仅抑制环境音（不动音乐音量，避免与 RuYouMusicPlayer 的 MCI 音量联动冲突）。
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
