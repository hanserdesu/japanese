# -*- coding: utf-8 -*-
"""【已废弃改名功能 2026-09-06 晚】

逆向发现 (GetSelfBookMeaningAfterSet::SetSelfBook):
  游戏只在书名 == 「自定义词书一/二/三/四」(或 新概念第一~四册) 时
  才把 MyBook.es3 的 wordDictionaryN 装载进 SelfBookMeaningDictionary,
  每日学习界面的查词才走词书内置释义。

  因此自定义书名必须保持默认「自定义词书一~四」!
  (释义/例句已改由本地词库 wcpFullEng.db 兜底, 见 patch_local_db.py,
   但保留内置字典路径响应更快。)

本脚本现在的作用: 仅当书名被改动过时, 恢复为默认名(游戏关闭时执行)。
"""
import json
import shutil
import subprocess
import sys
import time
from pathlib import Path

SAVE = Path.home() / 'AppData' / 'LocalLow' / 'WCP' / 'wcp' / 'SaveFile.es3'
NAMES = {
    'SelfBookName1': '自定义词书一',
    'SelfBookName2': '自定义词书二',
    'SelfBookName3': '自定义词书三',
    'SelfBookName4': '自定义词书四',
}


def game_running():
    r = subprocess.run(['tasklist', '/FI', 'IMAGENAME eq wcp.exe'],
                       capture_output=True, text=True, encoding='gbk',
                       errors='replace')
    return 'wcp.exe' in (r.stdout or '')


def main():
    if game_running():
        print('wcp.exe 正在运行, 跳过(避免存档覆盖)')
        return
    if not SAVE.exists():
        print('SaveFile.es3 不存在')
        return
    doc = json.loads(SAVE.read_text(encoding='utf-8-sig'))
    changed = []
    for k, name in NAMES.items():
        cur = doc.get(k, {}).get('value')
        if cur is not None and cur != name:
            doc[k] = {'__type': 'string', 'value': name}
            changed.append(f'{k}: {cur} -> {name}')
    if not changed:
        print('书名均为默认(自定义词书一~四), 无需修改')
        return
    bak = SAVE.with_suffix(f'.es3.bak_{time.strftime("%Y%m%d_%H%M%S")}')
    shutil.copy2(SAVE, bak)
    SAVE.write_text(json.dumps(doc, ensure_ascii=False, indent=1),
                    encoding='utf-8')
    print('已恢复默认书名:', '; '.join(changed))
    print('备份:', bak)


if __name__ == '__main__':
    main()
