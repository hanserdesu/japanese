// WCP Host Core — 语言包清单与身份注册表
//
// 与 mod_book_name/BookProfiles.cs 的关系:
//   · 指纹算法（SHA-256 over 排序后的全词形集合，'\n' 连接）**逐字节保持不变**，
//     见 FingerprintOf()。任何改动都会让已导入的词书全部失配。
//   · 区别在于注册表不再硬编码进 DLL，而是从 packs/*/manifest.json 读。
//     这就是"加一种语言不需要重编译其它语言的 DLL"的实现。
//
// 关键语义（2026-09-14 实测确认）:
//   一个语言包 = 一个 SlotProfile = 一条 BookProfile。
//   manifest.books 里可以列多个 xlsx 片段（法语/俄语各 3 个分册），
//   游戏导入后并成一个槽位，fingerprint 是对**并集**算的。
//   实测存档: 槽1=7922(ja) 槽2=8116(fr) 槽3=8451(ru) 槽4=空。
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace WcpHost
{
    internal sealed class BookProfile
    {
        internal readonly string Id;
        internal readonly string Language;
        internal readonly string DisplayName;
        internal readonly int WordCount;
        internal readonly string Fingerprint;
        internal readonly int ObservedSlot;
        internal readonly string Es3Prefix;

        internal BookProfile(string id, string language, string displayName, int wordCount,
                             string fingerprint, int observedSlot, string es3Prefix)
        {
            Id = id;
            Language = language;
            DisplayName = displayName;
            WordCount = wordCount;
            Fingerprint = fingerprint;
            ObservedSlot = observedSlot;
            Es3Prefix = es3Prefix;
        }
    }

    internal sealed class LanguageManifest
    {
        internal int Schema;
        internal BookProfile Profile;
        internal string PackRoot;                    // 本 pack 目录（绝对路径）
        internal string ManifestPath;
        internal List<string> Books = new List<string>();
        internal string MeaningDb;
        internal string SentenceTable;
        internal string Repair;
        internal string WordAudioDir;
        internal string SentenceAudioDir;
        internal List<string> RepairProbes = new List<string>();
        internal string StrategyAssembly;
        internal string StrategyType;

        internal static LanguageManifest Load(string manifestPath)
        {
            string json = File.ReadAllText(manifestPath, Encoding.UTF8);
            Dictionary<string, object> root = Json.AsDict(Json.Parse(json));

            LanguageManifest m = new LanguageManifest();
            m.ManifestPath = manifestPath;
            m.PackRoot = Path.GetDirectoryName(Path.GetFullPath(manifestPath));
            m.Schema = Json.Int(root, "schema", 0);

            m.Profile = new BookProfile(
                Json.Str(root, "profile_id", null),
                Json.Str(root, "language", null),
                Json.Str(root, "display_name", null),
                Json.Int(root, "word_count", 0),
                Json.Str(root, "fingerprint_sha256", null),
                Json.Int(root, "observed_slot", 0),
                Json.Str(root, "es3_prefix", Json.Str(root, "language", "xx")));

            if (m.Profile.Id == null || m.Profile.Language == null || m.Profile.Fingerprint == null)
                throw new FormatException("manifest 缺 profile_id/language/fingerprint_sha256: " + manifestPath);

            Dictionary<string, object> res = Json.Sub(root, "resources");
            m.Books = Json.StrList(res, "books");
            m.MeaningDb = Json.Str(res, "meaning_db", null);
            m.SentenceTable = Json.Str(res, "sentence_table", null);
            m.Repair = Json.Str(res, "repair", null);
            m.WordAudioDir = Json.Str(res, "word_audio", null);
            m.SentenceAudioDir = Json.Str(res, "sentence_audio", null);

            m.RepairProbes = Json.StrList(root, "repair_probes");

            Dictionary<string, object> st = Json.Sub(root, "strategy");
            m.StrategyAssembly = Json.Str(st, "assembly", null);
            m.StrategyType = Json.Str(st, "type", null);
            return m;
        }

        internal string Resolve(string relative)
        {
            if (string.IsNullOrEmpty(relative)) return null;
            if (Path.IsPathRooted(relative)) return relative;
            return Path.GetFullPath(Path.Combine(PackRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        }

        internal bool Matches(IList<string> words)
        {
            if (words == null || words.Count == 0) return false;
            if (words.Count != Profile.WordCount) return false;
            return BookRegistry.FingerprintOf(words) == Profile.Fingerprint;
        }
    }

    internal sealed class BookRegistry
    {
        private readonly List<LanguageManifest> _manifests = new List<LanguageManifest>();
        private readonly List<string> _errors = new List<string>();

        internal IList<LanguageManifest> Manifests { get { return _manifests; } }
        internal IList<string> Errors { get { return _errors; } }

        // 扫描 <packsRoot>/<lang>/manifest.json。单个 pack 坏掉不影响其它 pack 加载
        // —— 这是宿主对语言包唯一的容错要求：一种语言的数据坏了，别的语言照常用。
        internal static BookRegistry Load(string packsRoot)
        {
            BookRegistry r = new BookRegistry();
            if (string.IsNullOrEmpty(packsRoot) || !Directory.Exists(packsRoot))
            {
                r._errors.Add("packs 根目录不存在: " + packsRoot);
                return r;
            }
            string[] dirs = Directory.GetDirectories(packsRoot);
            Array.Sort(dirs, StringComparer.Ordinal);
            for (int i = 0; i < dirs.Length; i++)
            {
                string mp = Path.Combine(dirs[i], "manifest.json");
                if (!File.Exists(mp)) continue;
                try
                {
                    LanguageManifest m = LanguageManifest.Load(mp);
                    for (int j = 0; j < r._manifests.Count; j++)
                        if (r._manifests[j].Profile.Id == m.Profile.Id)
                            throw new FormatException("profile_id 重复: " + m.Profile.Id);
                    r._manifests.Add(m);
                }
                catch (Exception e)
                {
                    r._errors.Add(Path.GetFileName(dirs[i]) + ": " + e.Message);
                }
            }
            return r;
        }

        // 与 BookProfiles.Match 语义一致（先比词数再算哈希，避免无谓的重哈希）
        internal BookProfile Match(IList<string> words)
        {
            if (words == null || words.Count == 0) return null;
            string hash = null;
            for (int i = 0; i < _manifests.Count; i++)
            {
                BookProfile p = _manifests[i].Profile;
                if (words.Count != p.WordCount) continue;
                if (hash == null) hash = FingerprintOf(words);
                if (hash == p.Fingerprint) return p;
            }
            return null;
        }

        internal LanguageManifest ByProfileId(string id)
        {
            for (int i = 0; i < _manifests.Count; i++)
                if (_manifests[i].Profile.Id == id) return _manifests[i];
            return null;
        }

        internal LanguageManifest ByLanguage(string lang)
        {
            for (int i = 0; i < _manifests.Count; i++)
                if (_manifests[i].Profile.Language == lang) return _manifests[i];
            return null;
        }

        // 槽位预算：一个语言包 = 一个槽，游戏硬上限 4。
        internal bool SlotBudgetOk(out string detail)
        {
            HashSet<int> used = new HashSet<int>();
            for (int i = 0; i < _manifests.Count; i++)
            {
                int s = _manifests[i].Profile.ObservedSlot;
                if (s > 0) used.Add(s);
            }
            detail = _manifests.Count + " 个语言包 / 4 槽，实测占用 " + used.Count + " 槽";
            return _manifests.Count <= 4;
        }

        // ── 指纹算法：必须与 mod_book_name/BookProfiles.cs 逐字节一致 ──
        internal static string FingerprintOf(IList<string> words)
        {
            if (words == null) return null;
            List<string> normalized = new List<string>(words.Count);
            for (int i = 0; i < words.Count; i++)
            {
                string word = words[i];
                if (word == null) return null;
                normalized.Add(word.Trim().Normalize(NormalizationForm.FormC));
            }
            normalized.Sort(StringComparer.Ordinal);
            StringBuilder payload = new StringBuilder();
            for (int i = 0; i < normalized.Count; i++)
                payload.Append(normalized[i]).Append('\n');
            byte[] bytes = Encoding.UTF8.GetBytes(payload.ToString());
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(bytes);
                StringBuilder hex = new StringBuilder(digest.Length * 2);
                for (int i = 0; i < digest.Length; i++) hex.Append(digest[i].ToString("x2"));
                return hex.ToString();
            }
        }
    }
}
