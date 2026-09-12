# -*- coding: utf-8 -*-
"""Shared model of JpWordListPlugin.CrossBookGuard (v1.7.1)."""
import io
import json
import os
import sys

PD = r"C:/Users/hanserdesu/AppData/LocalLow/WCP/wcp"

POOL = "allTestWordsS10_Para"
NEED = "S8needToLearnWordList_Para"
LEFT = "S8TestWordList_LearnedTest_left"
OWNED_LISTS = "JpWL_owned_lists"
OWNED_ARRAYS = "JpWL_owned_arrays"
BAK = "JpWL_bak_"
JP_FIELDS = (POOL, NEED, LEFT)


def unwrap(node):
    if isinstance(node, dict):
        if "__type" in node and "value" in node:
            return unwrap(node["value"])
        return {k: unwrap(v) for k, v in node.items()}
    if isinstance(node, list):
        return [unwrap(x) for x in node]
    return node


def load(path):
    with io.open(path, "r", encoding="utf-8") as f:
        return {k: unwrap(v) for k, v in json.load(f).items()}


def looks_japanese(w):
    for ch in w or "":
        o = ord(ch)
        if 0x3040 <= o <= 0x30FF or 0x3400 <= o <= 0x4DBF or 0x4E00 <= o <= 0x9FFF:
            return True
        if 0xF900 <= o <= 0xFAFF or 0xFF66 <= o <= 0xFF9D:
            return True
        if o in (0x3005, 0x3006, 0x3007):
            return True
    return False


def contains_japanese(seq):
    for w in seq or []:
        if looks_japanese(w):
            return True
    return False


SV = load(os.path.join(PD, "SaveFile.es3"))
LEARNED = SV.get("HaveLearnedDictionary") or {}
ENGLISH_LEARNED = [w for w in LEARNED if not looks_japanese(w)][:10]
JP_POOL = ["続ける", "歯医者", "全部", "うち", "豚肉", "工業", "割れる", "黄色", "今週", "止む"]


def fresh_save():
    d = {
        POOL: list(JP_POOL),
        NEED: list(JP_POOL),
        LEFT: list(JP_POOL),
        "S8Progress_Para": 0,
        "testingIf_Para": True,
        "testingIf_CompleteIf": False,
        OWNED_LISTS: list(JP_FIELDS),
        OWNED_ARRAYS: [],
    }
    for k in JP_FIELDS:
        d[BAK + k] = list(ENGLISH_LEARNED)
    return d


def cross_book_guard(save, book_state):
    if book_state == 1:
        return save, "in-jp-book-noop"
    owned = save.get(OWNED_LISTS) or []
    owned_arrays = save.get(OWNED_ARRAYS) or []
    if not owned and not owned_arrays:
        return save, "not-owned-noop"
    dirty = False
    for key in owned + owned_arrays:
        base = save.get(BAK + key)
        if base is None or contains_japanese(base):
            save[key] = []
            dirty = True
        else:
            save[key] = list(base)
    if dirty:
        save["S8Progress_Para"] = 0
        save["testingIf_Para"] = False
        save["testingIf_CompleteIf"] = True
    save[OWNED_LISTS] = []
    save[OWNED_ARRAYS] = []
    return save, ("restored+ended" if dirty else "restored")


OK = [True]


def check(name, cond):
    print("   [%s] %s" % ("PASS" if cond else "FAIL", name))
    OK[0] = OK[0] and bool(cond)
