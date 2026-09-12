// 一次性诊断 (读 BepInEx/diag.flag): 把「读例句按钮挂载了什么」和
// 「UK/US 标签由谁写」导出到 BepInEx/diag.txt。定位完成后可删除本文件。
using System;
using System.Reflection;
using System.Text;
using BepInEx;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WcpBookName
{
    internal static class Diag
    {
        private static readonly string[] Accents = { "UK", "US", "美", "英" };
        private static string _path;
        internal static bool Enabled;

        // 只有在真正抓到界面实例时才写文件, 且每次覆盖
        internal static int LastCount = -1;

        internal static void Dump()
        {
            try
            {
                _path = System.IO.Path.Combine(Paths.BepInExRootPath, "diag.txt");
                System.IO.File.Delete(_path);
                Line("=== dump " + DateTime.Now.ToString("HH:mm:ss") + " ===");
                DumpAccentLabels();
                int n = DumpReadButtons();
                DumpSentenceSources();
                Line("=== end ===\n");
                if (n == 0 && LastCount <= 0)
                {
                    // 还没进词页: 不留无内容的结果, 下次再试
                    System.IO.File.Delete(_path);
                }
                LastCount = n;
            }
            catch (Exception e)
            {
                try { Line("dump error: " + e); } catch (Exception) { }
            }
        }

        private static void Line(string s)
        {
            System.IO.File.AppendAllText(_path, s + "\n");
        }

        private static string PathOf(Transform t)
        {
            var sb = new StringBuilder();
            int guard = 0;
            while (t != null && guard++ < 12)
            {
                sb.Insert(0, "/" + t.name);
                t = t.parent;
            }
            return sb.ToString();
        }

        private static bool IsAccent(string s)
        {
            if (s == null) return false;
            s = s.Trim();
            for (int i = 0; i < Accents.Length; i++)
                if (s == Accents[i]) return true;
            return false;
        }

        private static void DumpAccentLabels()
        {
            Line("--- accent labels ---");
            var tmps = Resources.FindObjectsOfTypeAll(typeof(TMP_Text));
            for (int i = 0; i < tmps.Length; i++)
            {
                var t = tmps[i] as TMP_Text;
                if (t == null || !IsAccent(t.text)) continue;
                Line(string.Format("TMP '{0}' active={1} path={2}",
                    t.text, t.gameObject.activeInHierarchy, PathOf(t.transform)));
            }
            var txts = Resources.FindObjectsOfTypeAll(typeof(Text));
            for (int i = 0; i < txts.Length; i++)
            {
                var t = txts[i] as Text;
                if (t == null || !IsAccent(t.text)) continue;
                Line(string.Format("TXT '{0}' active={1} path={2}",
                    t.text, t.gameObject.activeInHierarchy, PathOf(t.transform)));
            }
        }

        private static int DumpReadButtons()
        {
            Line("--- read buttons ---");
            var t = Type.GetType("ShowReadButtons, Assembly-CSharp");
            if (t == null) { Line("ShowReadButtons NOT FOUND"); return 0; }
            var f = t.GetField("ReadButtons",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var all = Resources.FindObjectsOfTypeAll(t);
            Line("ShowReadButtons instances: " + all.Length);
            int found = 0;
            for (int k = 0; k < all.Length; k++)
            {
                var comp = all[k] as Component;
                if (comp == null) continue;
                found++;
                Line(" instance " + k + " path=" + PathOf(comp.transform)
                     + " active=" + comp.gameObject.activeInHierarchy);
                var arr = f == null ? null : f.GetValue(comp) as Button[];
                if (arr == null) { Line("  ReadButtons null"); continue; }
                for (int i = 0; i < arr.Length; i++)
                {
                    var b = arr[i];
                    if (b == null) { Line("  [" + i + "] null"); continue; }
                    var lbl = b.GetComponentInChildren<TMP_Text>(true);
                    var comps = b.GetComponents<Component>();
                    var cs = new StringBuilder();
                    for (int c = 0; c < comps.Length; c++)
                    {
                        if (comps[c] == null) continue;
                        cs.Append(comps[c].GetType().Name).Append(",");
                    }
                    int n = b.onClick.GetPersistentEventCount();
                    var ev = new StringBuilder();
                    for (int e = 0; e < n; e++)
                    {
                        ev.Append(" {").Append(b.onClick.GetPersistentTarget(e))
                          .Append(".").Append(b.onClick.GetPersistentMethodName(e))
                          .Append("}");
                    }
                    Line(string.Format(
                        "  [{0}] '{1}' active={2} label='{3}' persistent={4}{5} comps={6}",
                        i, b.name, b.gameObject.activeInHierarchy,
                        lbl == null ? "<null>" : lbl.text, n, ev.ToString(), cs.ToString()));
                }
            }
            return found;
        }

        private static void DumpSentenceSources()
        {
            Line("--- sentence sources ---");
            var mp = Type.GetType("MyParameters, Assembly-CSharp");
            if (mp != null)
            {
                var fw = mp.GetField("checkWordInDictionary",
                    BindingFlags.Public | BindingFlags.Static);
                if (fw != null) Line("checkWordInDictionary=" + fw.GetValue(null));
                var fl = mp.GetField("exaple_sentences",
                    BindingFlags.Public | BindingFlags.Static);
                var list = fl == null ? null : fl.GetValue(null) as System.Collections.IList;
                if (list != null)
                {
                    Line("exaple_sentences count=" + list.Count);
                    for (int i = 0; i < list.Count; i++)
                        Line("  [" + i + "] " + list[i]);
                }
            }
            DumpManager("DatabaseManagerS8");
            DumpManager("DatabaseManagerS17");
        }

        private static void DumpManager(string name)
        {
            var t = Type.GetType(name + ", Assembly-CSharp");
            if (t == null) { Line(name + " NOT FOUND"); return; }
            var f = t.GetField("exmplesentences",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var all = Resources.FindObjectsOfTypeAll(t);
            Line(name + " instances: " + all.Length);
            for (int k = 0; k < all.Length; k++)
            {
                var comp = all[k] as Component;
                if (comp == null) continue;
                Line("  instance " + k + " path=" + PathOf(comp.transform)
                     + " active=" + comp.gameObject.activeInHierarchy);
                var arr = f == null ? null : f.GetValue(comp) as TMP_Text[];
                if (arr == null) { Line("    exmplesentences null"); continue; }
                for (int i = 0; i < arr.Length; i++)
                {
                    if (arr[i] == null) { Line("    [" + i + "] null"); continue; }
                    var s = arr[i].text;
                    if (s != null && s.Length > 60) s = s.Substring(0, 60);
                    Line("    [" + i + "] active=" + arr[i].gameObject.activeInHierarchy
                         + " '" + s + "'");
                }
            }
        }
    }
}
