using System;
using DotNetDetour;
using Microsoft.Xna.Framework.Audio;
using Terraria;
using Terraria.Audio;

namespace R.MidiSoundEngine;

/// <summary>
/// 拦截 SoundEngine.PlaySound（六参 int 重载，132 包与手弹都汇入此处）。
/// 26/35/47 是竖琴/铃铛/吉他斧 —— 卡顿重灾区，改走 NAudio 旁路。
/// 其它 style 原样放行。
/// </summary>
public class HSoundEngine : IMethodMonitor
{
    [Original]
    public static SoundEffectInstance OriginalPlaySound(int type, int x, int y, int Style, float volumeScale, float pitchOffset)
    {
        return null; // 仅在旁路不可用时实际调用，见下
    }

    public static bool UseBypass = false;

    [Monitor(typeof(SoundEngine), "PlaySound")]
    public static SoundEffectInstance PlaySound(int type, int x, int y, int Style, float volumeScale, float pitchOffset)
    {
        if (UseBypass && (Style == 26 || Style == 35 || Style == 47))
        {
            // 有效音高：musicPitch（已被归零）+ pitchOffset，钳制到 [-1,1]
            float pitch = Main.musicPitch + pitchOffset;
            if (pitch < -1f) pitch = -1f;
            if (pitch > 1f) pitch = 1f;
            BypassPlayer.Play(Style, pitch, volumeScale);
            return null; // 132 包 handler 不检查返回值，手弹也不检查
        }
        return OriginalPlaySound(type, x, y, Style, volumeScale, pitchOffset);
    }
}