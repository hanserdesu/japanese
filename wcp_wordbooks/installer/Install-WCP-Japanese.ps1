$ErrorActionPreference = 'Stop'

function Write-Step([string]$message) {
    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] $message" -ForegroundColor Cyan
}

function Fail([string]$message) {
    throw "错误: $message"
}

function Download-WithProgress([string]$uri, [string]$destination, [string]$displayName, [int64]$expectedSize) {
    $request = $null
    $response = $null
    $inputStream = $null
    $outputStream = $null
    try {
        $request = [Net.WebRequest]::Create($uri)
        $request.Method = 'GET'
        $request.Timeout = 60000
        $request.ReadWriteTimeout = 60000
        $request.Proxy = [Net.WebRequest]::DefaultWebProxy
        if ($request.Proxy) { $request.Proxy.Credentials = [Net.CredentialCache]::DefaultCredentials }
        $response = $request.GetResponse()
        $total = [int64]$response.ContentLength
        if ($total -le 0) { $total = $expectedSize }
        $inputStream = $response.GetResponseStream()
        $outputStream = [IO.File]::Open($destination, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
        $buffer = New-Object byte[] (1024 * 1024)
        $downloaded = [int64]0
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $lastUpdate = [datetime]::MinValue
        while (($read = $inputStream.Read($buffer, 0, $buffer.Length)) -gt 0) {
            $outputStream.Write($buffer, 0, $read)
            $downloaded += $read
            $now = Get-Date
            if (($now - $lastUpdate).TotalMilliseconds -ge 250 -or ($total -gt 0 -and $downloaded -ge $total)) {
                $lastUpdate = $now
                $percent = if ($total -gt 0) { [Math]::Min(100, [int](($downloaded * 100) / $total)) } else { 0 }
                $speed = if ($watch.Elapsed.TotalSeconds -gt 0) { $downloaded / 1MB / $watch.Elapsed.TotalSeconds } else { 0 }
                $status = if ($total -gt 0) {
                    "$percent%  $([Math]::Round($downloaded / 1MB, 1)) / $([Math]::Round($total / 1MB, 1)) MB  $([Math]::Round($speed, 2)) MB/s"
                } else {
                    "$([Math]::Round($downloaded / 1MB, 1)) MB  $([Math]::Round($speed, 2)) MB/s"
                }
                Write-Progress -Activity "下载 $displayName" -Status $status -PercentComplete $percent
            }
        }
        $outputStream.Flush()
        Write-Progress -Activity "下载 $displayName" -Completed
    } finally {
        if ($outputStream) { $outputStream.Dispose() }
        if ($inputStream) { $inputStream.Dispose() }
        if ($response) { $response.Dispose() }
    }
}

function Ensure-ZipExtractor {
    if ('WcpJapaneseZipSession' -as [type]) { return }
    Add-Type -AssemblyName System.IO.Compression -ErrorAction Stop
    Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction Stop
    $zipReferences = @('System.dll', 'System.Core.dll',
        'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll')
    $zipSource = @'
using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

public sealed class WcpJapaneseZipSession
{
    public Task Task;
    public volatile bool TotalReady;
    public int TotalFiles;
    public long TotalBytes;
    public int CompletedFiles;
    public long CompletedBytes;

    private readonly object ErrorLock = new object();
    private string ErrorText;

    public static WcpJapaneseZipSession Start(string zipPath, string destination, int workerCount)
    {
        WcpJapaneseZipSession session = new WcpJapaneseZipSession();
        session.CompletedFiles = 0;
        session.CompletedBytes = 0;
        session.TotalFiles = 0;
        session.TotalBytes = 0;
        session.TotalReady = false;
        session.Task = Task.Factory.StartNew(
            () => session.Extract(zipPath, destination, workerCount),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        return session;
    }

    public string GetError()
    {
        lock (ErrorLock) { return ErrorText; }
    }

    private void Extract(string zipPath, string destination, int workerCount)
    {
        ErrorText = null;
        string root = Path.GetFullPath(destination);
        if (!root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            root += Path.DirectorySeparatorChar;

        string[] names;
        using (ZipArchive archive = ZipFile.OpenRead(zipPath))
        {
            int nameCount = 0;
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (!String.IsNullOrEmpty(entry.Name)) nameCount++;
            }
            names = new string[nameCount];
            int nameIndex = 0;
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (!String.IsNullOrEmpty(entry.Name)) names[nameIndex++] = entry.FullName;
            }
            TotalFiles = names.Length;
            long total = 0;
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (!String.IsNullOrEmpty(entry.Name)) total += entry.Length;
            }
            TotalBytes = total;
            TotalReady = true;
        }

        int workers = Math.Max(1, Math.Min(workerCount, Math.Max(1, names.Length)));
        Task[] tasks = new Task[workers];
        for (int worker = 0; worker < workers; worker++)
        {
            int workerIndex = worker;
            tasks[worker] = Task.Factory.StartNew(
                () => ExtractWorker(zipPath, root, names, workerIndex, workers),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }
        try
        {
            Task.WaitAll(tasks);
        }
        catch (Exception ex)
        {
            SetError(ex.ToString());
            throw;
        }
    }

    private void ExtractWorker(string zipPath, string root, string[] names,
                               int workerIndex, int workerCount)
    {
        try
        {
            using (ZipArchive archive = ZipFile.OpenRead(zipPath))
            {
                for (int i = workerIndex; i < names.Length; i += workerCount)
                {
                    ZipArchiveEntry entry = archive.GetEntry(names[i]);
                    if (entry == null) throw new InvalidDataException("ZIP entry disappeared: " + names[i]);
                    string relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                    string target = Path.GetFullPath(Path.Combine(root, relative));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Unsafe ZIP path: " + entry.FullName);
                    string parent = Path.GetDirectoryName(target);
                    if (!String.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                    using (Stream input = entry.Open())
                    using (FileStream output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        input.CopyTo(output);
                    }
                    Interlocked.Increment(ref CompletedFiles);
                    Interlocked.Add(ref CompletedBytes, entry.Length);
                }
            }
        }
        catch (Exception ex)
        {
            SetError(ex.ToString());
            throw;
        }
    }

    private void SetError(string text)
    {
        lock (ErrorLock) { if (ErrorText == null) ErrorText = text; }
    }
}
'@
    if ($PSVersionTable.PSEdition -eq 'Core') {
        Add-Type -TypeDefinition $zipSource -Language CSharp -ErrorAction Stop
    } else {
        Add-Type -TypeDefinition $zipSource -Language CSharp -ReferencedAssemblies $zipReferences -ErrorAction Stop
    }
}

function Expand-ZipWithProgress([string]$zipPath, [string]$destination, [string]$displayName) {
    Ensure-ZipExtractor
    $workers = [Math]::Max(2, [Math]::Min(4, [Environment]::ProcessorCount))
    $session = [WcpJapaneseZipSession]::Start($zipPath, $destination, $workers)
    while (-not $session.TotalReady -and -not $session.Task.IsCompleted) {
        Write-Progress -Activity "解压 $displayName" -Status '正在读取资源清单...' -PercentComplete 0
        Start-Sleep -Milliseconds 100
    }
    $task = $session.Task
    $totalFiles = $session.TotalFiles
    $totalBytes = $session.TotalBytes
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while (-not $task.IsCompleted) {
        $doneFiles = [Math]::Min($session.CompletedFiles, $totalFiles)
        $doneBytes = [Math]::Min($session.CompletedBytes, $totalBytes)
        $percent = if ($totalFiles -gt 0) { [Math]::Min(100, [int](($doneFiles * 100) / $totalFiles)) } else { 100 }
        $speed = if ($watch.Elapsed.TotalSeconds -gt 0) { $doneBytes / 1MB / $watch.Elapsed.TotalSeconds } else { 0 }
        $status = "$percent%  文件 $doneFiles / $totalFiles  $([Math]::Round($doneBytes / 1MB, 1)) / $([Math]::Round($totalBytes / 1MB, 1)) MB  $([Math]::Round($speed, 2)) MB/s  并行线程 $workers"
        Write-Progress -Activity "解压 $displayName" -Status $status -PercentComplete $percent
        Start-Sleep -Milliseconds 250
    }
    $task.GetAwaiter().GetResult()
    $finalStatus = "100%  文件 $totalFiles / $totalFiles  $([Math]::Round($totalBytes / 1MB, 1)) / $([Math]::Round($totalBytes / 1MB, 1)) MB"
    Write-Progress -Activity "解压 $displayName" -Status $finalStatus -PercentComplete 100
    Write-Progress -Activity "解压 $displayName" -Completed
}

Write-Step '开始安装 WCP 日语词书。'

function Add-Candidate([System.Collections.Generic.List[string]]$list, [string]$path) {
    if ([string]::IsNullOrWhiteSpace($path)) { return }
    try { $full = [IO.Path]::GetFullPath($path).TrimEnd('\') } catch { return }
    if (-not $list.Contains($full)) { $list.Add($full) }
}

function Get-SteamRoots {
    $roots = New-Object 'System.Collections.Generic.List[string]'
    foreach ($hive in @('HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam', 'HKLM:\SOFTWARE\Valve\Steam')) {
        try {
            $item = Get-ItemProperty -Path $hive -ErrorAction Stop
            Add-Candidate $roots ([string]$item.SteamPath)
            Add-Candidate $roots ([string]$item.InstallPath)
        } catch {}
    }
    $initial = @($roots)
    foreach ($root in $initial) {
        if (-not [IO.Directory]::Exists($root)) {
            Write-Host "跳过不存在的 Steam 路径：$root" -ForegroundColor DarkYellow
            continue
        }
        $vdf = Join-Path $root 'steamapps\libraryfolders.vdf'
        if (-not (Test-Path -LiteralPath $vdf)) { continue }
        try {
            $raw = Get-Content -LiteralPath $vdf -Raw -ErrorAction Stop
            foreach ($m in [regex]::Matches($raw, '"path"\s*"([^"]+)"')) {
                Add-Candidate $roots ($m.Groups[1].Value -replace '\\\\','\')
            }
        } catch {}
    }
    return @($roots | Where-Object { [IO.Directory]::Exists($_) })
}

function Get-GameCandidates {
    $list = New-Object 'System.Collections.Generic.List[string]'
    Add-Candidate $list $env:WCP_GAME_DIR
    foreach ($root in (Get-SteamRoots)) {
        if (-not [IO.Directory]::Exists($root)) { continue }
        Add-Candidate $list (Join-Path $root 'steamapps\common\WCP-WordGirlgriend')
    }
    foreach ($root in @('C:\Program Files (x86)\Steam\steamapps\common', 'C:\Program Files\Steam\steamapps\common')) {
        Add-Candidate $list (Join-Path $root 'WCP-WordGirlgriend')
    }
    $valid = New-Object 'System.Collections.Generic.List[string]'
    foreach ($candidate in @($list)) {
        if (-not [IO.Directory]::Exists($candidate)) {
            if ($candidate -match '^[A-Za-z]:') {
                Write-Host "跳过不存在的游戏路径：$candidate" -ForegroundColor DarkYellow
            }
            continue
        }
        $managed = Join-Path $candidate 'wcp_Data\Managed\Assembly-CSharp.dll'
        $database = Join-Path $candidate 'wcp_Data\StreamingAssets\wcpFullEng.db'
        if ((Test-Path -LiteralPath $managed) -and (Test-Path -LiteralPath $database)) {
            $valid.Add($candidate)
        }
    }
    return @($valid)
}

function Get-LogGamePath {
    $log = Join-Path $env:USERPROFILE 'AppData\LocalLow\WCP\wcp\Player.log'
    if (-not (Test-Path -LiteralPath $log)) { return $null }
    try {
        $raw = Get-Content -LiteralPath $log -Raw -ErrorAction Stop
        $m = [regex]::Match($raw, "Mono path\[0\] = '([^']+?)[/]wcp_Data[/]Managed'")
        if ($m.Success) { return (($m.Groups[1].Value -replace '/', '\').TrimEnd('\')) }
    } catch {}
    return $null
}

if (Get-Process -Name 'wcp' -ErrorAction SilentlyContinue) {
    Fail '检测到万词破正在运行，请完全退出游戏后再安装。'
}

Write-Step '正在搜索 Steam 游戏目录。'
$candidates = @(Get-GameCandidates)
if ($candidates.Count -eq 0) {
    Fail '没有找到有效的万词破安装目录。请确认 Steam 已安装游戏，或设置 WCP_GAME_DIR 后重试。'
}

$logPath = Get-LogGamePath
$game = $candidates | Where-Object { $_ -eq $logPath } | Select-Object -First 1
if (-not $game) { $game = $candidates | Select-Object -First 1 }
if ($candidates.Count -gt 1 -and -not ($candidates -contains $logPath)) {
    Write-Host '检测到多个有效游戏目录：'
    for ($i = 0; $i -lt $candidates.Count; $i++) { Write-Host "[$($i + 1)] $($candidates[$i])" }
    $choice = Read-Host '请输入要安装的序号（默认 1）'
    if ($choice -match '^[1-9][0-9]*$' -and [int]$choice -le $candidates.Count) { $game = $candidates[[int]$choice - 1] }
}

Write-Step "已找到游戏目录：$game"

$bepRoot = Join-Path $game 'BepInEx'
$payload = Join-Path $PSScriptRoot 'payload'
$plugins = Join-Path $payload 'plugins'
$data = Join-Path $env:USERPROFILE 'AppData\LocalLow\WCP\wcp'
New-Item -ItemType Directory -Force -Path $data | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$backup = Join-Path $data "jpmod_backups\$stamp"
New-Item -ItemType Directory -Force -Path $backup | Out-Null

$bepPayload = Join-Path $payload 'bepinex'
$bepCore = Join-Path $bepRoot 'core\BepInEx.dll'
$bepRootFiles = @('.doorstop_version', 'BepInEx-changelog.txt', 'doorstop_config.ini', 'winhttp.dll')
$needBepFramework = -not (Test-Path -LiteralPath $bepCore)
$needBepBootstrap = @($bepRootFiles | Where-Object { -not (Test-Path -LiteralPath (Join-Path $game $_)) }).Count -gt 0
if ($needBepFramework -or $needBepBootstrap) {
    if (-not (Test-Path -LiteralPath (Join-Path $bepPayload 'BepInEx\core\BepInEx.dll'))) {
        Fail '安装包缺少内置 BepInEx 运行环境。'
    }
    if (Test-Path -LiteralPath $bepRoot) {
        Copy-Item -LiteralPath $bepRoot -Destination (Join-Path $backup 'BepInEx-before-install') -Recurse -Force
    }
    foreach ($name in $bepRootFiles) {
        $existing = Join-Path $game $name
        if (Test-Path -LiteralPath $existing) {
            Copy-Item -LiteralPath $existing -Destination (Join-Path $backup $name) -Force
        }
    }
    foreach ($name in $bepRootFiles) {
        $src = Join-Path $bepPayload "root\$name"
        if (-not (Test-Path -LiteralPath $src)) { Fail "安装包缺少 BepInEx 启动文件：$name" }
        Copy-Item -LiteralPath $src -Destination (Join-Path $game $name) -Force
    }
    if ($needBepFramework) {
        New-Item -ItemType Directory -Force -Path $bepRoot | Out-Null
        Copy-Item -Path (Join-Path $bepPayload 'BepInEx\*') -Destination $bepRoot -Recurse -Force
        Write-Host '已安装内置 BepInEx 5 运行环境。'
    } else {
        Write-Host '已补齐 BepInEx 启动文件，保留现有 BepInEx 核心。'
    }
}

Write-Step '运行环境检查完成，正在读取资源清单。'

$releaseManifestPath = Join-Path $PSScriptRoot 'release-manifest.json'
if (-not (Test-Path -LiteralPath $releaseManifestPath)) { Fail '安装包缺少 release-manifest.json。' }
$release = Get-Content -LiteralPath $releaseManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $release.assets) { Fail 'release-manifest.json 无效。' }
$baseUrls = @($release.base_urls)
if ($baseUrls.Count -eq 0 -and $release.base_url) { $baseUrls = @($release.base_url) }
if ($baseUrls.Count -eq 0) { Fail 'release-manifest.json 没有资源下载地址。' }

function Download-VerifiedAsset($asset) {
    if (-not $asset.name -or -not $asset.sha256 -or -not $asset.size) { Fail 'Release 资源清单缺少文件信息。' }
    $free = (Get-PSDrive -Name ([IO.Path]::GetPathRoot($data).TrimEnd('\').Substring(0,1))).Free
    if ($free -lt [int64]$asset.size) { Fail "磁盘空间不足，至少需要 $([math]::Ceiling($asset.size / 1GB)) GB 可用空间。" }
    $downloadDir = Join-Path $data 'jpmod_downloads'
    New-Item -ItemType Directory -Force -Path $downloadDir | Out-Null
    $target = Join-Path $downloadDir $asset.name
    $valid = $false
    if (Test-Path -LiteralPath $target) {
        $item = Get-Item -LiteralPath $target
        if ($item.Length -eq [int64]$asset.size) {
            $valid = ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLower() -eq $asset.sha256.ToLower())
        }
    }
    if (-not $valid) {
        Write-Host "正在从 GitHub 下载 $($asset.name)，文件较大，请耐心等待..."
        $tmp = "$target.part"
        Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $downloaded = $false
        foreach ($baseUrl in $baseUrls) {
            try {
                Download-WithProgress ("$baseUrl/$($asset.name)") $tmp $asset.name ([int64]$asset.size)
                if ((Get-Item -LiteralPath $tmp).Length -eq [int64]$asset.size -and
                    (Get-FileHash -LiteralPath $tmp -Algorithm SHA256).Hash.ToLower() -eq $asset.sha256.ToLower()) {
                    Move-Item -LiteralPath $tmp -Destination $target -Force
                    $downloaded = $true
                    break
                }
            } catch {}
            Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
        }
        if (-not $downloaded) { Fail "资源下载或 SHA-256 校验失败：$($asset.name)" }
    }
    return $target
}

$wordAsset = $release.assets | Where-Object { $_.kind -eq 'word_audio' } | Select-Object -First 1
$sentenceAsset = $release.assets | Where-Object { $_.kind -eq 'sentence_audio' } | Select-Object -First 1
if (-not $wordAsset -or -not $sentenceAsset) { Fail 'Release 清单没有完整的单词和例句音频资源。' }
$wordZip = Download-VerifiedAsset $wordAsset
$sentenceZip = Download-VerifiedAsset $sentenceAsset
Write-Step '音频资源下载并校验完成，正在解压。'
$stagePattern = 'jpmod_audio_stage*'
foreach ($staleStage in @(Get-ChildItem -LiteralPath $data -Directory -Filter $stagePattern -ErrorAction SilentlyContinue)) {
    try {
        Remove-Item -LiteralPath $staleStage.FullName -Recurse -Force -ErrorAction Stop
        Write-Host "已清理上次安装遗留的临时目录：$($staleStage.Name)" -ForegroundColor DarkGray
    } catch {
        Write-Host "无法清理旧临时目录：$($staleStage.FullName)，本次将使用新的临时目录继续安装。" -ForegroundColor DarkYellow
    }
}
$audioStage = Join-Path $data ("jpmod_audio_stage_{0}_{1}" -f $stamp, ([guid]::NewGuid().ToString('N')))
New-Item -ItemType Directory -Force -Path $audioStage | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $audioStage 'vocabulary') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $audioStage 'sentence_audio') | Out-Null
Write-Host '正在解压单词音频...'
Expand-ZipWithProgress $wordZip (Join-Path $audioStage 'vocabulary') '单词音频'
Write-Host '正在解压例句音频...'
Expand-ZipWithProgress $sentenceZip (Join-Path $audioStage 'sentence_audio') '例句音频'
Write-Step '音频解压完成，正在安装插件和词书文件。'
foreach ($name in @('MyBook.es3', 'SaveFile.es3')) {
    $src = Join-Path $data $name
    if (Test-Path -LiteralPath $src) { Copy-Item -LiteralPath $src -Destination (Join-Path $backup $name) }
}
foreach ($name in @('JpWordListMod.dll', 'BookNameMod.dll', 'SentenceAudioMod.dll')) {
    $src = Join-Path $bepRoot "plugins\$name"
    if (Test-Path -LiteralPath $src) { Copy-Item -LiteralPath $src -Destination (Join-Path $backup $name) }
}

New-Item -ItemType Directory -Force -Path (Join-Path $bepRoot 'plugins') | Out-Null
foreach ($name in @('JpWordListMod.dll', 'BookNameMod.dll', 'SentenceAudioMod.dll')) {
    $src = Join-Path $plugins $name
    if (-not (Test-Path -LiteralPath $src)) { Fail "安装包缺少插件：$name" }
    Copy-Item -LiteralPath $src -Destination (Join-Path $bepRoot "plugins\$name") -Force
}

$bookDir = Join-Path $payload 'books'
Get-ChildItem -LiteralPath $bookDir -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $data $_.Name) -Force
}
$repairDir = Join-Path $data 'jp_db_payload'
New-Item -ItemType Directory -Force -Path $repairDir | Out-Null
Get-ChildItem -LiteralPath (Join-Path $payload 'jp_db_payload') -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $repairDir $_.Name) -Force
}
New-Item -ItemType Directory -Force -Path (Join-Path $env:USERPROFILE 'AppData\LocalLow\WCP\vocabulary') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $data 'sentence_audio') | Out-Null
Copy-Item -Path (Join-Path $audioStage 'vocabulary\*') -Destination (Join-Path $env:USERPROFILE 'AppData\LocalLow\WCP\vocabulary') -Recurse -Force
Copy-Item -Path (Join-Path $audioStage 'sentence_audio\*') -Destination (Join-Path $data 'sentence_audio') -Recurse -Force
try {
    Remove-Item -LiteralPath $audioStage -Recurse -Force -ErrorAction Stop
    Write-Host '音频临时目录已清理。' -ForegroundColor DarkGray
} catch {
    Write-Host "音频临时目录未能自动清理：$audioStage；不影响安装结果。" -ForegroundColor DarkYellow
}

function Set-WrappedValue($doc, [string]$key, $value, [string]$typeName) {
    $prop = $doc.PSObject.Properties[$key]
    if ($prop) {
        if ($prop.Value -and $prop.Value.PSObject.Properties['value']) { $prop.Value.value = $value }
        else { $prop.Value = [pscustomobject]@{ __type = $typeName; value = $value } }
    } else {
        $doc | Add-Member -NotePropertyName $key -NotePropertyValue ([pscustomobject]@{ __type = $typeName; value = $value })
    }
}

$bookPayloadPath = Join-Path $payload 'catbar_book.json'
if (-not (Test-Path -LiteralPath $bookPayloadPath)) { Fail '安装包缺少 catbar_book.json。' }
$bookPayload = Get-Content -LiteralPath $bookPayloadPath -Raw -Encoding UTF8 | ConvertFrom-Json
Write-Step '正在写入日语词书并设置当前选中词书。'
$words = @($bookPayload.words | ForEach-Object { [string]$_ })
$meanings = [ordered]@{}
foreach ($p in $bookPayload.meanings.PSObject.Properties) { $meanings[$p.Name] = [string]$p.Value }

$myBookPath = Join-Path $data 'MyBook.es3'
$savePath = Join-Path $data 'SaveFile.es3'
if (Test-Path -LiteralPath $myBookPath) {
    $myBook = Get-Content -LiteralPath $myBookPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $targetSlot = 0
    for ($i = 1; $i -le 4; $i++) {
        $p = $myBook.PSObject.Properties["SelfBookList$i"]
        $v = if ($p -and $p.Value.PSObject.Properties['value']) { @($p.Value.value) } else { @() }
        if ($v.Count -eq $words.Count) { $targetSlot = $i; break }
    }
    if ($targetSlot -eq 0) {
        for ($i = 1; $i -le 4; $i++) {
            $p = $myBook.PSObject.Properties["SelfBookList$i"]
            $v = if ($p -and $p.Value.PSObject.Properties['value']) { @($p.Value.value) } else { @() }
            if ($v.Count -eq 0) { $targetSlot = $i; break }
        }
    }
    if ($targetSlot -eq 0) {
        $choice = Read-Host '四个自定义槽都已占用，请输入要替换的槽位 1-4（默认 1）'
        $targetSlot = if ($choice -match '^[1-4]$') { [int]$choice } else { 1 }
    }
    $arrType = 'System.String[],mscorlib'
    $dictType = 'System.Collections.Generic.Dictionary`2[[System.String, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089],[System.String, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089]],mscorlib'
    Set-WrappedValue $myBook "SelfBookList$targetSlot" $words $arrType
    Set-WrappedValue $myBook "wordDictionary$targetSlot" $meanings $dictType
    $json = $myBook | ConvertTo-Json -Depth 100
    [IO.File]::WriteAllText($myBookPath, $json, (New-Object Text.UTF8Encoding($false)))
    Write-Host "已写入日语词书槽位 $targetSlot（$($words.Count) 词）。"

    if (Test-Path -LiteralPath $savePath) {
        $save = Get-Content -LiteralPath $savePath -Raw -Encoding UTF8 | ConvertFrom-Json
        $canonical = @('','自定义词书一','自定义词书二','自定义词书三','自定义词书四')[$targetSlot]
        Set-WrappedValue $save 'ChosenBook_Para' $canonical 'string'
        $listType = 'System.Collections.Generic.List`1[[System.String, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089]],mscorlib'
        Set-WrappedValue $save 'ChosenBook_List' $words $listType
        Set-WrappedValue $save "SelfBookName$targetSlot" '日语词库(猫条版)' 'string'
        $saveJson = $save | ConvertTo-Json -Depth 100
        [IO.File]::WriteAllText($savePath, $saveJson, (New-Object Text.UTF8Encoding($false)))
        Write-Host '已将当前选中词书切换为日语词库(猫条版)。'
    } else {
        Write-Host '没有找到 SaveFile.es3，已安装词书；首次进入游戏后请在自定义词书中选择它。' -ForegroundColor Yellow
    }
} else {
    Write-Host '没有找到 MyBook.es3，已安装插件和导入文件；请先启动游戏完成一次初始化，再重新运行安装器自动导入。' -ForegroundColor Yellow
}

$marker = [ordered]@{ installed = (Get-Date).ToString('s'); game = $game; backup = $backup; profile = $bookPayload.id; words = $words.Count }
[IO.File]::WriteAllText((Join-Path $data 'jpmod_install.json'), ($marker | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
Write-Host "游戏目录: $game"
Write-Host "备份目录: $backup"
Write-Host '安装成功。'
Write-Step '全部安装步骤已完成。'
