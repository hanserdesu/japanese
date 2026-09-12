# -*- coding: utf-8 -*-
"""构建可移植安装包 (WCP日语词书安装包/):

  安装日语词书.exe        ← tools/wcp_installer.py 经 PyInstaller 另行编译
  payload/additions.db    pron/help/sentence2/bookslot 增量数据 (SQL 安装时合并)
  payload/books/          persistentDataPath 词书文件 (xlsx + db)
  payload/audio/words.zip        单词发音 mp3 (~350MB)
  payload/audio/sentences.zip    例句发音 mp3 (~1.4GB)
  payload/SentenceAudioMod.dll   BepInEx 播放插件
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
SA = Path(r'E:\SteamLibrary\steamapps\common\WCP-WordGirlgriend\wcp_Data\StreamingAssets')

BOOK_FILES = ['JLPT_N5N4_初级.xlsx', 'JLPT_N3.xlsx', 'JLPT_N2.xlsx',
              'JLPT_N1.xlsx', 'IT用语.xlsx',
              'wcp_jlpt.db', 'wcp_setb.db', 'wcp_themed.db',
              'wcp_kanji.db', 'wcp_all.db', 'wcp_grammar.db',
              '语法路线.xlsx']


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
    (PAYLOAD / 'audio').mkdir()

    words, route = collect_words()
    build_additions(words, route)

    for name in BOOK_FILES:
        src = PDA / 'wcp' / name
        if src.exists():
            shutil.copy2(src, PAYLOAD / 'books' / name)
        else:
            print(f'!! 缺词书文件: {name}')
    print(f'books: {len(list((PAYLOAD / "books").iterdir()))} 个')

    dll = ROOT.parent / 'mod_sentence_audio' / 'SentenceAudioMod.dll'
    if dll.exists():
        shutil.copy2(dll, PAYLOAD / 'SentenceAudioMod.dll')

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
        'size_mb': round(total / 1e6, 1),
    }
    (PAYLOAD / 'manifest.json').write_text(
        json.dumps(manifest, ensure_ascii=False, indent=1), encoding='utf-8')
    print('manifest:', json.dumps(manifest, ensure_ascii=False))
    print('包目录 ->', PKG)


if __name__ == '__main__':
    main()
