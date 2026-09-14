# -*- coding: utf-8 -*-
"""构建可发布的 Gitee/GitHub Release 资源。

生成三类文件：
  1. WCP日语词书一键安装包-<tag>.zip  核心安装器和词书
  2. wcp-japanese-audio-words.zip   单词音频
  3. wcp-japanese-audio-sentences.zip 例句音频

核心包不内嵌大音频；Install-WCP-Japanese.ps1 按本清单探测 Gitee/GitHub
下载路由并校验两份音频，确保群友安装后的运行资源与作者本机一致。
"""
import hashlib
import json
import os
import shutil
import subprocess
import sys
import time
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / 'output'
PKG = OUT / 'installer_pkg' / 'WCP日语词书安装包'
RELEASE = OUT / 'release'
MAIN_TAG = 'wcp-jp-v1.2.2'
RESOURCE_TAG = 'wcp-jp-resources-v1.0.0'
GITEE_REPO = os.environ.get('WCP_GITEE_REPO', 'https://gitee.com/hanserdesu/japanese').rstrip('/')
GITEE_URL_TEMPLATE = os.environ.get(
    'WCP_GITEE_URL_TEMPLATE',
    f'{GITEE_REPO}/releases/download/{RESOURCE_TAG}/{{name}}',
)
# Stay below the strict 50 MB single-file quota so the same parts can be
# uploaded even when a Gitee account applies repository-style limits.
GITEE_PART_SIZE = 45 * 1024 * 1024


def sha256(path):
    h = hashlib.sha256()
    with path.open('rb') as f:
        for block in iter(lambda: f.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()


def zip_tree(source, target):
    if target.exists():
        target.unlink()
    files = [p for p in source.rglob('*') if p.is_file() and p.stat().st_size > 1000]
    with zipfile.ZipFile(target, 'w', zipfile.ZIP_STORED, allowZip64=True) as z:
        for p in files:
            z.write(p, p.relative_to(source).as_posix())
    print(f'{target.name}: {len(files)} files, {target.stat().st_size / 1e9:.3f} GB')


def split_file(source, target_dir):
    """Split one resource for Gitee's per-attachment limit.

    The parts stay byte-for-byte concatenable. Each part is independently
    hashed so the installer can resume valid parts and reject corrupt ones.
    """
    target_dir.mkdir(parents=True, exist_ok=True)
    for old in target_dir.glob(source.name + '.*'):
        if old.is_file():
            old.unlink()
    parts = []
    with source.open('rb') as src:
        index = 0
        while True:
            block = src.read(GITEE_PART_SIZE)
            if not block:
                break
            index += 1
            part = target_dir / f'{source.name}.{index:03d}'
            part.write_bytes(block)
            parts.append({'name': part.name, 'size': part.stat().st_size,
                          'sha256': sha256(part)})
    print(f'{source.name}: {len(parts)} Gitee parts')
    return parts


def zip_uncompressed_size(path):
    with zipfile.ZipFile(path) as archive:
        return sum(info.file_size for info in archive.infolist())


def build_core():
    subprocess.run([sys.executable, str(ROOT / 'tools' / 'build_installer_payload.py'),
                    '--skip-audio'], check=True)


def main():
    build_core()
    audio_root = Path.home() / 'AppData' / 'LocalLow' / 'WCP'
    words = audio_root / 'vocabulary'
    sentences = audio_root / 'wcp' / 'sentence_audio'
    if not words.is_dir() or not sentences.is_dir():
        raise FileNotFoundError('本机单词音频或例句音频目录不存在')
    RELEASE.mkdir(parents=True, exist_ok=True)
    word_zip = RELEASE / 'wcp-japanese-audio-words.zip'
    sentence_zip = RELEASE / 'wcp-japanese-audio-sentences.zip'
    if '--reuse-audio' not in sys.argv or not word_zip.exists():
        zip_tree(words, word_zip)
    else:
        print(f'reuse {word_zip.name}')
    if '--reuse-audio' not in sys.argv or not sentence_zip.exists():
        zip_tree(sentences, sentence_zip)
    else:
        print(f'reuse {sentence_zip.name}')

    base = f'https://github.com/hanserdesu/japanese/releases/download/{RESOURCE_TAG}'
    assets = []
    gitee_parts = RELEASE / 'gitee-parts'
    for kind, path in (('word_audio', word_zip), ('sentence_audio', sentence_zip)):
        assets.append({'kind': kind, 'name': path.name,
                       'size': path.stat().st_size,
                       'expanded_size': zip_uncompressed_size(path),
                       'sha256': sha256(path),
                       'parts': split_file(path, gitee_parts)})
    release_manifest = {
        'version': RESOURCE_TAG,
        'built': time.strftime('%Y-%m-%d %H:%M:%S'),
        # Keep base_urls for older installers. New installers use route
        # metadata and can probe a domestic mirror before falling back to GitHub.
        'base_urls': [base],
        'download_routes': [
            {'id': 'gitee', 'name': '国内 Gitee Release', 'mode': 'parts',
             'url_template': GITEE_URL_TEMPLATE},
            {'id': 'github', 'name': 'GitHub Release',
             'url_template': base + '/{name}'},
        ],
        'assets': assets,
    }
    manifest = RELEASE / 'release-manifest.json'
    manifest.write_text(json.dumps(release_manifest, ensure_ascii=False, indent=2) + '\n',
                        encoding='utf-8')
    shutil.copy2(manifest, PKG / 'support' / 'release-manifest.json')

    # GitHub release uploads can mangle non-ASCII asset names. Keep the
    # downloadable filename ASCII; the archive itself remains Chinese-first.
    core_zip = RELEASE / f'WCP-Japanese-OneClick-Installer-{MAIN_TAG}.zip'
    if core_zip.exists():
        core_zip.unlink()
    with zipfile.ZipFile(core_zip, 'w', zipfile.ZIP_DEFLATED, allowZip64=True) as z:
        for p in PKG.rglob('*'):
            if p.is_file():
                z.write(p, p.relative_to(PKG.parent).as_posix())
    index = RELEASE / 'release-index.json'
    index.write_text(json.dumps({
        'main_release': MAIN_TAG,
        'resource_release': RESOURCE_TAG,
        'core_installer': {'name': core_zip.name, 'size': core_zip.stat().st_size,
                           'sha256': sha256(core_zip)},
        'resource_manifest': manifest.name,
    }, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print('release manifest:', manifest)
    print('release files:', RELEASE)


if __name__ == '__main__':
    main()
