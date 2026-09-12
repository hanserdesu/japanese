# -*- coding: utf-8 -*-
"""Cross-book guard (v1.7.1): quit in the JP book, resume in a different book."""
import sys

sys.path.insert(0, r"D:\Japanese\work\tmp")
from guard_model import (  # noqa: E402
    BAK, ENGLISH_LEARNED, JP_FIELDS, JP_POOL, LEFT, NEED,
    OWNED_ARRAYS, OWNED_LISTS, POOL, check, contains_japanese,
    cross_book_guard, fresh_save, OK,
)

print("--- A. quit in JP book, resume in another book (the leak) ---")
s, how = cross_book_guard(fresh_save(), 0)
print("   action = %s" % how)
check("no Japanese word left in the test pool", not contains_japanese(s[POOL]))
check("no Japanese word left in the stem queue", not contains_japanese(s[NEED]))
check("no Japanese word left in the left queue", not contains_japanese(s[LEFT]))
check("pre-takeover queue was restored", s[POOL] == ENGLISH_LEARNED)
check("takeover markers cleared", not s[OWNED_LISTS] and not s[OWNED_ARRAYS])
check("a resumable English test stays resumable", s["testingIf_Para"] is True)

print()
print("--- B. baseline unusable (missing, or itself Japanese) ---")
for label in ("baseline key missing", "baseline is Japanese"):
    d = fresh_save()
    for k in JP_FIELDS:
        if label == "baseline key missing":
            del d[BAK + k]
        else:
            d[BAK + k] = list(JP_POOL)
    s, how = cross_book_guard(d, 0)
    print("   %s -> action = %s" % (label, how))
    check("   %s: queues emptied" % label, s[POOL] == [] and s[NEED] == [])
    check("   %s: unfinished test ended" % label,
          s["testingIf_Para"] is False and s["testingIf_CompleteIf"] is True)
    check("   %s: nothing Japanese leaked" % label, not contains_japanese(s[POOL]))

print()
print("--- C. still inside the JP book ---")
s, how = cross_book_guard(fresh_save(), 1)
print("   action = %s" % how)
check("guard leaves the JP book's own queues alone", s[POOL] == JP_POOL and s[NEED] == JP_POOL)
check("markers kept for the later in-session switch", s[OWNED_LISTS] == list(JP_FIELDS))

print()
print("--- D. never took over anything ---")
d = fresh_save()
for k in list(d):
    if k.startswith(BAK) or k.startswith("JpWL_"):
        del d[k]
plain = ["apple", "banana", "cherry", "melon", "peach"]
d[POOL] = list(plain)
s, how = cross_book_guard(d, 0)
print("   action = %s" % how)
check("guard is a no-op", how == "not-owned-noop" and s[POOL] == plain)
check("a plain English test is untouched", s["testingIf_Para"] is True)

print()
print("--- E. idempotence ---")
_, how2 = cross_book_guard(s, 0)
check("second run is a no-op", how2 == "not-owned-noop")

print()
print("--- F. a taken-over string[] field (S9extraStudy_Para) ---")
ARR = "S9extraStudy_Para"
d = fresh_save()
d[ARR] = list(JP_POOL)
d[BAK + ARR] = list(ENGLISH_LEARNED)
d[OWNED_ARRAYS] = [ARR]
s, how = cross_book_guard(d, 0)
print("   usable baseline -> action = %s" % how)
check("owned array field restored to pre-takeover value", s[ARR] == ENGLISH_LEARNED)
check("no Japanese left in the array field", not contains_japanese(s[ARR]))
check("array markers cleared", not s[OWNED_ARRAYS])

d = fresh_save()
d[ARR] = list(JP_POOL)
d[BAK + ARR] = list(JP_POOL)          # baseline itself is Japanese -> unusable
d[OWNED_ARRAYS] = [ARR]
s, how = cross_book_guard(d, 0)
print("   Japanese baseline -> action = %s" % how)
check("Japanese array baseline is emptied", s[ARR] == [])
check("Japanese array baseline ends the unfinished test",
      s["testingIf_Para"] is False and s["testingIf_CompleteIf"] is True)

print()
print("RESULT:", "ALL PASS" if OK[0] else "FAILED")
sys.exit(0 if OK[0] else 1)
