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
        internal static int Writes;
        internal static object StaticField(string type, string key) { object v; return Fields.TryGetValue(key, out v) ? v : null; }
        internal static void SetStaticField(string type, string key, object value) { Fields[key] = value; }
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
        GameAdapter.Fields.Clear(); GameAdapter.Saved.Clear(); GameAdapter.FailKey = null; GameAdapter.Writes = 0;
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
        Console.WriteLine("Failures: " + failures); return failures == 0 ? 0 : 1;
    }
}
