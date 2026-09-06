using System;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace SimpleUnityMCP.Editor
{
    public static class CiTools
    {
        public const string ProjectFilesSyncedMarker = "SIMPLE_UNITY_MCP_CI:PROJECT_FILES_SYNCED";
        public const string CompilationPassedMarker = "SIMPLE_UNITY_MCP_CI:COMPILATION_PASSED";

        private const string CompilationPendingKey = "SimpleUnityMCP.CI.CompilationPending";
        private const string CompilationErrorCountKey = "SimpleUnityMCP.CI.CompilationErrorCount";
        private const string SyncPendingKey = "SimpleUnityMCP.CI.SyncPending";
        private const string EditorErrorCountKey = "SimpleUnityMCP.CI.EditorErrors";

        [InitializeOnLoadMethod]
        private static void ResumePendingCompilationAfterDomainReload()
        {
            if (Application.isBatchMode)
            {
                Application.logMessageReceived -= RecordEditorError;
                Application.logMessageReceived += RecordEditorError;
            }
            if (SessionState.GetBool(SyncPendingKey, false))
                EditorApplication.update += SyncWhenIdle;
            if (SessionState.GetBool(CompilationPendingKey, false))
                EditorApplication.update += CompleteCompilationWhenEditorIsIdle;
        }

        private static void RecordEditorError(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                SessionState.SetInt(EditorErrorCountKey, SessionState.GetInt(EditorErrorCountKey, 0) + 1);
        }

        // Force Unity to fully recompile scripts (clean build cache + compile),
        // then quit the editor when it's done so your bash step can continue.
        // Invoke as SimpleUnityMCP.Editor.CiTools.ForceCompileAndExit.
        public static void ForceCompileAndExit()
        {
            SessionState.SetBool(CompilationPendingKey, true);
            SessionState.SetInt(CompilationErrorCountKey, 0);
            RegisterCompilationCallbacks();

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            CompilationPipeline.RequestScriptCompilation(
                RequestScriptCompilationOptions.CleanBuildCache
            );
        }

        private static void RegisterCompilationCallbacks()
        {
            CompilationPipeline.assemblyCompilationFinished -= OnAssemblyCompilationFinished;
            CompilationPipeline.compilationFinished -= OnCompilationFinished;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;
            CompilationPipeline.compilationFinished += OnCompilationFinished;
        }

        private static void OnAssemblyCompilationFinished(string assemblyPath, CompilerMessage[] messages)
        {
            var errorCount = SessionState.GetInt(CompilationErrorCountKey, 0);

            foreach (var message in messages)
            {
                if (message.type != CompilerMessageType.Error)
                    continue;

                errorCount++;
                Debug.LogError($"{assemblyPath}: {message.message} ({message.file}:{message.line}:{message.column})");
            }

            SessionState.SetInt(CompilationErrorCountKey, errorCount);
        }

        private static void OnCompilationFinished(object context)
        {
            CompilationPipeline.assemblyCompilationFinished -= OnAssemblyCompilationFinished;
            CompilationPipeline.compilationFinished -= OnCompilationFinished;
            EditorApplication.update += CompleteCompilationWhenEditorIsIdle;
        }

        private static void CompleteCompilationWhenEditorIsIdle()
        {
            if (!SessionState.GetBool(CompilationPendingKey, false))
            {
                EditorApplication.update -= CompleteCompilationWhenEditorIsIdle;
                return;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            EditorApplication.update -= CompleteCompilationWhenEditorIsIdle;
            CompilationPipeline.assemblyCompilationFinished -= OnAssemblyCompilationFinished;
            CompilationPipeline.compilationFinished -= OnCompilationFinished;

            var compilationErrorCount = SessionState.GetInt(CompilationErrorCountKey, 0)
                + SessionState.GetInt(EditorErrorCountKey, 0);
            SessionState.SetBool(CompilationPendingKey, false);
            SessionState.SetInt(CompilationErrorCountKey, 0);

            if (compilationErrorCount == 0)
                Debug.Log(CompilationPassedMarker);
            else
                Debug.LogError($"SIMPLE_UNITY_MCP_CI:COMPILATION_FAILED errors={compilationErrorCount}");

            EditorApplication.Exit(compilationErrorCount == 0 ? 0 : 1);
        }

        // Own shutdown after refresh/reload has settled. Do not pass -quit.
        public static void RegenerateProjectFilesAndExit()
        {
            SessionState.SetBool(SyncPendingKey, true);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            EditorApplication.update -= SyncWhenIdle;
            EditorApplication.update += SyncWhenIdle;
        }

        private static void SyncWhenIdle()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;
            EditorApplication.update -= SyncWhenIdle;
            SessionState.SetBool(SyncPendingKey, false);
            try
            {
                Unity.CodeEditor.CodeEditor.CurrentEditor.SyncAll();
                if (SessionState.GetInt(EditorErrorCountKey, 0) > 0)
                {
                    Debug.LogError("SIMPLE_UNITY_MCP_CI:IMPORT_FAILED; inspect the complete editor log.");
                    EditorApplication.Exit(1);
                    return;
                }
                Debug.Log(ProjectFilesSyncedMarker);
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }
    }
}
