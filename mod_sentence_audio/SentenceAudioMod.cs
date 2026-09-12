// WCP Sentence Audio — BepInEx 5 插件
// 功能: 在 每日学习(DatabaseManagerS8) 与 词典查询(DatabaseManagerS17) 的
// 例句旁挂 ▶ 按钮, 点击播放 sentence_audio/<md5(ja)>.mp3 (由
// wcp_wordbooks/tools/gen_sentence_audio.py 生成, 文件名规则两端一致)。
// 设计: 不 Harmony 补丁游戏方法, 每 0.3s 反射扫描 exmplesentences 数组,
// 游戏更新导致类型变化时自动降级为不显示, 不影响原游戏逻辑。
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace SentenceAudioMod
{
    [BepInPlugin("dev.hanserdesu.sentaudio", "WCP Sentence Audio", "1.0.0")]
    public class SentenceAudioPlugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        private const float ScanInterval = 0.3f;
        private const string AudioDirName = "sentence_audio";

        private AudioSource _audio;
        private ConfigEntry<bool> _enabled;
        private bool _diagPending;
        private bool _autoPlayPending;
        private bool _autoPlayDone;
        private float _diagAt;
        private float _diagDeadline;
        private float _nextScan;
        private string _audioDir;
        private readonly Dictionary<Button, ReadBtnState> _readStates =
            new Dictionary<Button, ReadBtnState>();
        private bool _gameButtonsActive;
        private static FieldInfo _fSentences;
        private Type _t8, _t17;
        private FieldInfo _f8, _f17;
        private object _s8, _s17;
        private readonly Dictionary<TMP_Text, GameObject> _buttons =
            new Dictionary<TMP_Text, GameObject>();

        void Awake()
        {
            Log = Logger;
            try
            {
                File.AppendAllText(Path.Combine(Paths.BepInExRootPath, "diag.txt"),
                    DateTime.Now.ToString("HH:mm:ss") + " awake start\n");
            }
            catch (Exception) { }
            var go = new GameObject("SentenceAudioPlayer");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _audio = go.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.volume = 1f;
            _audio.spatialBlend = 0f;   // 2D, 无视听位置衰减
            _enabled = Config.Bind("General", "Enabled", true,
                "显示例句旁的 ▶ 朗读按钮。");
            _audioDir = Path.Combine(Application.persistentDataPath,
                AudioDirName);
            Log.LogInfo(string.Format(
                "WCP Sentence Audio 1.0.0 loaded, audio dir = {0}", _audioDir));
            var bepRoot = Paths.BepInExRootPath;
            try
            {
                File.AppendAllText(Path.Combine(bepRoot, "diag.txt"),
                    string.Format("bepRoot={0} readbtn={1} audiotest={2}\n",
                        bepRoot, File.Exists(Path.Combine(bepRoot, "readbtn.flag")),
                        File.Exists(Path.Combine(bepRoot, "audiotest.flag"))));
            }
            catch (Exception) { }
            var flag = Path.Combine(bepRoot, "selftest.flag");
            if (File.Exists(flag))
            {
                File.Delete(flag);   // 一次性: 读到即删, 只在下一次启动生效
                StartCoroutine(SelfTest());
            }
            // 播放链路自检 (BepInEx/audiotest.flag): 按钮出现后自动点一次 ▶,
            // 用来在无人操作的情况下复现/验证播放路径是否稳定
            var atest = Path.Combine(bepRoot, "audiotest.flag");
            if (File.Exists(atest))
            {
                File.Delete(atest);
                _autoPlayPending = true;
                Log.LogInfo("AUDIOTEST armed");
            }
            // 只读诊断 (BepInEx/readbtn.flag): 打印游戏自带的"读例句"按钮
            // 挂载了哪些 onClick 目标 + 标签文本, 用来确定接管方式
            var rflag = Path.Combine(bepRoot, "readbtn.flag");
            if (File.Exists(rflag))
            {
                File.Delete(rflag);
                _diagPending = true;
                _diagAt = Time.unscaledTime + 15f;
                _diagDeadline = Time.unscaledTime + 900f;
                Log.LogInfo("READBTN armed");
                try
                {
                    File.AppendAllText(Path.Combine(bepRoot, "diag.txt"),
                        "readbtn branch entered\n");
                }
                catch (Exception) { }
            }
        }

        private bool DumpReadButtonsNow()
        {
            Log.LogInfo("READBTN dump start");
            bool found = false;
            try
            {
                var t = FindGameType("ShowReadButtons");
                Log.LogInfo("READBTN type: " + (t == null ? "NOT FOUND" : t.FullName));
                if (t != null)
                {
                    var all = Resources.FindObjectsOfTypeAll(t);
                    Log.LogInfo("READBTN instances: " + all.Length);
                    if (all.Length > 0) found = true;
                    for (int i = 0; i < all.Length; i++)
                    {
                        var c = all[i] as Component;
                        if (c == null) continue;
                        Log.LogInfo(string.Format("  [{0}] {1} active={2}",
                            i, c.name, c.gameObject.activeInHierarchy));
                        var f = t.GetField("ReadButtons",
                            BindingFlags.Public | BindingFlags.NonPublic |
                            BindingFlags.Instance);
                        var arr = f == null ? null : f.GetValue(c) as Button[];
                        if (arr == null) { Log.LogInfo("    ReadButtons null"); continue; }
                        for (int j = 0; j < arr.Length; j++)
                        {
                            var b = arr[j];
                            if (b == null) { Log.LogInfo("    [" + j + "] null"); continue; }
                            var lbl = b.GetComponentInChildren<TMP_Text>();
                            int n = b.onClick.GetPersistentEventCount();
                            var sb = new StringBuilder();
                            for (int k = 0; k < n; k++)
                            {
                                sb.Append(" {").Append(k).Append(":")
                                  .Append(b.onClick.GetPersistentTarget(k))
                                  .Append(".")
                                  .Append(b.onClick.GetPersistentMethodName(k))
                                  .Append("}");
                            }
                            Log.LogInfo(string.Format(
                                "    [{0}] '{1}' active={2} label='{3}' persistent[{4}]{5}",
                                j, b.name, b.gameObject.activeInHierarchy,
                                lbl == null ? "<null>" : lbl.text, n, sb.ToString()));
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.LogError("READBTN error: " + e);
            }
            Log.LogInfo("READBTN dump done");
            return found;
        }

        // 一次性自检 (仅当 BepInEx/selftest.flag 存在):
        // 反射调用词典查询 S17::OnSearchButtonClick("低い"), 验证
        // 例句填充 -> ja 提取 -> md5 -> 音频文件命中 全链路, 并把面板激活
        // 以便截图确认按钮外观。
        private IEnumerator SelfTest()
        {
            yield return new WaitForSeconds(20f);
            Log.LogInfo("SELFTEST start");
            try
            {
                if (_t17 == null || _f17 == null) { Log.LogInfo("SELFTEST: S17 type/field missing"); yield break; }
                var all = Resources.FindObjectsOfTypeAll(_t17);
                Log.LogInfo("SELFTEST S17 instances (incl inactive): " + all.Length);
                object inst = null;
                foreach (var o in all)
                {
                    var c = o as Component;
                    if (c == null) continue;
                    Log.LogInfo(string.Format("  S17 '{0}' active={1}", c.name, c.gameObject.activeInHierarchy));
                    if (inst == null) inst = o;
                }
                if (inst == null) { Log.LogInfo("SELFTEST: no S17 instance"); yield break; }
                var comp = inst as Component;
                var method = _t17.GetMethod("OnSearchButtonClick",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null, new Type[] { typeof(string) }, null);
                if (method == null)
                {
                    foreach (var m in _t17.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                        if (m.Name == "OnSearchButtonClick")
                            Log.LogInfo("  OnSearchButtonClick sig: " + m.ToString());
                    Log.LogInfo("SELFTEST: OnSearchButtonClick(string) not found");
                    yield break;
                }
                var compTr = comp.GetComponent(_t17);
                method.Invoke(compTr, new object[] { "低い" });
                Log.LogInfo("SELFTEST: OnSearchButtonClick(低い) invoked");
                var arr = _f17.GetValue(compTr) as TMP_Text[];
                if (arr == null) { Log.LogInfo("SELFTEST: exmplesentences null"); yield break; }
                Log.LogInfo("SELFTEST exmplesentences: " + arr.Length);
                int hits = 0;
                for (int i = 0; i < arr.Length; i++)
                {
                    var tmp = arr[i];
                    if (tmp == null) continue;
                    string ja = ExtractJa(tmp.text);
                    string file = null;
                    if (ja != null)
                    {
                        string p = Path.Combine(_audioDir, Md5(ja) + ".mp3");
                        if (File.Exists(p)) { file = p; hits++; }
                    }
                    Log.LogInfo(string.Format("  [{0}] ja='{1}' audio={2}",
                        i, ja == null ? "<none>" : (ja.Length > 40 ? ja.Substring(0, 40) : ja),
                        file != null ? "OK" : "MISSING"));
                }
                Log.LogInfo("SELFTEST audio hits: " + hits);
                if (!comp.gameObject.activeInHierarchy)
                {
                    Log.LogInfo("SELFTEST: activating panel chain");
                    var t = comp.transform;
                    while (t != null)
                    {
                        if (!t.gameObject.activeSelf) { t.gameObject.SetActive(true); Log.LogInfo("  activated: " + t.name); }
                        t = t.parent;
                    }
                }
                Log.LogInfo("SELFTEST done");
            }
            catch (Exception e)
            {
                Log.LogWarning("SELFTEST error: " + e);
            }
        }

        void Update()
        {
            if (_enabled != null && !_enabled.Value) return;
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + ScanInterval;
            try { ScanAll(); }
            catch (Exception e)
            {
                Log.LogWarning("scan failed: " + e.Message);
                _nextScan = Time.unscaledTime + 3f;
            }
            try
            {
                if (_diagPending && Time.unscaledTime >= _diagAt)
                {
                    // 可能还没进到学习界面: 每 10s 重试, 直到找到实例
                    if (DumpReadButtonsNow()) _diagPending = false;
                    else
                    {
                        _diagAt = Time.unscaledTime + 10f;
                        if (Time.unscaledTime > _diagDeadline)
                        {
                            _diagPending = false;
                            Log.LogWarning("READBTN: 从未找到 ShowReadButtons 实例");
                        }
                    }
                }
                if (_autoPlayPending && !_autoPlayDone)
                {
                    foreach (var kv in _buttons)
                    {
                        if (kv.Key == null || kv.Value == null) continue;
                        if (!kv.Value.activeSelf) continue;
                        var spb = kv.Value.GetComponent<SentencePlayButton>();
                        if (spb == null || string.IsNullOrEmpty(spb.file)) continue;
                        _autoPlayDone = true;
                        Log.LogInfo("AUDIOTEST: invoking play for " + spb.file);
                        spb.Play();
                        break;
                    }
                }
            }
            catch (Exception e) { Log.LogError("diag failed: " + e); }
        }

        private void ScanAll()
        {
            if (_t8 == null)
            {
                _t8 = FindGameType("DatabaseManagerS8");
                if (_t8 != null)
                    _f8 = _t8.GetField("exmplesentences",
                        BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Instance);
            }
            if (_t17 == null)
            {
                _t17 = FindGameType("DatabaseManagerS17");
                if (_t17 != null)
                    _f17 = _t17.GetField("exmplesentences",
                        BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Instance);
            }
            if (_s8 == null && _t8 != null) _s8 = FindObjectOfType(_t8);
            if (_s17 == null && _t17 != null) _s17 = FindObjectOfType(_t17);
            if (_s8 == null && _s17 == null) return;
            if (_f8 == null && _f17 == null) return;

            if (_s8 != null && _f8 != null)
                Scan(_f8.GetValue(_s8) as TMP_Text[]);
            if (_s17 != null && _f17 != null)
                Scan(_f17.GetValue(_s17) as TMP_Text[]);
            CleanupDestroyed();
            TakeOverGameReadButtons();
        }

        // 把游戏自带的"读例句"按钮改造成日语朗读 + JP 标签。
        // 只有该句有本地日语 mp3 时才接管, 否则完整保留游戏原行为。
        private void TakeOverGameReadButtons()
        {
            try
            {
                var t = FindGameType("ShowReadButtons");
                if (t == null) return;
                var f = t.GetField("ReadButtons",
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance);
                if (f == null) return;
                if (_fSentences == null)
                {
                    var mp = FindGameType("MyParameters");
                    if (mp != null)
                        _fSentences = mp.GetField("exaple_sentences",
                            BindingFlags.Public | BindingFlags.Static);
                }
                System.Collections.IList sentences = null;
                if (_fSentences != null)
                {
                    try { sentences = _fSentences.GetValue(null) as System.Collections.IList; }
                    catch (Exception) { }
                }
                var all = Resources.FindObjectsOfTypeAll(t);
                bool any = false;
                for (int k = 0; k < all.Length; k++)
                {
                    var comp = all[k] as Component;
                    if (comp == null) continue;
                    var arr = f.GetValue(comp) as Button[];
                    if (arr == null) continue;
                    for (int i = 0; i < arr.Length; i++)
                    {
                        var b = arr[i];
                        if (b == null) continue;
                        any = true;
                        if (_takenOver.Contains(b)) continue;
                        if (b.GetComponent<JpReadTag>() != null)
                        {
                            _takenOver.Add(b);
                            continue;
                        }
                        string file = LocalAudio(SentenceAt(sentences, i));
                        if (file == null) continue;   // 无本地日语音频 -> 保留原样
                        _takenOver.Add(b);
                        b.gameObject.AddComponent<JpReadTag>();
                        var f2 = file;
                        b.onClick.RemoveAllListeners();
                        b.onClick.AddListener(delegate { PlayFile(f2); });
                        Relabel(b, i);
                        Diag("took over read button " + i + " -> "
                             + Path.GetFileName(f2));
                    }
                }
                _gameButtonsActive = any;
            }
            catch (Exception e) { Diag("takeover error: " + e.Message); }
        }

        private string SentenceAt(System.Collections.IList sentences, int i)
        {
            if (sentences != null && i < sentences.Count)
            {
                var s = sentences[i] as string;
                if (!string.IsNullOrEmpty(s)) return s;
            }
            if (_s8 != null && _f8 != null)
            {
                var arr = _f8.GetValue(_s8) as TMP_Text[];
                if (arr != null && i < arr.Length && arr[i] != null)
                    return arr[i].text;
            }
            return null;
        }

        // 由例句文本(或 TMP 富文本)定位本地日语 mp3
        private string LocalAudio(string raw)
        {
            string ja = ExtractJa(raw);
            if (ja == null) return null;
            string p = Path.Combine(_audioDir, Md5(ja) + ".mp3");
            return File.Exists(p) ? p : null;
        }

        // 标签 "读例句N" -> "JPN", 其余文字(如快捷键号)保留
        private void Relabel(Button b, int i)
        {
            var texts = b.GetComponentsInChildren<TMP_Text>(true);
            for (int j = 0; j < texts.Length; j++)
            {
                var s = texts[j].text;
                if (string.IsNullOrEmpty(s)) continue;
                if (s.IndexOf("读例句", StringComparison.Ordinal) >= 0)
                {
                    texts[j].text = s.Replace("读例句", "JP");
                    Diag("relabel " + i + ": '" + s + "' -> '" + texts[j].text + "'");
                }
            }
        }

        internal static void Diag(string msg)
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(Paths.BepInExRootPath, "diag.txt"),
                    DateTime.Now.ToString("HH:mm:ss") + " " + msg + "\n");
            }
            catch (Exception) { }
        }

        private void Scan(TMP_Text[] arr)
        {
            if (arr == null) return;
            var seen = new HashSet<TMP_Text>();
            for (int i = 0; i < arr.Length; i++)
            {
                var tmp = arr[i];
                if (tmp == null) continue;
                seen.Add(tmp);
                // 游戏自带"读例句"按钮已接管时, 不再重复挂自己的 ▶ 按钮
                if (_gameButtonsActive)
                {
                    GameObject owned;
                    if (_buttons.TryGetValue(tmp, out owned) && owned != null &&
                        owned.activeSelf)
                        owned.SetActive(false);
                    continue;
                }
                string ja = null;
                if (tmp.gameObject.activeInHierarchy)
                    ja = ExtractJa(tmp.text);
                string file = null;
                if (ja != null)
                {
                    string p = Path.Combine(_audioDir, Md5(ja) + ".mp3");
                    if (File.Exists(p)) file = p;
                }
                GameObject btn;
                if (!_buttons.TryGetValue(tmp, out btn) || btn == null)
                {
                    if (file == null) continue;
                    btn = CreateButton(tmp);
                    _buttons[tmp] = btn;
                }
                bool show = file != null;
                if (btn.activeSelf != show) btn.SetActive(show);
                if (show)
                {
                    var spb = btn.GetComponent<SentencePlayButton>();
                    if (spb.file != file) spb.file = file;
                }
            }
            // 数组里已不出现的 TMP → 藏按钮
            foreach (var kv in _buttons)
            {
                if (kv.Key == null) continue;
                if (!seen.Contains(kv.Key) && kv.Value != null &&
                    kv.Value.activeSelf)
                    kv.Value.SetActive(false);
            }
        }

        private void CleanupDestroyed()
        {
            List<TMP_Text> dead = null;
            foreach (var kv in _buttons)
            {
                if (kv.Key == null)
                {
                    if (dead == null) dead = new List<TMP_Text>();
                    dead.Add(kv.Key);
                }
            }
            if (dead == null) return;
            for (int i = 0; i < dead.Count; i++) _buttons.Remove(dead[i]);
        }

        private GameObject CreateButton(TMP_Text tmp)
        {
            var go = new GameObject("SentenceAudioBtn",
                typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(tmp.gameObject.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(2f, 0f);
            rt.sizeDelta = new Vector2(30f, 30f);
            var img = go.GetComponent<Image>();
            img.color = new Color(0.20f, 0.55f, 0.95f, 0.95f);
            try
            {
                var font = Resources.GetBuiltinResource<Font>(
                    "LegacyRuntime.ttf");
                var tgo = new GameObject("label", typeof(RectTransform),
                    typeof(Text));
                tgo.transform.SetParent(go.transform, false);
                var trt = (RectTransform)tgo.transform;
                trt.anchorMin = Vector2.zero;
                trt.anchorMax = Vector2.one;
                trt.offsetMin = Vector2.zero;
                trt.offsetMax = Vector2.zero;
                var t = tgo.GetComponent<Text>();
                t.text = "▶";
                t.alignment = TextAnchor.MiddleCenter;
                t.color = Color.white;
                t.fontSize = 17;
                t.font = font;
                t.raycastTarget = false;
            }
            catch (Exception e)
            {
                Log.LogWarning("button label skipped: " + e.Message);
            }
            var spb = go.AddComponent<SentencePlayButton>();
            spb.owner = this;
            go.GetComponent<Button>().onClick.AddListener(spb.Play);
            Log.LogInfo("sentence button created for: " + tmp.name);
            return go;
        }

        internal void PlayFile(string file)
        {
            StartCoroutine(LoadAndPlay(file));
        }

        private IEnumerator LoadAndPlay(string file)
        {
            // 与游戏 VocabularyAudioPlayer.LoadAndPlayAudio 同构:
            // AudioSource.clip + Play() (游戏自身已验证可用), 不用 PlayOneShot。
            string url = "file:///" + file.Replace('\\', '/');
            UnityWebRequest www = null;
            try
            {
                www = UnityWebRequestMultimedia.GetAudioClip(url,
                    AudioType.MPEG);
            }
            catch (Exception e)
            {
                Log.LogWarning("audio request failed: " + e.Message + " " + file);
                yield break;
            }
            yield return www.SendWebRequest();
            bool ok = false;
            string err = null;
            try
            {
                ok = www.result == UnityWebRequest.Result.Success;
                err = www.error;
            }
            catch (Exception e) { err = e.Message; }
            if (!ok)
            {
                Log.LogWarning("audio load failed: " + err + " " + file);
                www.Dispose();
                yield break;
            }
            AudioClip clip = null;
            try { clip = DownloadHandlerAudioClip.GetContent(www); }
            catch (Exception e) { Log.LogWarning("decode failed: " + e.Message); }
            if (clip == null)
            {
                Log.LogWarning("audio clip null: " + file);
                www.Dispose();
                yield break;
            }
            try
            {
                _audio.Stop();
                _audio.clip = clip;
                _audio.Play();
                Log.LogInfo("playing " + Path.GetFileName(file));
            }
            catch (Exception e)
            {
                Log.LogWarning("play failed: " + e.Message);
            }
            www.Dispose();
        }

        // "ja（zh）" / TMP 标记 / "例句：" 前缀 → 还原出纯 ja 文本
        internal static string ExtractJa(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            string s = Regex.Replace(raw, "<[^>]+>", "");
            s = s.Replace("例句：", "").Trim();
            if (s.Length == 0) return null;
            int nl = s.IndexOf('\n');
            if (nl >= 0) s = s.Substring(0, nl).Trim();
            if (s.EndsWith("）"))
            {
                int i = s.LastIndexOf('（');
                if (i > 0) s = s.Substring(0, i).Trim();
            }
            if (s.Length < 2) return null;
            bool hasJp = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if ((c >= 0x3040 && c <= 0x30FF) ||
                    (c >= 0x4E00 && c <= 0x9FFF)) { hasJp = true; break; }
            }
            return hasJp ? s : null;
        }

        internal static string Md5(string s)
        {
            using (var md5 = MD5.Create())
            {
                var h = md5.ComputeHash(Encoding.UTF8.GetBytes(s));
                var sb = new StringBuilder(32);
                for (int i = 0; i < h.Length; i++)
                    sb.Append(h[i].ToString("x2"));
                return sb.ToString();
            }
        }

        private static Type FindGameType(string name)
        {
            var t = Type.GetType(name + ", Assembly-CSharp");
            if (t != null) return t;
            var asms = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < asms.Length; i++)
            {
                t = asms[i].GetType(name);
                if (t != null) return t;
            }
            return null;
        }
    }

    public class SentencePlayButton : MonoBehaviour
    {
        public string file;
        public SentenceAudioPlugin owner;

        public void Play()
        {
            if (!string.IsNullOrEmpty(file) && owner != null)
                owner.PlayFile(file);
        }
    }

    // 标记: 该按钮已被改造成日语朗读 (避免重复接管)
    public class JpReadTag : MonoBehaviour
    {
    }
}
