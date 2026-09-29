# R.MusicPitchFix

Terraria 1.4.5.x 原版客户端插件：修复 **MIDI 音乐播放时的音高污染**与**播放卡顿**。

> 这是一个运行在 **R-TA-RA2526 客户端插件框架**（`R.PluginSwitch`）之上的 `.rym` 客户端插件。
> 不需要玩家安装 Mod，把 `.rym` 放进游戏目录的 `Plugins\` 即可。

***

## 仓库结构

| 目录 | 插件 | 作用 |
|---|---|---|
| `/`（本目录） | `R.MusicPitchFix` | 音高归零 + MIDI 播放期间自动静音环境音 |
| `R.MidiSoundEngine/` | `R.MidiSoundEngine` | **竖琴/铃铛/吉他斧改走 NAudio 预重采样播放**，绕开 XNA 实时 SRC 卡顿（最终根治残余卡顿） |

两个插件建议**同时部署**：前者解决音高污染 + 环境音抢占混音预算的主因，后者把三件乐器从 XNA 音频线程旁路出去（残余 harp/bell 卡顿的根治方案）。

***

## 功能

1. **音高污染修复**：每帧把全局音高基准 `Main.musicPitch` 归零，消除"任何人手弹竖琴/铃铛/吉他斧后，MIDI 旋律音高被永久平移/钳平"的问题（无需重启客户端）。
2. **播放卡顿抑制**：检测到 MIDI 音符（132 号包）播放时，自动把环境音音量压到 0，播放停止 1.5 秒后恢复——复现"手动把环境音滑块关掉"的效果，大幅缓解密集世界中的可听卡顿。

***

## 根因分析（逆向结论）

### 1. 音高污染

- 客户端对竖琴(26)/铃铛(35)/吉他斧(47)三种音效，在 `LegacySoundPlayer` 创建音效实例时**强制** **`Pitch = Main.musicPitch`**（无视显式音高），再叠加 132 包携带的 `pitchOffset`。

- `Main.musicPitch` 是全局单值，会被**任意一次手弹**永久改写，写入源共 4 个：

  - 本人手弹竖琴/铃铛（`Player.cs:46932`）

  - 本人手弹吉他斧（`Player.cs:46969`）

  - 收到他人的 58 号包（`MessageBuffer.cs:2668`）

  - 音符弹射物（`Projectile.cs:25929`）

- 服务端 TShock 插件只能拦截"他人 58 包转发"，拦不住"玩家本人在本地写坏自己的 `musicPitch`"——所以必须客户端修。修复方式：每帧 `PreUpdate` 归零，手弹本身（同帧写→同帧播）不受影响。

### 2. 播放卡顿

实测证据链：

- 游戏 FPS 稳定 60，卡顿依旧 → 不是游戏线程帧预算问题；

- 乐器单实例化（限制并发 `SoundEffectInstance`）无效 → 不是实例堆积问题；

- **环境音+音乐音量归零后卡顿大幅缓解** → 主因是 **XNA 4.0 音频引擎总混音预算超载**：XACT（音乐/环境音 cue）与 MIDI 音符（XNA `SoundEffect`）争抢同一混音线程；

- 密集世界环境音 cue 多 → 容易过载；掏空世界 cue 少 → 有余量；

- **TerraAngel 客户端不卡，因为它换用了 FNA 引擎**（FAudio），混音能力与 XNA 不在一个量级。

结论：卡顿的主因可以在插件层缓解（自动静音环境音，即本插件做法）；残余的 harp 卡顿属于 XNA 引擎本身的限制，插件层无法根治，彻底解决只能换 FNA 客户端（如 TerraAngel）。

***

## 构建前提

| 依赖                        | 说明                                                                                                                  |
| ------------------------- | ------------------------------------------------------------------------------------------------------------------- |
| .NET SDK                  | 编译 .NET Framework 4.8 目标（引用程序集包会自动还原）                                                                               |
| `refs\Terraria.exe`       | Terraria 1.4.5.x 客户端主程序，从你的游戏目录复制                                                                                   |
| `refs\R.PluginSwitch.dll` | R-TA-RA2526 插件框架程序集。从 `Plugins\R.PluginSwitch.rym` 解压得到（`.rym` = Deflate 压缩的 DLL，可用本仓库 `tools\RymConverter.ps1` 解压） |
| XNA 4.0 运行时               | 原版 Windows 客户端使用，编译期解析 `Microsoft.Xna.Framework` 类型                                                                 |

## 构建与打包

```powershell
# 1. 放置引用程序集
#   把 Terraria.exe 和 R.PluginSwitch.dll 放进 refs\ 目录

# 2. 编译
dotnet build -c Release

# 3. 打包成 .rym（Deflate 压缩 DLL）
.\tools\RymConverter.ps1 -InputPath "bin\Release\net48\R.MusicPitchFix.dll" -Reverse

# 产物：R.MusicPitchFix.rym
```

### R.MidiSoundEngine（旁路音引擎）独立构建

```powershell
# 在子目录构建（引用 ../refs/ 中的程序集）
cd R.MidiSoundEngine
dotnet build -c Release

# 打包；并把 NAudio.dll 一并放进 Plugins\
..\tools\RymConverter.ps1 -InputPath "bin\Release\net48\R.MidiSoundEngine.dll" -Reverse
# 产物：R.MidiSoundEngine.rym（需与 bin\Release\net48\NAudio.dll 一起部署）
```

> 注意：`R.MidiSoundEngine` 依赖 **NAudio 1.10.0**（经典单 DLL，无多程序集拆分）。把 `NAudio.dll` 也放进 `Plugins\` 目录，框架的 AssemblyResolve 会自动解析。

## 部署

1. 把 `R.MusicPitchFix.rym` 放进 **游戏根目录** 的 `Plugins\`（与 `R.PluginSwitch.rym` 同目录）；
2. 若使用 MidiSoundEngine：同时放入 `R.MidiSoundEngine.rym` 和 `NAudio.dll`；
3. 启动游戏，插件随 R-TA-RA2526 框架自动加载；
4. 运行日志写入 `Plugins\R.PluginSwitch.log`。

## 验证

- **音高**：进服后自己或他人弹一次竖琴/铃铛/吉他斧，再播放 MIDI，旋律音高应保持正常；

- **卡顿**：在物块密集的世界播放 MIDI，日志应出现 `MIDI 播放中：环境音静音`（播放）与 `MIDI 结束：环境音恢复`（停止）。

***

## 免责声明

- 本插件依赖 **R-TA-RA2526 客户端插件框架**（`R.PluginSwitch`），该框架版权归原作者所有，本仓库**不包含**其代码；

- 仅供学习与个人使用，请在遵守游戏 EULA 的前提下使用。

## License

[MIT](LICENSE)
