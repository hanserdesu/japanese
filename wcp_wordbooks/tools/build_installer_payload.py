# -*- coding: utf-8 -*-
"""构建可移植安装包 (WCP日语词书安装包/):

  一键安装日语词书.cmd    ← 自动定位 Steam 游戏目录并安装
  payload/additions.db    pron/help/sentence2/bookslot 增量数据 (SQL 安装时合并)
  payload/books/          persistentDataPath 词书文件 (xlsx + db)
  payload/audio/words.zip        单词发音 mp3 (~350MB)
  payload/audio/sentences.zip    例句发音 mp3 (~1.4GB)
  payload/plugins/*.dll          BepInEx 插件
  payload/catbar_book.json       可移植合并词书, 安装时自动写入一个自定义槽
  payload/manifest.json

用法: python tools/build_installer_payload.py [--skip-audio]
"""
import json
import shutil
import sqlite3
import sys
import time
import zipfile
from pathlib import Path

sys.stdout.reconfigure(encoding='utf-8')
ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / 'tools'))
OUT = ROOT / 'output'
PKG = OUT / 'installer_pkg' / 'WCP日语词书安装包'
PAYLOAD = PKG / 'payload'
PDA = Path.home() / 'AppData' / 'LocalLow' / 'WCP'

BOOK_FILES = ['JLPT_N5N4_初级.xlsx', 'JLPT_N3.xlsx', 'JLPT_N2.xlsx',
              'JLPT_N1.xlsx', 'IT用语.xlsx',
              'wcp_jlpt.db', 'wcp_setb.db', 'wcp_themed.db',
              'wcp_kanji.db', 'wcp_all.db', 'wcp_grammar.db',
              '语法路线.xlsx']

PLUGIN_FILES = {
    'JpWordListMod.dll': ROOT.parent / 'mod_jp_wordlist' / 'JpWordListMod.dll',
    'BookNameMod.dll': ROOT.parent / 'mod_book_name' / 'BookNameMod.dll',
    'SentenceAudioMod.dll': ROOT.parent / 'mod_sentence_audio' / 'SentenceAudioMod.dll',
}


def build_combined_book_payload():
    """Write the exact profile recognized by BookProfiles.cs."""
    data = json.loads((OUT / 'jlpt_books.json').read_text(encoding='utf-8'))
    seen, words, meanings = set(), [], {}
    for level in ('n5', 'n4', 'n3', 'n2', 'n1'):
        for row in data['levels'][level]:
            word = (row.get('word') or '').strip()
            meaning = (row.get('meaning') or row.get('meaning_en') or '').strip()
            if not word or not meaning or word in seen:
                continue
            seen.add(word)
            words.append(word)
            meanings[word] = meaning
    if len(words) != 7922:
        raise RuntimeError(f'combined JLPT profile count mismatch: {len(words)}')
    payload = {
        'id': 'catbar-jlpt-complete',
        'language': 'ja',
        'display_name': '日语词库(猫条版)',
        'word_count': len(words),
        'fingerprint_sha256': '6d7a51c0c5d30dd63d8a6e3412bce1cf2419554fb8487cf5c8a205e43ac41663',
        'words': words,
        'meanings': meanings,
    }
    (PAYLOAD / 'catbar_book.json').write_text(
        json.dumps(payload, ensure_ascii=False, indent=1), encoding='utf-8')
    print(f'catbar_book.json: {len(words)} 词')


def collect_words():
    from patch_local_db import collect
    words = collect()
    # 语法占位词
    from build_grammar_book import load_curriculum, build_route, load_content
    route, _ = build_route(load_curriculum(None), load_content(), True)
    ph = {}
    for e in route:
        if e['kind'] in ('grammar', 'stage'):
            ph[e['word']] = {'reading': e['reading'], 'meaning': e['meaning'],
                             'ex_ja': '', 'ex_tr': ''}
    words.update(ph)
    return words, route


def build_additions(words, route):
    master = json.loads((ROOT / 'data' / 'translations' / 'sentences_master.json')
                        .read_text(encoding='utf-8'))
    furigana = json.loads((ROOT / 'data' / 'translations' / 'furigana_map.json')
                          .read_text(encoding='utf-8'))
    jlpt = json.loads((OUT / 'jlpt_books.json').read_text(encoding='utf-8'))
    path = PAYLOAD / 'additions.db'
    if path.exists():
        path.unlink()
    con = sqlite3.connect(path)
    cur = con.cursor()
    cur.execute('CREATE TABLE pron (word TEXT PRIMARY KEY, ukPhonic TEXT, meaning TEXT)')
    cur.execute('CREATE TABLE sentence2 (word TEXT, sentences TEXT)')
    cur.execute('CREATE TABLE help (word TEXT PRIMARY KEY, help TEXT)')
    cur.execute('CREATE TABLE bookslot (slot INTEGER, word TEXT, meaning TEXT)')
    n_p = n_s = n_h = n_b = 0
    for w, v in words.items():
        cur.execute('INSERT OR REPLACE INTO pron VALUES (?,?,?)',
                    (w, f"[{v['reading']}]" if v['reading'] else '', v['meaning']))
        n_p += 1
        for ja, zh in master.get(w, []):
            cur.execute('INSERT INTO sentence2 VALUES (?,?)',
                        (w, f'例句：{ja}（{zh}）' if zh else f'例句：{ja}'))
            n_s += 1
        if w in furigana:
            cur.execute('INSERT OR REPLACE INTO help VALUES (?,?)',
                        (w, f'振り仮名：{furigana[w]}'))
            n_h += 1
    for e in route:
        if e['kind'] in ('grammar', 'stage'):
            for ja, zh in e['rows']:
                cur.execute('INSERT INTO sentence2 VALUES (?,?)',
                            (e['word'], f'例句：{ja}（{zh}）'))
                n_s += 1
    slot_of = {'n5n4': 1, 'n3': 2, 'n2': 3, 'n1': 4}
    for lv, rows in jlpt['levels'].items():
        slot = slot_of.get(lv) or (1 if lv == 'n4' else None)
        for r in rows:
            if r.get('meaning'):
                cur.execute('INSERT INTO bookslot VALUES (?,?,?)',
                            (slot, r['word'], r['meaning']))
                n_b += 1
    con.commit()
    con.close()
    print(f'additions.db: pron {n_p}, sentence2 {n_s}, help {n_h}, bookslot {n_b}')


def zip_dir(src: Path, dst: Path):
    if dst.exists():
        dst.unlink()
    files = [p for p in src.rglob('*') if p.is_file() and p.stat().st_size > 1000]
    with zipfile.ZipFile(dst, 'w', zipfile.ZIP_STORED, allowZip64=True) as z:
        for p in files:
            z.write(p, p.name)
    print(f'{dst.name}: {len(files)} 文件, {dst.stat().st_size / 1e6:.0f}MB')


def main():
    skip_audio = '--skip-audio' in sys.argv
    if PKG.exists():
        shutil.rmtree(PKG)
    PAYLOAD.mkdir(parents=True)
    (PAYLOAD / 'books').mkdir()
    (PAYLOAD / 'plugins').mkdir()
    (PAYLOAD / 'jp_db_payload').mkdir()
    (PAYLOAD / 'audio').mkdir()

    words, route = collect_words()
    build_additions(words, route)
    build_combined_book_payload()

    for name in BOOK_FILES:
        src = PDA / 'wcp' / name
        if src.exists():
            shutil.copy2(src, PAYLOAD / 'books' / name)
        else:
            print(f'!! 缺词书文件: {name}')
    print(f'books: {len(list((PAYLOAD / "books").iterdir()))} 个')

    for name, dll in PLUGIN_FILES.items():
        if not dll.exists():
            raise FileNotFoundError(f'missing plugin build: {dll}')
        shutil.copy2(dll, PAYLOAD / 'plugins' / name)
    db_payload = ROOT / 'output' / 'jp_db_payload'
    for name in ('jp_pron.tsv', 'jp_sentences.tsv', 'jp_only_pron.tsv', 'manifest.json'):
        src = db_payload / name
        if not src.exists():
            raise FileNotFoundError(f'missing database repair payload: {src}')
        shutil.copy2(src, PAYLOAD / 'jp_db_payload' / name)

    combined = ROOT / 'output' / 'import' / '日语词库(猫条版).xlsx'
    if combined.exists():
        shutil.copy2(combined, PAYLOAD / 'books' / combined.name)
    else:
        print(f'!! 缺少可移植合并词书: {combined}')

    for name in ('一键安装日语词书.cmd', 'Install-WCP-Japanese.ps1',
                 '说明-给群友.txt'):
        src = ROOT / 'installer' / name
        if not src.exists():
            raise FileNotFoundError(f'missing installer file: {src}')
        shutil.copy2(src, PKG / name)

    if not skip_audio:
        t0 = time.time()
        zip_dir(PDA / 'vocabulary', PAYLOAD / 'audio' / 'words.zip')
        zip_dir(PDA / 'wcp' / 'sentence_audio', PAYLOAD / 'audio' / 'sentences.zip')
        print(f'音频打包用时 {(time.time() - t0) / 60:.1f}min')

    total = sum(f.stat().st_size for f in PAYLOAD.rglob('*') if f.is_file())
    manifest = {
        'built': time.strftime('%Y-%m-%d %H:%M'),
        'pron': len(words),
        'grammar_placeholders': sum(1 for e in route if e['kind'] == 'grammar'),
        'stages': 5,
        'route_rows': len(route),
        'skip_audio': skip_audio,
        'plugins': sorted(PLUGIN_FILES),
        'auto_import': True,
        'size_mb': round(total / 1e6, 1),
    }
    (PAYLOAD / 'manifest.json').write_text(
        json.dumps(manifest, ensure_ascii=False, indent=1), encoding='utf-8')
    print('manifest:', json.dumps(manifest, ensure_ascii=False))
    print('包目录 ->', PKG)


if __name__ == '__main__':
    main()
