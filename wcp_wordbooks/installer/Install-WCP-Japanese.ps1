$ErrorActionPreference = 'Stop'

function Write-Step([string]$message) {
    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] $message" -ForegroundColor Cyan
}

function Fail([string]$message) {
    throw "错误: $message"
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
        $vdf = Join-Path $root 'steamapps\libraryfolders.vdf'
        if (-not (Test-Path -LiteralPath $vdf)) { continue }
        try {
            $raw = Get-Content -LiteralPath $vdf -Raw -ErrorAction Stop
            foreach ($m in [regex]::Matches($raw, '"path"\s*"([^"]+)"')) {
                Add-Candidate $roots ($m.Groups[1].Value -replace '\\\\','\')
            }
        } catch {}
    }
    return @($roots)
}

function Get-GameCandidates {
    $list = New-Object 'System.Collections.Generic.List[string]'
    Add-Candidate $list $env:WCP_GAME_DIR
    foreach ($root in (Get-SteamRoots)) {
        Add-Candidate $list (Join-Path $root 'steamapps\common\WCP-WordGirlgriend')
    }
    foreach ($root in @('C:\Program Files (x86)\Steam\steamapps\common', 'C:\Program Files\Steam\steamapps\common')) {
        Add-Candidate $list (Join-Path $root 'WCP-WordGirlgriend')
    }
    return @($list | Where-Object {
        (Test-Path -LiteralPath (Join-Path $_ 'wcp_Data\Managed\Assembly-CSharp.dll')) -and
        (Test-Path -LiteralPath (Join-Path $_ 'wcp_Data\StreamingAssets\wcpFullEng.db'))
    })
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
                $request = @{ Uri = "$baseUrl/$($asset.name)"; OutFile = $tmp }
                if ($PSVersionTable.PSVersion.Major -lt 6) { $request.UseBasicParsing = $true }
                Invoke-WebRequest @request
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
$audioStage = Join-Path $data 'jpmod_audio_stage'
if (Test-Path -LiteralPath $audioStage) { Remove-Item -LiteralPath $audioStage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $audioStage | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $audioStage 'vocabulary') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $audioStage 'sentence_audio') | Out-Null
Write-Host '正在解压单词音频...'
Expand-Archive -LiteralPath $wordZip -DestinationPath (Join-Path $audioStage 'vocabulary') -Force
Write-Host '正在解压例句音频...'
Expand-Archive -LiteralPath $sentenceZip -DestinationPath (Join-Path $audioStage 'sentence_audio') -Force
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
