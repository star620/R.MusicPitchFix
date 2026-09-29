using System;
using System.Collections.Generic;
using NAudio.Wave;
using Terraria;

namespace R.MidiSoundEngine;

/// <summary>
/// NAudio 旁路播放器：用少量常驻 WaveOutEvent 通道播放预重采样 PCM，
/// 完全绕开 XNA SoundEffect 实时重采样与混音线程，消除 harp/bell 卡顿。
/// </summary>
public static class BypassPlayer
{
    private class Channel
    {
        public readonly WaveOutEvent Output = new WaveOutEvent();
        public bool Busy;
        public int Style;
        public long StartedTick;
    }

    private static readonly List<Channel> Channels = new List<Channel>();
    private static readonly object Gate = new object();
    private static int _next;

    public static bool Ready { get; private set; }
    private static string _error = "";

    public static void Init()
    {
        try
        {
            XnbWaveExtractor.Preload();
            for (int i = 0; i < 4; i++) // 4 个常驻通道，足够 MIDI 常见和声
            {
                var c = new Channel();
                c.Output.DesiredLatency = 400;
                c.Output.NumberOfBuffers = 3;
                Channels.Add(c);
            }
            Ready = true;
            R.PluginSwitch.Log.Info($"BypassPlayer: 初始化成功（{Channels.Count} 通道）");
        }
        catch (Exception e)
        {
            _error = e.Message;
            Ready = false;
            R.PluginSwitch.Log.Info("BypassPlayer: 初始化失败 " + e.Message);
        }
    }

    /// <summary>播放一个旁路音符。</summary>
    public static void Play(int style, float pitch, float volumeScale)
    {
        if (!Ready) return;
        try
        {
            var (fmt, _) = XnbWaveExtractor.Get(style);
            byte[] pcm = ResampleCache.Get(style, pitch);
            if (pcm == null || pcm.Length == 0) return;

            // waveOut 设备不自动转换声道数：单声道样本复制成立体声再播放
            var outFmt = new WaveFormat(fmt.SampleRate, 16, 2);
            if (fmt.Channels == 1)
            {
                int frames = pcm.Length / 2;
                var exp = new byte[frames * 4];
                for (int fi = 0; fi < frames; fi++)
                {
                    exp[fi * 4] = pcm[fi * 2];
                    exp[fi * 4 + 1] = pcm[fi * 2 + 1];
                    exp[fi * 4 + 2] = pcm[fi * 2];
                    exp[fi * 4 + 3] = pcm[fi * 2 + 1];
                }
                pcm = exp;
            }

            // 简单增益：volumeScale * 0.75（对齐 XNA 26/35/47 的 0.75 规则）* 全局音量
            float vol = volumeScale * 0.75f * Main.soundVolume;
            if (vol > 1f) vol = 1f;
            if (vol <= 0f) return;

            Channel ch = Acquire(style);
            lock (ch)
            {
                try
                {
                    ch.Output.Stop();
                }
                catch { }
                var rs = new RawSourceWaveStream(new System.IO.MemoryStream(pcm, false), outFmt);
                var volStream = new VolumeWaveProvider16(rs) { Volume = vol };
                ch.Output.Init(volStream);
                ch.Busy = true;
                ch.Style = style;
                ch.StartedTick = Environment.TickCount;
                ch.Output.Play();
            }
        }
        catch (Exception e)
        {
            R.PluginSwitch.Log.Info($"BypassPlayer.Play: {style} pitch={pitch} 异常 {e.Message}");
        }
    }

    /// <summary>收割已播完的通道（供 PreUpdate 调用）。</summary>
    public static void Update()
    {
        if (!Ready) return;
        long now = Environment.TickCount;
        foreach (var c in Channels)
        {
            if (c.Busy && now - c.StartedTick > 2000) // 最长样本 ~0.9s，2s 保险回收
            {
                c.Busy = false;
                c.Style = 0;
            }
        }
    }

    private static Channel Acquire(int style)
    {
        lock (Gate)
        {
            // 优先复用同 style 的空闲通道
            for (int i = 0; i < Channels.Count; i++)
            {
                var c = Channels[_next % Channels.Count];
                if (!c.Busy) { _next++; return c; }
                _next++;
            }
            // 全忙：抢占最旧
            Channel oldest = Channels[0];
            for (int i = 1; i < Channels.Count; i++)
                if (Channels[i].StartedTick < oldest.StartedTick) oldest = Channels[i];
            return oldest;
        }
    }
}