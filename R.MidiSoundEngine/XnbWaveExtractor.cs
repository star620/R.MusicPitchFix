using System;
using System.Collections.Generic;
using System.IO;
using NAudio.Wave;

namespace R.MidiSoundEngine;

/// <summary>
/// XNB 波形提取：从 Terraria 的 Content\Sounds\Item_XX.xnb 精确提取 PCM。
/// 布局按 FNA SoundEffectReader 验证过（与游戏真实解码一致）：
///   XNB头(10B) + reader名+版本+共享计数+类型索引 + formatLength(u32) + fmt(16B) + cbSize(2B) + dataLength(u32) + data
/// 已用真 XNA 播放验证：偏移 93 提取的数据 = 正确音色。
/// </summary>
public static class XnbWaveExtractor
{
    public static WaveFormat Format = null; // 由 FirstSound 初始化

    private static readonly Dictionary<int, (WaveFormat fmt, byte[] pcm)> Cache = new();

    /// <summary>提取指定 style（26/35/47）的原始 PCM 并缓存。</summary>
    public static (WaveFormat fmt, byte[] pcm) Get(int style)
    {
        if (Cache.TryGetValue(style, out var hit)) return hit;

        string file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Content", "Sounds", $"Item_{style}.xnb");
        if (!File.Exists(file))
            throw new FileNotFoundException("找不到音效文件: " + file);

        byte[] b = File.ReadAllBytes(file);
        if (b.Length < 40 || b[0] != 'X' || b[1] != 'N' || b[2] != 'B')
            throw new InvalidDataException("不是 XNB 文件: " + file);

        // 找 fmt 块特征：audioFormat=1(PCM), sampleRate=44100，声道数不固定（26/35 立体声、47 单声道）
        byte[] pat2 = { 0x01, 0x00, 0x02, 0x00, 0x44, 0xAC, 0x00, 0x00 }; // 立体声
        byte[] pat1 = { 0x01, 0x00, 0x01, 0x00, 0x44, 0xAC, 0x00, 0x00 }; // 单声道
        int f = -1;
        foreach (byte[] pat in new[] { pat2, pat1 })
        {
            for (int i = 0; i <= b.Length - pat.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < pat.Length; j++)
                    if (b[i + j] != pat[j]) { match = false; break; }
                if (match) { f = i; break; }
            }
            if (f >= 0) break;
        }
        if (f < 0) throw new InvalidDataException("未找到 44.1kHz PCM fmt 块: " + file);

        int channels = BitConverter.ToUInt16(b, f + 2);
        int rate = BitConverter.ToInt32(b, f + 4);
        int bits = BitConverter.ToUInt16(b, f + 14);

        // 数据长度在 fmt 起点 + 18（fmt16B + cbSize2B），数据在其后
        int dataLength = BitConverter.ToInt32(b, f + 18);
        int dataStart = f + 18 + 4;
        if (dataStart < 0 || dataStart + dataLength > b.Length)
            throw new InvalidDataException($"dataLength={dataLength} 越界（文件 {b.Length}B）");

        byte[] pcm = new byte[dataLength];
        Array.Copy(b, dataStart, pcm, 0, dataLength);

        var fmt = new WaveFormat(rate, bits, channels);
        Cache[style] = (fmt, pcm);
        return (fmt, pcm);
    }

    /// <summary>预加载 26/35/47，供启动诊断。</summary>
    public static void Preload()
    {
        foreach (int s in new[] { 26, 35, 47 })
        {
            try
            {
                var (fmt, pcm) = Get(s);
                R.PluginSwitch.Log.Info($"XnbWaveExtractor: style={s} {fmt.SampleRate}Hz {fmt.Channels}ch {fmt.BitsPerSample}bit {pcm.Length}B ok");
            }
            catch (Exception e)
            {
                R.PluginSwitch.Log.Info($"XnbWaveExtractor: style={s} 失败 {e.Message}");
            }
        }
    }
}