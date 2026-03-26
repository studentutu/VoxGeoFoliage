# Technical Context

Purpose: compact toolchain, package, and verification reference for the current repo.

## Engine and Language

- Unity: `6000.3.7f1`
- C#: `8` target style, current generated projects report `LangVersion 9.0`
- API compatibility: `.NET Standard 2.1`

## Core Packages

- `com.unity.inputsystem 1.18.0`
- `com.unity.test-framework 1.6.0`
- `com.unity.cinemachine 3.1.4`
- `com.unity.addressables 2.8.1`
- `com.unity.render-pipelines.universal 17.3.0`
- `com.unity.collections 2.6.2`
- `com.unity.mathematics 1.3.3`
- `com.unity.burst 1.8.27`

Repo-used libraries and plugins:

- none

## Repo Structure

- `Assets/Scripts`
  - runtime code, authoring data
- `Assets/Scripts/MassPlacement`
  - editor-triggered scatter utility that raycasts down onto physical ground
- `Assets/EditorTests`
  - EditMode tests (faster then playmode tests)
- `Assets/Editor`
  - Editor tools, utilities and visualization (in editor)
- `DetailedDocs`
  - feature-specific architecture and ASCII authority docs
- `memorybank`
  - compact cross-cutting repo guidance and routing

## Build / Verification Flow

- Build entry points are defined in `.vscode/tasks.json`.
- Fast compile: `Compile by Rider MSBuild`
- Mandatory full compile when new `.cs` or `.asmdef` files are added: `Fully Compile by Unity`
- Test runner wrapper: `runTestsFromRoot.sh`
- Result parser: `runParsetests.sh`
- Authoritative outputs:
  - `CI/CITestOutput.xml`
  - `CI/CompileErrorsAfterUnityRun.txt`

## Constraints

- EditMode tests must not rely on Unity lifecycle callbacks.
- Because Unity generates the authoritative compile/test outputs, the Unity editor must be closed before running the required compile/test scripts.
- New or renamed `.cs` files require Full Unity compile path so the generated solution is rebuilt from Unity itself.
- Use message-bus or singleton when cross communication is needed (prefer message bus with explicit sender and data).
- All static runtime classes must have explicit `Reset` method that is invoked once in `EditorPlayModeStaticServicesReset.cs`.
