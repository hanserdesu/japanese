// WCP Host — 运行态接管与还原协议
//
// 游戏的测试队列是全局单例。这个类把“进入一本受管词书时临时接管，
// 离开时只还原自己写过的字段”做成与语言无关的协议。所有持久化键都
// 使用 manifest.es3_prefix，避免不同语言的接管记录互相覆盖。
using System;
using System.Collections;
using System.Collections.Generic;

namespace WcpHost
{
    internal sealed class TakeoverScope
    {
        private static readonly string[] ListFields = new string[] {
            "S7TestWordList_Para",
            "allTestWordsS10_Para",
            "S8needToLearnWordList_Para"
        };

        private static readonly string[] ArrayFields = new string[] {
            "S9CurrentArray_Para",
            "S9extraStudy_Para"
        };

        private LanguageManifest _manifest;
        private IList<string> _bookWords;
        private readonly Dictionary<string, List<string>> _listBaselines =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);
        private readonly Dictionary<string, string[]> _arrayBaselines =
            new Dictionary<string, string[]>(StringComparer.Ordinal);
        private readonly HashSet<string> _ownedLists =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _ownedArrays =
            new HashSet<string>(StringComparer.Ordinal);
        private bool _active;

        internal bool IsActive { get { return _active; } }
        internal string ProfileId { get { return _manifest == null ? null : _manifest.Profile.Id; } }

        internal void Enter(LanguageManifest manifest, IList<string> bookWords)
        {
            if (manifest == null || bookWords == null || bookWords.Count == 0)
            {
                Leave();
                return;
            }
            if (_active && _manifest != null && _manifest.Profile.Id == manifest.Profile.Id)
            {
                _bookWords = bookWords;
                return;
            }
            Leave();
            _manifest = manifest;
            _bookWords = new List<string>(bookWords);
            _active = true;
            RecoverStale(manifest.Profile.Id);
        }

        internal void Leave()
        {
            if (!_active || _manifest == null)
            {
                _manifest = null;
                _bookWords = null;
                _active = false;
                return;
            }
            RestoreOwned(_manifest.Profile.Es3Prefix);
            _listBaselines.Clear();
            _arrayBaselines.Clear();
            _ownedLists.Clear();
            _ownedArrays.Clear();
            _manifest = null;
            _bookWords = null;
            _active = false;
        }

        // 当前 profile 变化后，清理上一次会话留下但不属于当前 pack 的接管记录。
        // 只处理 manifest 注册表里的前缀；未知键永远不猜、不碰。
        internal void RecoverStale(string activeProfileId)
        {
            BookRegistry registry = WcpHostPlugin.Instance == null ? null :
                WcpHostPlugin.Instance.Registry;
            RecoverStale(registry, activeProfileId);
        }

        internal void RecoverStale(BookRegistry registry, string activeProfileId)
        {
            if (registry == null) return;
            for (int i = 0; i < registry.Manifests.Count; i++)
            {
                LanguageManifest m = registry.Manifests[i];
                if (m.Profile.Id == activeProfileId) continue;
                RestoreOwned(m.Profile.Es3Prefix);
            }
        }

        internal void Enforce()
        {
            if (!_active || _manifest == null || _bookWords == null || _bookWords.Count == 0)
                return;

            HashSet<string> allowed = new HashSet<string>(_bookWords, StringComparer.Ordinal);
            for (int i = 0; i < ListFields.Length; i++)
                EnforceList(ListFields[i], allowed);
            for (int i = 0; i < ArrayFields.Length; i++)
                EnforceArray(ArrayFields[i], allowed);

            AlignTestQueue(allowed);
        }

        private void EnforceList(string fieldName, HashSet<string> allowed)
        {
            object raw = GameAdapter.StaticField("MyParameters", fieldName);
            IList<string> current = GameAdapter.ToWordList(raw);
            if (current == null) return;

            List<string> filtered = Filter(current, allowed);
            bool testing = IsLearnedTest();
            int minimum = fieldName == "S7TestWordList_Para" ? 4 :
                          fieldName == "allTestWordsS10_Para" && (testing || current.Count > 0) ? 5 : 0;
            if (minimum > 0 && filtered.Count < minimum)
                AddBookWords(filtered, allowed, minimum);

            bool changed = !Same(current, filtered);
            if (!changed) return;
            if (minimum > 0 && filtered.Count < minimum) return;
            if (!CaptureList(fieldName, current)) return;
            object replacement = ListValueForField(raw, filtered);
            GameAdapter.SetStaticField("MyParameters", fieldName, replacement);
            GameAdapter.Es3Save(fieldName, replacement);
        }

        private void EnforceArray(string fieldName, HashSet<string> allowed)
        {
            object raw = GameAdapter.StaticField("MyParameters", fieldName);
            string[] current = ToArray(raw);
            if (current == null) return;
            List<string> filtered = Filter(current, allowed);
            if (Same(current, filtered)) return;
            if (!CaptureArray(fieldName, current)) return;
            string[] value = filtered.ToArray();
            if (raw is string[])
                GameAdapter.SetStaticField("MyParameters", fieldName, value);
            else
                GameAdapter.SetStaticField("MyParameters", fieldName, value);
            GameAdapter.Es3Save(fieldName, value);
        }

        // 题干队列必须和测试词池的当前进度对齐。只在游戏已经进入已学词测试
        // 或者两张表本来就有内容时修正，避免给普通词书流程凭空制造测试状态。
        private void AlignTestQueue(HashSet<string> allowed)
        {
            if (!IsLearnedTest()) return;
            object poolRaw = GameAdapter.StaticField("MyParameters", "allTestWordsS10_Para");
            List<string> pool = GameAdapter.ToWordList(poolRaw) as List<string>;
            if (pool == null)
            {
                IList<string> any = GameAdapter.ToWordList(
                    poolRaw);
                if (any == null) return;
                pool = new List<string>(any);
            }
            if (pool.Count < 5) return;

            int progress = ReadInt("S8Progress_Para", 0);
            if (progress < 0 || progress >= pool.Count) progress = 0;
            List<string> expected = new List<string>();
            for (int i = progress; i < pool.Count; i++) expected.Add(pool[i]);

            object needRaw = GameAdapter.StaticField("MyParameters", "S8needToLearnWordList_Para");
            IList<string> current = GameAdapter.ToWordList(needRaw);
            if (current == null || current.Count == 0 || current[0] != pool[progress] ||
                !ContainsOnly(current, allowed))
            {
                List<string> old = current == null ? new List<string>() : new List<string>(current);
                if (!CaptureList("S8needToLearnWordList_Para", old)) return;
                object replacement = ListValueForField(needRaw, expected);
                GameAdapter.SetStaticField("MyParameters", "S8needToLearnWordList_Para", replacement);
                GameAdapter.Es3Save("S8needToLearnWordList_Para", replacement);
            }
        }

        private static object ListValueForField(object raw, IList<string> values)
        {
            if (raw is string[])
                return new List<string>(values).ToArray();
            return new List<string>(values);
        }

        private bool CaptureList(string fieldName, IList<string> current)
        {
            if (_ownedLists.Contains(fieldName)) return true;
            List<string> baseline = current == null ? new List<string>() : new List<string>(current);
            if (!GameAdapter.Es3Save(Key("bak_" + fieldName), baseline.ToArray())) return false;
            _ownedLists.Add(fieldName);
            if (!GameAdapter.Es3Save(Key("owned_lists"), ToArray(_ownedLists)))
            {
                _ownedLists.Remove(fieldName);
                return false;
            }
            _listBaselines[fieldName] = baseline;
            return true;
        }

        private bool CaptureArray(string fieldName, string[] current)
        {
            if (_ownedArrays.Contains(fieldName)) return true;
            string[] baseline = current == null ? new string[0] : (string[])current.Clone();
            if (!GameAdapter.Es3Save(Key("bak_" + fieldName), baseline)) return false;
            _ownedArrays.Add(fieldName);
            if (!GameAdapter.Es3Save(Key("owned_arrays"), ToArray(_ownedArrays)))
            {
                _ownedArrays.Remove(fieldName);
                return false;
            }
            _arrayBaselines[fieldName] = baseline;
            return true;
        }

        private void RestoreOwned(string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return;
            HashSet<string> lists = new HashSet<string>(LoadOwned(prefix + "_owned_lists"), StringComparer.Ordinal);
            HashSet<string> arrays = new HashSet<string>(LoadOwned(prefix + "_owned_arrays"), StringComparer.Ordinal);
            bool local = _manifest != null && _manifest.Profile.Es3Prefix == prefix;
            if (local)
            {
                lists.UnionWith(_ownedLists);
                arrays.UnionWith(_ownedArrays);
            }
            // An inactive host must be completely read-only when it has no
            // ownership marker.  Writing empty marker arrays on every probe
            // races legacy language plugins while they switch books.
            if (lists.Count == 0 && arrays.Count == 0) return;
            HashSet<string> pendingLists = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> pendingArrays = new HashSet<string>(StringComparer.Ordinal);
            bool restoredPool = false;
            foreach (string field in lists)
            {
                if (Array.IndexOf(ListFields, field) < 0) continue;
                List<string> baseline = null;
                if (local) _listBaselines.TryGetValue(field, out baseline);
                if (baseline == null)
                {
                    string[] saved = GameAdapter.Es3Load(prefix + "_bak_" + field,
                        typeof(string[]), null, null) as string[];
                    if (saved != null) baseline = new List<string>(saved);
                }
                if (baseline == null || !RestoreList(field, baseline)) pendingLists.Add(field);
                else if (field == "allTestWordsS10_Para") restoredPool = true;
            }
            foreach (string field in arrays)
            {
                if (Array.IndexOf(ArrayFields, field) < 0) continue;
                string[] baseline = null;
                if (local) _arrayBaselines.TryGetValue(field, out baseline);
                if (baseline == null) baseline = GameAdapter.Es3Load(prefix + "_bak_" + field,
                    typeof(string[]), null, null) as string[];
                if (baseline == null || !RestoreArray(field, baseline)) pendingArrays.Add(field);
            }
            if (restoredPool) MarkNoTestIfUnsafe();
            GameAdapter.Es3Save(prefix + "_owned_lists", ToArray(pendingLists));
            GameAdapter.Es3Save(prefix + "_owned_arrays", ToArray(pendingArrays));
        }

        private bool RestoreList(string fieldName, List<string> value)
        {
            object current = GameAdapter.StaticField("MyParameters", fieldName);
            object replacement;
            if (current is string[]) replacement = value.ToArray();
            else replacement = value;
            GameAdapter.SetStaticField("MyParameters", fieldName, replacement);
            return GameAdapter.Es3Save(fieldName, replacement);
        }

        private bool RestoreArray(string fieldName, string[] value)
        {
            GameAdapter.SetStaticField("MyParameters", fieldName,
                (string[])value.Clone());
            return GameAdapter.Es3Save(fieldName, (string[])value.Clone());
        }

        private string Key(string suffix)
        {
            return _manifest == null ? suffix : _manifest.Profile.Es3Prefix + "_" + suffix;
        }

        private static string[] LoadOwned(string key)
        {
            string[] value = GameAdapter.Es3Load(key, typeof(string[]), null, null) as string[];
            return value ?? new string[0];
        }

        private bool IsLearnedTest()
        {
            object mode = GameAdapter.StaticField("MyParameters", "S8ThisMode_Para");
            return string.Equals(mode as string, "已学词测试", StringComparison.Ordinal);
        }

        private void MarkNoTestIfUnsafe()
        {
            IList<string> pool = GameAdapter.ToWordList(
                GameAdapter.StaticField("MyParameters", "allTestWordsS10_Para"));
            if (pool != null && pool.Count >= 5) return;
            GameAdapter.SetStaticField("MyParameters", "S8Progress_Para", 0);
            GameAdapter.Es3Save("S8Progress_Para", 0);
            GameAdapter.SetStaticField("MyParameters", "testingIf_Para", false);
            GameAdapter.Es3Save("testingIf_Para", false);
            GameAdapter.SetStaticField("MyParameters", "testingIf_CompleteIf", true);
            GameAdapter.Es3Save("testingIf_CompleteIf", true);
        }

        private int ReadInt(string fieldName, int fallback)
        {
            object value = GameAdapter.StaticField("MyParameters", fieldName);
            if (value is int) return (int)value;
            return fallback;
        }

        private void AddBookWords(List<string> list, HashSet<string> allowed, int minimum)
        {
            for (int i = 0; i < _bookWords.Count && list.Count < minimum; i++)
            {
                string word = _bookWords[i];
                if (string.IsNullOrEmpty(word) || !allowed.Contains(word) || list.Contains(word)) continue;
                list.Add(word);
            }
        }

        private static List<string> Filter(IList<string> current, HashSet<string> allowed)
        {
            List<string> result = new List<string>();
            if (current == null) return result;
            for (int i = 0; i < current.Count; i++)
            {
                string word = current[i];
                if (!string.IsNullOrEmpty(word) && allowed.Contains(word) && !result.Contains(word))
                    result.Add(word);
            }
            return result;
        }

        private static string[] ToArray(object raw)
        {
            if (raw == null || raw is string) return null;
            if (raw is string[]) return (string[])((string[])raw).Clone();
            IEnumerable seq = raw as IEnumerable;
            if (seq == null) return null;
            List<string> result = new List<string>();
            try
            {
                foreach (object item in seq) result.Add(item == null ? null : item.ToString());
            }
            catch (Exception) { return null; }
            return result.ToArray();
        }

        private static bool ContainsOnly(IList<string> values, HashSet<string> allowed)
        {
            for (int i = 0; i < values.Count; i++)
                if (string.IsNullOrEmpty(values[i]) || !allowed.Contains(values[i])) return false;
            return true;
        }

        private static bool Same(IList<string> a, IList<string> b)
        {
            if (a == null || b == null || a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
            return true;
        }

        private static string[] ToArray(HashSet<string> values)
        {
            string[] result = new string[values.Count];
            values.CopyTo(result);
            Array.Sort(result, StringComparer.Ordinal);
            return result;
        }
    }
}
