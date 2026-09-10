# -*- coding: utf-8 -*-
"""生成 WCP 游戏官方导入通道所需的配套文件:
  - 每级别 .xlsx (游戏内可用 Excel 导入创建词书)
  - 全级别 SQLite 外接词库 wcp_jlpt.db (pron 表: word/meaning)

游戏内 Excel 导入契约 (逆向自 SaveSourceType, NPOI):
  - 读第一个 sheet, 从第 0 行开始逐行读 A/B 两列
  - A列=单词, B列=释义(游戏内显示文本)  -> 因此【不要加表头行】
  - C列以后被忽略, 可放读音/例句等人工参考信息
外接词库通道: persistentDataPath 下 .db, 表名/列名在游戏 UI 里指定,
  默认兼容 pron(word, meaning)。
"""
import json
import sqlite3
from pathlib import Path

from openpyxl import Workbook
from openpyxl.styles import Alignment, Font, PatternFill

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / 'output'
IMPORT = OUT / 'import'

# A=单词 B=释义(游戏读取); C-F 仅人工参考
SLOT_MAP = {
    'JLPT_N5N4_初级': ['n5', 'n4'],
    'JLPT_N3': ['n3'],
    'JLPT_N2': ['n2'],
    'JLPT_N1': ['n1'],
}


def main():
    data = json.loads((OUT / 'jlpt_books.json').read_text(encoding='utf-8'))
    levels = data['levels']
    IMPORT.mkdir(parents=True, exist_ok=True)

    all_rows = []
    for name, lvs in SLOT_MAP.items():
        wb = Workbook()
        ws = wb.active
        ws.title = '词汇表'
        count = 0
        for lv in lvs:
            for r in levels[lv]:
                meaning = r.get('meaning') or r.get('meaning_en') or ''
                if not r['word'] or not meaning:
                    continue
                # 游戏: A=单词, B=释义; C/D/E=读音/英文/例句(仅人工参考)
                row = [r['word'], meaning, r['reading'], r['meaning_en'],
                       r['example_ja'], r['level']]
                ws.append(row)
                all_rows.append(row)
                count += 1
        for col, w in zip('ABCDEF', (18, 46, 14, 40, 44, 8)):
            ws.column_dimensions[col].width = w
        for row in ws.iter_rows(min_row=1):
            for c in row:
                c.alignment = Alignment(vertical='center', wrap_text=True)
        path = IMPORT / f'{name}.xlsx'
        wb.save(path)
        print(f'{path.name}: {count} 词')

    # 外接词库 SQLite
    db_path = IMPORT / 'wcp_jlpt.db'
    if db_path.exists():
        db_path.unlink()
    con = sqlite3.connect(db_path)
    cur = con.cursor()
    cur.execute('CREATE TABLE pron (word TEXT PRIMARY KEY, meaning TEXT)')
    seen = set()
    for word, meaning, *_rest in all_rows:
        if word in seen:
            continue
        seen.add(word)
        cur.execute('INSERT INTO pron VALUES (?, ?)', (word, meaning))
    cur.execute('''CREATE TABLE jlpt_all (
        word TEXT, meaning TEXT, reading TEXT, meaning_en TEXT,
        example_ja TEXT, level TEXT)''')
    for row in all_rows:
        cur.execute('INSERT INTO jlpt_all VALUES (?,?,?,?,?,?)', row)
    con.commit()
    con.close()
    print(f'{db_path.name}: pron {len(seen)} 词 (去重), jlpt_all {len(all_rows)} 行')


if __name__ == '__main__':
    main()
