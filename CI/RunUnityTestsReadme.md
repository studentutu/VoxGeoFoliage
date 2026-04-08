# Summary

We are using Unity Editor’s Test Runner.

All tests are in xml file and Unity logs here `./CI/CITestOutput.xml`

1. Rebuild C# solution and check for compile errors in [CompileErrorsAfterUnityRun.txt](CI/CompileErrorsAfterUnityRun.txt) file (if any):
   - Use "Compile by Rider MSBuild" (see .vscode/tasks.json) for a fast compile check when no new .cs/asmdef files were added. Does not use Unity editor(preferred way).
   - Use "Fully Compile by Unity" when new files/asmdefs were added or before running tests. Requires to close editor and compilation will use new headless Editor process. This takes  1-4 minutes.

Note:

- "Fully Compile by Unity" task must be called if new `.cs` file is added, before running any tests, as it is the only way to properly rebuild solution. This will also update [CompileErrorsAfterUnityRun.txt](CI/CompileErrorsAfterUnityRun.txt) which shows all compile time errors, if the text file is not empty. This is best to run automatically but infrequent (this process takes 1-2 minutes).
- **IMPORTANT**: `xxxx-unity.sln` will not see new `.cs` files, you will need to rebuild solution from within Unity Editor by running `rebuildSolutionFromUnityItself.sh`, see [Fully Compile by Unity](../.vscode/tasks.json).
- "Compile by Rider MSBuild" and "Fully Compile by Unity" writes compile errors to the same [CompileErrorsAfterUnityRun.txt](CI/CompileErrorsAfterUnityRun.txt) file.

2. Run/parse tests as stated in `.vscode/tasks.json`

You can run tests by using `runTestsBash.sh` (this will run all tests via Unity test runner). This will regenerate `./CI/CITestOutput.xml` as well as write additional logs into `UnityLogs.log`. Prefer to ask use to run test task manually, as to avoid any agent process/bash limitations.

**IMPORTANT**: all unity editor process with current project must be closed prior to running test shell script.

To parse test results use `parseTestErrors.sh` (preferred) or just parse `CITestOutput.xml` manually to find if any test failed. Running `parseTestErrors.sh` will also extract from last `UnityLogs.log`[UnityEditorCompileErrors](CompileErrorsAfterUnityRun.txt) possible compile time errors.

**IMPORTANT NOTE**:

- avoid running under plain cmd.exe/PowerShell, prefer bash
- prefer run with git bash (already set as default for vscode)
- Close any open Unity Editor instance of this project before running tests; the scripts launch their own headless Editor.
