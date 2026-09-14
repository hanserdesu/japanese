// WCP Host — 游戏适配层（反射封装）
//
// 为什么全走反射: 游戏更新会改字段名 / 类型。现有插件（JpWordListMod.cs 的 L 段）
// 已经把这条做对了 —— 字段消失时只记一条日志，不抛异常、不崩、不写坏存档。
// 宿主把这条约定提升为通用规则: **任何对游戏内部的访问都必须能"优雅失效"**。
//
// 所有类型/方法都按名字查，查不到返回 null / 空，由调用方按 fail-closed 处理。
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace WcpHost
{
    internal static class GameAdapter
    {
        private static readonly Dictionary<string, FieldInfo> _fields =
            new Dictionary<string, FieldInfo>();
        private static MethodInfo _es3LoadString;
        private static bool _es3Probed;

        internal static object StaticField(string typeName, string fieldName)
        {
            string cacheKey = typeName + "." + fieldName;
            FieldInfo fi;
            if (!_fields.TryGetValue(cacheKey, out fi))
            {
                try
                {
                    Type t = AccessTools.TypeByName(typeName);
                    fi = (t == null) ? null : AccessTools.Field(t, fieldName);
                }
                catch (Exception) { fi = null; }
                _fields[cacheKey] = fi;
                if (fi == null && WcpHostPlugin.Log != null)
                    WcpHostPlugin.Log.LogWarning("WcpHost: 游戏字段不可用 " + cacheKey +
                        "（游戏更新导致？本项功能降级，其余不受影响）");
            }
            if (fi == null) return null;
            try { return fi.GetValue(null); }
            catch (Exception) { return null; }
        }

        internal static IList<string> ToWordList(object raw)
        {
            if (raw == null) return null;
            if (raw is string) return null;                 // 别把 IEnumerable<char> 当词表
            IList<string> typed = raw as IList<string>;
            if (typed != null) return typed;
            IEnumerable seq = raw as IEnumerable;
            if (seq == null) return null;
            List<string> list = new List<string>();
            try
            {
                foreach (object o in seq) list.Add(o == null ? null : o.ToString());
            }
            catch (Exception) { return null; }
            return list;
        }

        // 落盘书名。ES3 是游戏自带的静态类，这里只找 (string key, T default) 这个重载。
        internal static string DiskBookName()
        {
            MethodInfo m = Es3LoadString();
            if (m == null) return null;
            try { return m.Invoke(null, new object[] { "ChosenBook_Para", null }) as string; }
            catch (Exception) { return null; }
        }

        private static MethodInfo Es3LoadString()
        {
            if (_es3Probed) return _es3LoadString;
            _es3Probed = true;
            try
            {
                Type es3 = AccessTools.TypeByName("ES3");
                if (es3 == null) return null;
                MethodInfo[] ms = es3.GetMethods(BindingFlags.Public | BindingFlags.Static);
                for (int i = 0; i < ms.Length; i++)
                {
                    MethodInfo m = ms[i];
                    if (m.Name != "Load" || !m.IsGenericMethodDefinition) continue;
                    ParameterInfo[] ps = m.GetParameters();
                    if (ps.Length == 2 && ps[0].ParameterType == typeof(string))
                    {
                        _es3LoadString = m.MakeGenericMethod(typeof(string));
                        break;
                    }
                }
                if (_es3LoadString == null && WcpHostPlugin.Log != null)
                    WcpHostPlugin.Log.LogWarning("WcpHost: 找不到 ES3.Load<string>(string,T) 重载，" +
                        "落盘书名这一路判定降级（宿主将保持未激活）");
            }
            catch (Exception e)
            {
                if (WcpHostPlugin.Log != null) WcpHostPlugin.Log.LogWarning("WcpHost: ES3 探测失败: " + e.Message);
            }
            return _es3LoadString;
        }
    }
}
