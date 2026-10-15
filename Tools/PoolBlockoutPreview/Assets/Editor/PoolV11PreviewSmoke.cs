using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Batch-only startup smoke check for the map viewer. Inactive in ordinary editor sessions.
public static class PoolV11PreviewSmoke
{
    const string Armed = "PoolV11PreviewSmoke.Armed";
    const string Error = "PoolV11PreviewSmoke.Error";
    const string Phase = "PoolV11PreviewSmoke.Phase";

    [InitializeOnLoadMethod]
    static void Install()
    {
        if (!SessionState.GetBool(Armed, false)) return;
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
        Application.logMessageReceived -= Log; Application.logMessageReceived += Log;
    }

    public static void Start()
    {
        PoolV11Builder.ValidateSavedMap();
        SessionState.SetBool(Armed, true); SessionState.SetString(Error, ""); SessionState.SetInt(Phase, 0);
        SessionState.SetFloat("PoolV11PreviewSmoke.Start", (float)EditorApplication.timeSinceStartup);
        Install(); EditorApplication.EnterPlaymode();
    }

    static void Log(string condition, string trace, LogType type)
    {
        if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
            SessionState.SetString(Error, condition);
    }

    static void Tick()
    {
        if (EditorApplication.timeSinceStartup - SessionState.GetFloat("PoolV11PreviewSmoke.Start", 0) > 90)
        { Finish("FAIL: play mode startup timed out", 1); return; }
        try
        {
            if (SessionState.GetInt(Phase, 0) == 0 && EditorApplication.isPlaying && Time.frameCount >= 3)
            {
                var view = UnityEngine.Object.FindFirstObjectByType<PoolV11Viewer>();
                if (view == null || view.viewCamera == null) throw new InvalidOperationException("Viewer camera reference missing.");
                view.visibleFloor = 1; view.ApplyView();
                if (!view.firstBuildings.gameObject.activeSelf || view.secondBuildings.gameObject.activeSelf || !view.pools[0].activeSelf)
                    throw new InvalidOperationException("1F preview toggle failed.");
                view.visibleFloor = 2; view.ApplyView();
                if (view.firstBuildings.gameObject.activeSelf || !view.secondBuildings.gameObject.activeSelf || view.pools[0].activeSelf)
                    throw new InvalidOperationException("2F preview toggle failed.");
                view.visibleFloor = 0; view.ApplyView();
                if (Mathf.Abs(Shader.GetGlobalFloat("_PoolV11CutHeight") - view.cutHeight) > .001f)
                    throw new InvalidOperationException("Wall cutaway state missing.");
                SessionState.SetInt(Phase, 1); EditorApplication.ExitPlaymode();
            }
            if (SessionState.GetInt(Phase, 0) == 1 && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                string error = SessionState.GetString(Error, "");
                Finish(error.Length == 0 ? "PASS: entered Play mode; viewer camera, 1F/2F/all toggles, pool visibility and wall cutaway initialized without runtime errors." : "FAIL: " + error,
                    error.Length == 0 ? 0 : 1);
            }
        }
        catch (Exception exception) { Finish("FAIL: " + exception, 1); }
    }

    static void Finish(string message, int code)
    {
        SessionState.SetBool(Armed, false); EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
        Directory.CreateDirectory("PoolV11Reports"); File.WriteAllText("PoolV11Reports/PlayModeSmoke.txt", message);
        Debug.Log(message); EditorApplication.Exit(code);
    }
}
