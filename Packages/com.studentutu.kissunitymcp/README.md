# kissunitymcp editor package

Package ID: `com.studentutu.kissunitymcp`. Editor assembly:
`Studentutu.kissunitymcp.Editor`. The plugin and repository are `kiss-unity-mcp`.

Reusable editor methods for Unity 2023.1+. There are no required package
dependencies. Test execution needs the consuming project's Unity Test Framework;
IDE solution generation needs an IDE integration already selected by that project.
Setup does not add either dependency.

- `SimpleUnityMCP.Editor.CiTools.RegenerateProjectFilesAndExit`: refresh, wait for
  compilation/import to settle across reloads, synchronize IDE projects, and exit.
  The method owns shutdown: do not pass `-quit`. Success emits
  `SIMPLE_UNITY_MCP_CI:PROJECT_FILES_SYNCED`.
- `SimpleUnityMCP.Editor.CiTools.ForceCompileAndExit`: request clean script
  compilation and exit with compilation/error evidence.
- `SimpleUnityMCP.Editor.ShaderCompileTool.CompileAllProjectShaders`: reimport
  shaders, inspect Unity shader errors, emit SHADER_COMPILATION_PASSED or FAILED,
  and exit accordingly. This does not enumerate every player-build variant.

Editor errors are recorded across reloads using SessionState during batch runs.
The shell wrapper still scans the complete native editor log, including errors
before managed initialization or after the execute method.

Shell tools remain outside this package. Setup installs them into the project's
`.kissunitymcp` directory, with tool paths in `tools.env` and VS Code actions
in `.vscode`. See the plugin repository's Readme.md for the manual workflow.
