// WCP Host Core — 资源路由
//
// 这是"解耦"真正落地的地方。设计约束只有一条，但不可让步:
//
//   **语言 A 的代码路径里不允许出现语言 B 的路径字符串。**
//
// 现状（2026-09-14 实测）违反这条: 单词音频 JA 倒进游戏原生 vocabulary\、
// FR 私有 fr_word_audio\、RU 私有 + 回退 vocabulary\；例句音频三语言共用
// sentence_audio\，隔离靠提取器谓词互斥（假名/拉丁/西里尔）—— 属逻辑巧合，
// 新增德语/西班牙语即失效。
//
// 本路由器把隔离变成物理的: 所有资源都在 packs/<lang>/ 下，
// 解析入口只有 Resolve() 一个，且必须先激活。
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace WcpHost
{
    internal enum ResourceKind
    {
        Book,             // 词书 xlsx
        MeaningDb,        // 释义库
        SentenceTable,    // 例句表（含译文）
        Repair,           // 自愈载荷（游戏更新覆盖 DB 后用）
        WordAudio,        // 单词音频（按词形命名）
        SentenceAudio     // 例句音频（按 md5(例句原文) 命名）
    }

    internal sealed class ResourceRouter
    {
        private readonly BookRegistry _registry;
        private string _activeProfileId;

        internal ResourceRouter(BookRegistry registry)
        {
            if (registry == null) throw new ArgumentNullException("registry");
            _registry = registry;
        }

        internal BookRegistry Registry { get { return _registry; } }

        internal string ActiveProfileId { get { return _activeProfileId; } }

        internal LanguageManifest Active
        {
            get { return _activeProfileId == null ? null : _registry.ByProfileId(_activeProfileId); }
        }

        internal bool IsActive
        {
            get { return Active != null; }
        }

        // 激活 / 取消激活。传 null 表示"当前没有受管词书"（fail-closed）。
        // 宿主在作用域门判定"内存词表 / 槽位 / 落盘书名三者一致"之后才调用这里。
        internal void SetActive(string profileId)
        {
            if (profileId != null && _registry.ByProfileId(profileId) == null)
                throw new ArgumentException("未注册的 profile_id: " + profileId);
            _activeProfileId = profileId;
        }

        // 从当前内存词表推断应激活哪个 pack。识别不出 = 不激活。
        internal LanguageManifest SetActiveFromWords(IList<string> words)
        {
            BookProfile p = _registry.Match(words);
            _activeProfileId = (p == null) ? null : p.Id;
            return Active;
        }

        // 唯一的资源解析入口。未激活 → null（调用方必须按"没有资源"处理，不得回退到别的语言）。
        internal string Resolve(ResourceKind kind, string key)
        {
            return ResolveFor(_activeProfileId, kind, key);
        }

        internal string ResolveFor(string profileId, ResourceKind kind, string key)
        {
            if (profileId == null) return null;
            LanguageManifest m = _registry.ByProfileId(profileId);
            if (m == null) return null;

            switch (kind)
            {
                case ResourceKind.Book:
                    return PickBook(m, key);
                case ResourceKind.MeaningDb:
                    return m.Resolve(m.MeaningDb);
                case ResourceKind.SentenceTable:
                    return m.Resolve(m.SentenceTable);
                case ResourceKind.Repair:
                    return m.Resolve(m.Repair);
                case ResourceKind.WordAudio:
                    return AudioPath(m.WordAudioDir, key, null);
                case ResourceKind.SentenceAudio:
                    return AudioPath(m.SentenceAudioDir, null, key);
                default:
                    return null;
            }
        }

        private static string PickBook(LanguageManifest m, string key)
        {
            if (m.Books.Count == 0) return null;
            if (string.IsNullOrEmpty(key)) return m.Resolve(m.Books[0]);
            for (int i = 0; i < m.Books.Count; i++)
            {
                string rel = m.Books[i];
                if (rel != null && rel.IndexOf(key, StringComparison.Ordinal) >= 0) return m.Resolve(rel);
            }
            return null;
        }

        // 例句音频文件名 = md5(例句原文) —— 与 SentenceAudioMod 的 Md5() 必须一致
        // （UTF-8、小写十六进制、无分隔）。
        private static string AudioPath(string dir, string wordKey, string sentenceKey)
        {
            string name;
            if (wordKey != null)
            {
                name = Sanitize(wordKey) + ".mp3";
            }
            else
            {
                if (sentenceKey == null) return null;
                name = Md5(sentenceKey) + ".mp3";
            }
            return JoinAudio(dir, name);
        }

        // 词形里可能有路径分隔符/保留设备名；文件名侧的兜底规则见复刻契约 §1.5。
        private static string Sanitize(string s)
        {
            char[] bad = System.IO.Path.GetInvalidFileNameChars();
            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                bool ok = true;
                for (int j = 0; j < bad.Length; j++)
                    if (s[i] == bad[j]) { ok = false; break; }
                sb.Append(ok ? s[i] : '_');
            }
            return sb.ToString();
        }

        private static string JoinAudio(string dir, string name)
        {
            if (string.IsNullOrEmpty(dir)) return null;
            string d = dir.Replace('/', System.IO.Path.DirectorySeparatorChar);
            char sep = System.IO.Path.DirectorySeparatorChar;
            if (d.Length > 0 && d[d.Length - 1] != sep) d += sep;
            return d + name;
        }

        internal static string Md5(string s)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] digest = md5.ComputeHash(Encoding.UTF8.GetBytes(s));
                StringBuilder hex = new StringBuilder(digest.Length * 2);
                for (int i = 0; i < digest.Length; i++) hex.Append(digest[i].ToString("x2"));
                return hex.ToString();
            }
        }
    }
}
