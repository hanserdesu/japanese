// Shared identity registry for the portable WCP word-book plugins.
// French copy of D:/Japanese/mod_book_name/BookProfiles.cs. Keep the registry
// content in sync with the Japanese one; each project deploys its own plugins
// and the matching game install reads whichever DLL carries the profile.
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
        internal const string French = "fr";

        // Fingerprints are SHA256 over the sorted full word-form set, so a book
        // matches in any custom slot. Registering the Japanese book here as well
        // lets the FR plugin positively identify (and therefore never touch) it.
        internal static readonly BookProfile[] All = new BookProfile[]
        {
            new BookProfile("catbar-jlpt-complete", Japanese, "日语词库(猫条版)", 7922,
                "6d7a51c0c5d30dd63d8a6e3412bce1cf2419554fb8487cf5c8a205e43ac41663"),
            new BookProfile("catbar-french-cefr-complete", French, "法语词库(猫条版)", 8116,
                "376af2eae0292052cefb4cb0d673f48a5ca480a38e25a6bf5353998caea17e9d")
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
