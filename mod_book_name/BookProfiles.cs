// Shared identity registry for the portable WCP word-book plugins.
//
// A profile is identified by the complete normalized word set, never by the
// custom-book slot or by a language heuristic.  Add future French/Russian
// profiles here with their own Id/Language/DisplayName/count/fingerprint;
// their behaviour plugins can then opt in by Language without touching books
// that do not exactly match a profile.
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace WcpBookProfiles
{
    internal sealed class BookProfile
    {
        internal readonly string Id;
        internal readonly string Language;
        internal readonly string DisplayName;
        internal readonly int WordCount;
        internal readonly string Fingerprint;

        internal BookProfile(string id, string language, string displayName,
                             int wordCount, string fingerprint)
        {
            Id = id;
            Language = language;
            DisplayName = displayName;
            WordCount = wordCount;
            Fingerprint = fingerprint;
        }
    }

    internal static class BookProfiles
    {
        internal const string Japanese = "ja";

        // 猫条版 JLPT 合并单册。指纹基于排序后的完整词形集合，导入到任意槽位均相同。
        // 新语言词书只需在此添加新的 BookProfile；现有日语逻辑只接管 Language=ja。
        internal static readonly BookProfile[] All = new BookProfile[]
        {
            new BookProfile("catbar-jlpt-complete", Japanese, "日语词库(猫条版)", 7922,
                "6d7a51c0c5d30dd63d8a6e3412bce1cf2419554fb8487cf5c8a205e43ac41663")
        };

        internal static BookProfile Match(IList<string> words)
        {
            if (words == null || words.Count == 0) return null;
            string hash = null;
            for (int i = 0; i < All.Length; i++)
            {
                BookProfile p = All[i];
                if (words.Count != p.WordCount) continue;
                if (hash == null) hash = FingerprintOf(words);
                if (hash == p.Fingerprint) return p;
            }
            return null;
        }

        internal static bool IsManagedDisplayName(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            for (int i = 0; i < All.Length; i++)
                if (text.StartsWith(All[i].DisplayName, StringComparison.Ordinal)) return true;
            return false;
        }

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
