// Compile the production scope against an in-memory game/storage boundary.
using System;
using System.Collections.Generic;
using WcpHost;

namespace WcpHost
{
    internal class BookProfile { internal string Id; internal string Es3Prefix; }
    internal class LanguageManifest { internal BookProfile Profile; }
    internal class BookRegistry { internal List<LanguageManifest> Manifests = new List<LanguageManifest>(); }
    internal class WcpHostPlugin { internal static WcpHostPlugin Instance; internal BookRegistry Registry; }
    internal static class GameAdapter
    {
        internal static Dictionary<string, object> Fields = new Dictionary<string, object>();
        internal static Dictionary<string, object> Saved = new Dictionary<string, object>();
        internal static string FailKey;
        internal static bool FailSet;
        internal static int Writes;
        internal static object StaticField(string type, string key) { object v; return Fields.TryGetValue(key, out v) ? v : null; }
        internal static bool SetStaticField(string type, string key, object value)
        { if (FailSet) return false; Fields[key] = value; return true; }
        internal static IList<string> ToWordList(object value) { return value as IList<string>; }
        internal static object Es3Load(string key, Type type, object fallback, string file)
        { object v; return Saved.TryGetValue(key, out v) ? v : fallback; }
        internal static bool Es3Save(string key, object value)
        { Writes++; if (key == FailKey) return false; Saved[key] = value; return true; }
    }
}

internal static class TakeoverScopeTest
{
    private const string Field = "S9extraStudy_Para";
    private static int failures;
    private static void Check(bool value, string label)
    { Console.WriteLine((value ? "PASS " : "FAIL ") + label); if (!value) failures++; }
    private static TakeoverScope Setup()
    {
        GameAdapter.Fields.Clear(); GameAdapter.Saved.Clear(); GameAdapter.FailKey = null; GameAdapter.FailSet = false; GameAdapter.Writes = 0;
        var registry = new BookRegistry();
        var manifest = new LanguageManifest { Profile = new BookProfile { Id = "test", Es3Prefix = "test" } };
        registry.Manifests.Add(manifest);
        WcpHostPlugin.Instance = new WcpHostPlugin { Registry = registry };
        GameAdapter.Fields[Field] = new string[] { "english" };
        var scope = new TakeoverScope(); scope.Enter(manifest, new string[] { "book" }); return scope;
    }
    private static bool Original() { return ((string[])GameAdapter.Fields[Field]).Length == 1 && ((string[])GameAdapter.Fields[Field])[0] == "english"; }
    private static int Main()
    {
        var scope = Setup(); scope.Enforce(); scope.Leave();
        Check(Original(), "normal array baseline restored");
        scope = Setup(); GameAdapter.FailKey = "test_bak_" + Field; scope.Enforce();
        Check(Original(), "backup write failure prevents takeover");
        scope.Leave(); Check(Original(), "backup failure does not erase original on leave");
        scope = Setup(); GameAdapter.FailKey = "test_owned_arrays"; scope.Enforce();
        Check(Original(), "ownership write failure prevents takeover");
        scope = Setup(); scope.Enforce(); GameAdapter.Saved.Clear(); scope.Leave();
        Check(Original(), "session baseline survives missing disk markers");
        scope = Setup(); scope.Leave();
        Check(GameAdapter.Writes == 0, "inactive unowned leave is read-only");
        scope = Setup(); scope.Leave();
        GameAdapter.Saved["test_owned_arrays"] = new string[] { "UnrelatedField" };
        GameAdapter.Saved["test_bak_UnrelatedField"] = new string[] { "corrupt" };
        GameAdapter.Fields["UnrelatedField"] = new string[] { "keep" };
        scope.RecoverStale(WcpHostPlugin.Instance.Registry, null);
        Check(((string[])GameAdapter.Fields["UnrelatedField"])[0] == "keep", "foreign field in marker is never restored");
        scope = Setup(); scope.Leave();
        GameAdapter.Saved["test_owned_arrays"] = new string[] { Field };
        scope.RecoverStale(WcpHostPlugin.Instance.Registry, null);
        Check(Original(), "missing persisted baseline does not invent empty queue");
        scope = Setup(); scope.Enforce();
        var restarted = new TakeoverScope();
        restarted.Enter(WcpHostPlugin.Instance.Registry.Manifests[0], new string[] { "book" });
        restarted.Enforce(); restarted.Leave();
        Check(Original(), "restart in same profile preserves original baseline");
        scope = Setup();
        GameAdapter.Fields["S8needToLearnWordList_Para"] = new List<string> { "english" };
        GameAdapter.FailKey = "test_bak_S8needToLearnWordList_Para"; scope.Enforce();
        Check(((IList<string>)GameAdapter.Fields["S8needToLearnWordList_Para"])[0] == "english", "list backup failure prevents takeover");
        scope = Setup();
        GameAdapter.Fields["S8needToLearnWordList_Para"] = new List<string> { "english" };
        scope.Enforce(); GameAdapter.Saved.Clear(); scope.Leave();
        Check(((IList<string>)GameAdapter.Fields["S8needToLearnWordList_Para"])[0] == "english", "list session baseline survives missing disk markers");
        scope = Setup(); scope.Enforce(); GameAdapter.FailKey = Field; scope.Leave();
        Check(((string[])GameAdapter.Saved["test_owned_arrays"]).Length == 1, "failed restore keeps retry marker");
        GameAdapter.FailKey = null; scope.RecoverStale(WcpHostPlugin.Instance.Registry, null);
        Check(((string[])GameAdapter.Saved["test_owned_arrays"]).Length == 0 && Original(), "successful recovery clears retry marker");
        scope = Setup(); GameAdapter.Fields["testingIf_Para"] = true; scope.Enforce(); scope.Leave();
        Check((bool)GameAdapter.Fields["testingIf_Para"], "array-only restore does not change test flags");
        scope = Setup(); GameAdapter.FailSet = true; scope.Enforce();
        Check(Original(), "field write failure does not persist takeover");
        Console.WriteLine("Failures: " + failures); return failures == 0 ? 0 : 1;
    }
}
