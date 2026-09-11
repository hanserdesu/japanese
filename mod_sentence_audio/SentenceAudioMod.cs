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
        private float _nextScan;
        private string _audioDir;
        private Type _t8, _t17;
        private FieldInfo _f8, _f17;
        private object _s8, _s17;
        private readonly Dictionary<TMP_Text, GameObject> _buttons =
            new Dictionary<TMP_Text, GameObject>();

        void Awake()
        {
            Log = Logger;
            var go = new GameObject("SentenceAudioPlayer");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _audio = go.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.volume = 1f;
            _audioDir = Path.Combine(Application.persistentDataPath,
                AudioDirName);
            Log.LogInfo(string.Format(
                "WCP Sentence Audio 1.0.0 loaded, audio dir = {0}", _audioDir));
        }

        void Update()
        {
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + ScanInterval;
            try { ScanAll(); }
            catch (Exception e)
            {
                Log.LogWarning("scan failed: " + e.Message);
                _nextScan = Time.unscaledTime + 3f;
            }
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
            string url = "file:///" + file.Replace('\\', '/');
            var www = UnityWebRequestMultimedia.GetAudioClip(url,
                AudioType.MPEG);
            yield return www.SendWebRequest();
            if (!string.IsNullOrEmpty(www.error))
            {
                Log.LogWarning("audio load failed: " + www.error + " " + file);
                yield break;
            }
            var clip = DownloadHandlerAudioClip.GetContent(www);
            if (clip == null)
            {
                Log.LogWarning("audio clip null: " + file);
                yield break;
            }
            _audio.Stop();
            _audio.PlayOneShot(clip);
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
}
