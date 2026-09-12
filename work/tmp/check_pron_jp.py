import io
import json
import sqlite3
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

DB = r'E:\Steam\steamapps\common\WCP-WordGirlgriend\wcp_Data\StreamingAssets\wcpOnlyWord.db'
conn = sqlite3.connect(DB)
cur = conn.cursor()

for w in ['割れる', '黄色', '続ける', '歯医者', '全部', 'うち', 'あそこ']:
    cur.execute("select meaning from pron where word=?", (w,))
    row = cur.fetchone()
    print(repr(w), "=>", (row[0][:70] if row else None))

sv = json.load(io.open(r'C:\Users\hanserdesu\AppData\LocalLow\WCP\wcp\SaveFile.es3', encoding='utf-8'))
book = sv['ChosenBook_List']['value']
cur.execute("select word from pron")
db = set(r[0] for r in cur.fetchall())
hit = sum(1 for w in book if w in db)
print("book words with a pron row: %d / %d" % (hit, len(book)))
missing = [w for w in book if w not in db][:12]
print("missing sample:", missing)
