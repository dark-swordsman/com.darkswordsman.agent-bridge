using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DarkSwordsman.AgentBridge
{
    // Polls Temp/AgentBridge/in/ for *.req files and answers each one in Temp/AgentBridge/out/<id>.txt.
    // A request is a command name on the first line, then key=value lines. Results are plain text;
    // failures start with "ERROR:". The CLI in Tools~/ub writes requests and prints capped previews.
    // Polling runs from EditorApplication.update, which keeps firing while the editor is unfocused.
    [InitializeOnLoad]
    internal static class AgentBridge
    {
        private const double PollSeconds = 0.25;

        internal static readonly string Root = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Temp", "AgentBridge");
        private static readonly string InDir = Path.Combine(Root, "in");
        private static readonly string OutDir = Path.Combine(Root, "out");
        private static double _nextPoll;

        static AgentBridge()
        {
            EditorApplication.update += Tick;
        }

        // A package installed from git lives in Library/PackageCache/<name>@<hash>/, so ask Unity where `ub` is.
        [MenuItem("Tools/Agent Bridge/Copy ub Path")]
        private static void CopyUbPath()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(AgentBridge).Assembly);
            var ub = Path.Combine(package.resolvedPath, "Tools~", "ub");
            EditorGUIUtility.systemCopyBuffer = ub;
            Debug.Log("[AgentBridge] copied ub path: " + ub);
        }

        private static void Tick()
        {
            if (EditorApplication.timeSinceStartup < _nextPoll) return;
            _nextPoll = EditorApplication.timeSinceStartup + PollSeconds;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || !Directory.Exists(InDir)) return;

            foreach (var req in Directory.GetFiles(InDir, "*.req").OrderBy(File.GetLastWriteTimeUtc))
            {
                var id = Path.GetFileNameWithoutExtension(req);
                string text;
                try
                {
                    text = File.ReadAllText(req);
                    File.Delete(req);
                }
                catch (IOException)
                {
                    continue; // still being written; pick it up next tick
                }
                Write(id, Run(text));
            }
        }

        private static string Run(string text)
        {
            var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();
            if (lines.Count == 0) return "ERROR: empty request";
            var args = new List<KeyValuePair<string, string>>();
            foreach (var l in lines.Skip(1))
            {
                var eq = l.IndexOf('=');
                if (eq > 0) args.Add(new KeyValuePair<string, string>(l.Substring(0, eq).Trim(), l.Substring(eq + 1)));
            }
            var cmd = lines[0].Trim();
            if (!Commands.All.TryGetValue(cmd, out var fn))
                return $"ERROR: unknown command '{cmd}'. Commands: {string.Join(", ", Commands.All.Keys.OrderBy(k => k))}";
            try
            {
                return fn(new Args(args));
            }
            catch (ArgumentException e)
            {
                return $"ERROR: {e.Message}";
            }
            catch (Exception e)
            {
                return $"ERROR: {e.GetType().Name}: {e.Message}\n{e.StackTrace}";
            }
        }

        private static void Write(string id, string result)
        {
            Directory.CreateDirectory(OutDir);
            var final = Path.Combine(OutDir, id + ".txt");
            var tmp = final + ".tmp";
            File.WriteAllText(tmp, result);
            if (File.Exists(final)) File.Delete(final);
            File.Move(tmp, final);
        }
    }

    internal class Args
    {
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>();

        // Every pair in request order, for commands that take free-form keys (set, setmat).
        public readonly List<KeyValuePair<string, string>> Pairs;

        public Args(List<KeyValuePair<string, string>> pairs)
        {
            Pairs = pairs;
            foreach (var p in pairs) _values[p.Key] = p.Value;
        }

        public string Get(string key, string fallback = null) => _values.TryGetValue(key, out var v) ? v : fallback;

        public string Require(string key) => Get(key) ?? throw new ArgumentException($"missing argument '{key}'");

        public int Int(string key, int fallback) => int.TryParse(Get(key), out var v) ? v : fallback;

        public bool Bool(string key, bool fallback)
        {
            var v = Get(key);
            return v == null ? fallback : v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "yes";
        }
    }
}
