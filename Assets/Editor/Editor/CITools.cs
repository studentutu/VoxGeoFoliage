using UnityEditor;
using UnityEditor.Compilation;

public static class CiTools
{
    // Force Unity to fully recompile scripts (clean build cache + compile),
    // then quit the editor when it's done so your bash step can continue.
    // Important: direct full namespace + name of method in bash files!
    public static void ForceCompileAndExit()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate); // pick up any file changes
        CompilationPipeline.RequestScriptCompilation(
            UnityEditor.Compilation.RequestScriptCompilationOptions.CleanBuildCache
        );
        EditorApplication.update += WaitForCompileThenExit;
    }

    static void WaitForCompileThenExit()
    {
        if (!EditorApplication.isCompiling && !EditorApplication.isUpdating)
        {
            EditorApplication.update -= WaitForCompileThenExit;
            EditorApplication.Exit(0);
        }
    }

    // Regenerate IDE project files (.sln/.csproj), then quit.
    // Important: direct full namespace + name of method in bash files!
    public static void RegenerateProjectFilesAndExit()
    {
        AssetDatabase.Refresh();
        Unity.CodeEditor.CodeEditor.CurrentEditor.SyncAll();
        EditorApplication.Exit(0);
    }
}