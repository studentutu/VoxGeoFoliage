using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SimpleUnityMCP.Editor
{
    public static class ShaderCompileTool
    {
        public const string CompilationPassedMarker = "SIMPLE_UNITY_MCP_CI:SHADER_COMPILATION_PASSED";
        public const string CompilationFailedMarker = "SIMPLE_UNITY_MCP_CI:SHADER_COMPILATION_FAILED";

        public static void CompileAllProjectShaders()
        {
            var findAll = FindAssetsOfTypeEditorExtension.FindAssetsWithType<Shader>().ToList();

            foreach (var shader in findAll)
            {
                ShaderUtil.ClearCachedData(shader.Item1);
                ShaderUtil.ClearShaderMessages(shader.Item1);
            }

            foreach (var shader in findAll)
            {
                // Reimport each shader so that unity can recompile them
                AssetDatabase.ImportAsset(shader.Item2, ImportAssetOptions.ForceUpdate);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.RefreshSettings();
            AssetDatabase.Refresh();

            var failedShaderCount = 0;
            var s = new StringBuilder();
            foreach (var shader in findAll)
            {
                var hasShaderError = ShaderUtil.ShaderHasError(shader.Item1);

                if (hasShaderError)
                {
                    failedShaderCount++;
                    s.AppendLine($"Errors in Shader: {shader.Item2}");
                    var errorsAndWarning = ShaderUtil.GetShaderMessages(shader.Item1);
                    foreach (var message in errorsAndWarning)
                    {
                        s.AppendLine(message.message);
                    }
                }
            }

            if (failedShaderCount > 0)
            {
                Debug.LogError($"{CompilationFailedMarker} shaders={failedShaderCount}");
                Debug.LogError(s);
            }
            else
                Debug.Log($"{CompilationPassedMarker} shaders={findAll.Count}");

            EditorApplication.Exit(failedShaderCount > 0 ? 1 : 0);
        }
    }
}
