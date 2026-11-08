using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Marco.Network.Editor
{
    // Test builds for the remote verification (2026-11-08). The scene list is read from the build settings and passed
    // straight to BuildPlayer (nothing is written back); the harness symbol MARCO_REMOTE_TEST is passed only through
    // extraScriptingDefines, so ProjectSettings never carries it. Output goes outside the repository.
    //   -remoteOut <path to .exe>   required
    //   -remoteSymbol on|off        default on; off builds exactly what a normal build compiles (harness check)
    public static class RemoteTestBuild
    {
        public static void Build()
        {
            string output = Arg("-remoteOut");
            if (string.IsNullOrEmpty(output)) throw new Exception("-remoteOut missing");
            bool symbol = Arg("-remoteSymbol") != "off";
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.Development,
                extraScriptingDefines = symbol ? new[] { "MARCO_REMOTE_TEST" } : Array.Empty<string>()
            };
            Debug.Log($"[RemoteTestBuild] scenes: {string.Join(", ", scenes)} → {output} (symbol {(symbol ? "MARCO_REMOTE_TEST" : "none")})");
            var report = BuildPipeline.BuildPlayer(options);
            var s = report.summary;
            Debug.Log($"[RemoteTestBuild] result={s.result} errors={s.totalErrors} warnings={s.totalWarnings} size={s.totalSize / (1024 * 1024)}MB time={s.totalTime}");
            EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
