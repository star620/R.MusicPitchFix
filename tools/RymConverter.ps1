<#
.SYNOPSIS
  R-TA-RA2526 客户端插件格式转换工具

  .rym = Deflate 压缩的 .NET 程序集（PluginManager 加载时会先解压再 Assembly.Load）
  .dll = 未压缩的原始程序集（加载器直接 Assembly.Load，效果等价）

  默认模式：.rym -> .dll（解压）
  加 -Reverse：.dll -> .rym（压缩，即"打包"）

.EXAMPLE
  # 把当前目录下所有 .rym 解压成 .dll
  .\RymConverter.ps1

  # 指定输入目录 / 输出目录
  .\RymConverter.ps1 -InputPath "Plugins" -OutputPath "out"

  # 只转换单个文件
  .\RymConverter.ps1 -InputPath "Plugins\R.RuYouMusicPlayer.rym"

  # 反向打包：把编译好的 R.Fix.dll 压缩成 R.Fix.rym
  .\RymConverter.ps1 -InputPath "Plugins\R.Fix.dll" -Reverse

  # 允许覆盖已存在的输出文件
  .\RymConverter.ps1 -Force
#>
param(
    [string]$InputPath  = "",
    [string]$OutputPath = "",
    [switch]$Reverse,
    [switch]$Force
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression

$wantExt = if ($Reverse) { ".dll" } else { ".rym" }

# ---------- 1. 确定输入文件列表 ----------
if ([string]::IsNullOrWhiteSpace($InputPath)) {
    $InputPath = (Get-Location).Path
}
$item = Get-Item $InputPath
$files = @()
if ($item.PSIsContainer) {
    $pattern = if ($Reverse) { "*.dll" } else { "*.rym" }
    $files = @(Get-ChildItem -LiteralPath $item.FullName -Filter $pattern -File)
} else {
    $files = @($item)
}
if ($files.Count -eq 0) {
    Write-Host "未找到任何 $wantExt 文件：$InputPath" -ForegroundColor Yellow
    exit 1
}

# ---------- 2. 确定输出目录 ----------
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = $files[0].DirectoryName
}
$OutputDir = [System.IO.Path]::GetFullPath($OutputPath)
[System.IO.Directory]::CreateDirectory($OutputDir) | Out-Null

# ---------- 3. 逐个转换 ----------
$ok = 0; $fail = 0; $skip = 0
foreach ($f in $files) {
    $baseName = [System.IO.Path]::GetFileNameWithoutExtension($f.Name)
    $outExt   = if ($Reverse) { ".rym" } else { ".dll" }
    $outPath  = Join-Path $OutputDir ($baseName + $outExt)

    try {
        if ((Test-Path -LiteralPath $outPath -PathType Leaf) -and -not $Force) {
            Write-Warning "已存在，跳过（加 -Force 覆盖）：$outPath"
            $skip++
            continue
        }

        $raw = [System.IO.File]::ReadAllBytes($f.FullName)

        if ($Reverse) {
            # DLL -> RYM：Deflate 压缩（Loader 用 DeflateStream 解压，必须是无头 raw deflate）
            $ms = [System.IO.MemoryStream]::new()
            $ds = [System.IO.Compression.DeflateStream]::new($ms, [System.IO.Compression.CompressionMode]::Compress)
            $ds.Write($raw, 0, $raw.Length)
            $ds.Dispose()
            $bytes = $ms.ToArray()
        } else {
            # RYM -> DLL：Deflate 解压，并校验 MZ 头（0x4D 0x5A）
            $in  = [System.IO.MemoryStream]::new($raw)
            $out = [System.IO.MemoryStream]::new()
            $ds  = [System.IO.Compression.DeflateStream]::new($in, [System.IO.Compression.CompressionMode]::Decompress)
            $ds.CopyTo($out)
            $ds.Dispose()
            $bytes = $out.ToArray()
            if ($bytes.Length -lt 2 -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) {
                throw "解压结果不是有效的 .NET 程序集（缺少 MZ 头）"
            }
        }

        [System.IO.File]::WriteAllBytes($outPath, $bytes)
        Write-Host ("{0,-32} -> {1,-32}  {2,7} -> {3,7} 字节" -f $f.Name, (Split-Path $outPath -Leaf), $raw.Length, $bytes.Length) -ForegroundColor Green
        $ok++
    } catch {
        Write-Host ("失败：{0}  {1}" -f $f.Name, $_.Exception.Message) -ForegroundColor Red
        $fail++
    }
}

Write-Host ""
Write-Host ("完成：成功 {0} 个，跳过 {1} 个，失败 {2} 个" -f $ok, $skip, $fail) -ForegroundColor Cyan
