# -*- coding: utf-8 -*-
import json, io

SAVE = r"C:\Users\hanserdesu\AppData\LocalLow\WCP\wcp\SaveFile.es3"
OUT = r"D:\Japanese\work\tmp\filter_report.txt"

def unwrap(node):
    if isinstance(node, dict):
        if "__type" in node and "value" in node:
            v = node["value"]
            return unwrap(v)
        return {k: unwrap(v) for k, v in node.items()}
    if isinstance(node, list):
        return [unwrap(x) for x in node]
    return node

def is_jp(w):
    for ch in w or "":
        o = ord(ch)
        if 0x3040 <= o <= 0x30FF: return True
        if 0x3400 <= o <= 0x4DBF: return True
        if 0x4E00 <= o <= 0x9FFF: return True
        if 0xF900 <= o <= 0xFAFF: return True
        if 0xFF66 <= o <= 0xFF9D: return True
        if o in (0x3005, 0x3006, 0x3007): return True
    return False

def as_list(v):
    if isinstance(v, list): return v
    if isinstance(v, dict): return list(v.keys())
    return []

with io.open(SAVE, "r", encoding="utf-8") as f:
    raw = json.load(f)
d = {k: unwrap(v) for k, v in raw.items()}

book = as_list(d.get("ChosenBook_List"))
bookset = set(book)
s7 = as_list(d.get("S7TestWordList_Para"))
learned = d.get("HaveLearnedDictionary")
learned_keys = list(learned.keys()) if isinstance(learned, dict) else []

lines = []
lines.append("ChosenBook_Para = %r" % d.get("ChosenBook_Para"))
lines.append("ChosenBook_List n = %d" % len(book))
lines.append("S7FightWordType = %r" % d.get("S7FightWordType"))
lines.append("ReviewRange_Para = %r" % d.get("ReviewRange_Para"))
lines.append("S7FightWordMax = %r" % d.get("S7FightWordMax"))
lines.append("")
lines.append("S7TestWordList_Para n = %d" % len(s7))
inb = [w for w in s7 if w in bookset]
outb = [w for w in s7 if w not in bookset]
lines.append("  在本书内 = %d ; 不在本书 = %d" % (len(inb), len(outb)))
lines.append("  不在本书的样本 = %r" % outb[:10])
lines.append("  样例 = %r" % s7[:6])
lines.append("")
lines.append("HaveLearnedDictionary n = %d" % len(learned_keys))
jpk = [w for w in learned_keys if is_jp(w)]
nonjpk = [w for w in learned_keys if not is_jp(w)]
lines.append("  含日语字符 = %d ; 纯 ASCII = %d" % (len(jpk), len(nonjpk)))
lines.append("  日语样本 = %r" % jpk[:10])
lines.append("")
# 关键问题: 英语词书(所有已学)会不会看到日语词?
leak = [w for w in jpk if w not in bookset]
lines.append("日语词中「不在当前日语书里」的 = %d" % len(leak))
lines.append("")
# 模拟本地英文书: 取一个大纲词汇表里存在的非日语词
import os
lines.append("--- 模拟 Filter(非日语词书) 对 S7TestWordList_Para 的效果 ---")
kept_en = [w for w in s7 if not is_jp(w)]
lines.append("剔除日语后剩 = %d (原 %d)" % (len(kept_en), len(s7)))
lines.append("--- 模拟 Filter(日语词书) 对 S7TestWordList_Para 的效果 ---")
kept_jp = [w for w in s7 if w in bookset]
lines.append("剔除书外词后剩 = %d" % len(kept_jp))

with io.open(OUT, "w", encoding="utf-8") as f:
    f.write("\n".join(lines) + "\n")
print("written", OUT)

