using System;
using System.Collections.Generic;
using NAudio.Wave;

namespace R.MidiSoundEngine;

/// <summary>
/// 预重采样缓存：每个 (style, pitch) 只做一次线性插值重采样，
/// 之后播放零实时重采样 —— 这正是规避 XNA 实时 SRC 卡顿的核心。
/// pitch∈[-1,1]，pitch=1.0 = 升八度（频率×2）→ 数据变短、播放变快。
/// </summary>
public static class ResampleCache
{
    private static readonly Dictionary<long, byte[]> Cache = new();
    private static readonly object Gate = new();

    /// <summary>取 (style, pitch) 的预重采样 PCM。</summary>
    public static byte[] Get(int style, float pitch)
    {
        if (pitch < -1f) pitch = -1f;
        if (pitch > 1f) pitch = 1f;

        long key = ((long)style << 20) | (long)Math.Round(pitch * 300.0);
        lock (Gate)
        {
            if (Cache.TryGetValue(key, out var hit)) return hit;
            var (fmt, src) = XnbWaveExtractor.Get(style);
            // XNA pitch 语义：pitch=1.0 → 频率×2（升八度）→ 数据必须变短、播放变快。
            // 因此重采样倍率取 2^-pitch（输出长度 = 输入长度 × 2^-pitch）。
            var dst = Resample(src, fmt.Channels, Math.Pow(2.0, -pitch));
            Cache[key] = dst;
            return dst;
        }
    }

    private static byte[] Resample(byte[] pcm, int channels, double factor)
    {
        int bytesPerFrame = channels * 2; // 16bit
        int srcFrames = pcm.Length / bytesPerFrame;
        int dstFrames = (int)Math.Ceiling(srcFrames * factor);

        var src = new short[srcFrames * channels];
        Buffer.BlockCopy(pcm, 0, src, 0, pcm.Length);

        var dst = new short[dstFrames * channels];
        for (int d = 0; d < dstFrames; d++)
        {
            double pos = d / factor;
            int i0 = (int)pos;
            double frac = pos - i0;
            int a = Math.Min(i0, srcFrames - 1);
            int c = Math.Min(i0 + 1, srcFrames - 1);
            int baseOut = d * channels;
            for (int ch = 0; ch < channels; ch++)
            {
                double v0 = src[a * channels + ch];
                double v1 = src[c * channels + ch];
                dst[baseOut + ch] = (short)Math.Round(v0 + (v1 - v0) * frac);
            }
        }

        byte[] outBytes = new byte[dst.Length * 2];
        Buffer.BlockCopy(dst, 0, outBytes, 0, outBytes.Length);
        return outBytes;
    }
}