# AGENTS.md

This file provides guidance to agents when working with code in this repository.

- Build commands (use these exact entry points to match local tooling) see [vscode-tasks](.vscode/tasks.json):
  - Must use one of after all edits are done (mandatory): "Compile by Rider MSBuild" task or "Fully Compile by Unity" task. They will update [CompileErrorsAfterUnityRun.txt](CI/CompileErrorsAfterUnityRun.txt) which can show all compile time errors, if the text file is not empty.
    - Use "Compile by Rider MSBuild" (see .vscode/tasks.json) for a fast compile check when no new .cs/asmdef files were added. Does not use Unity editor(preferred way).
    - Use "Fully Compile by Unity" when new files/asmdefs were added. Requires to close editor and compilation will use new headless Editor process. This takes  1-4 minutes.
  - IMPORTANT: xxxx-unity.sln will not see new .cs files, you will need to rebuild solution from within Unity Editor by running rebuildSolutionFromUnityItself.sh, see [Fully Compile by Unity](../.vscode/tasks.json).
  - "Compile by Rider MSBuild" task is the fast compile check but won't work if new script files/asmdefs were added to the solution. Use it when fixing failed tests or doing quick compile validation.

- Test execution is Git Bash–centric and directory-sensitive:
  1. Before any test run, compile  project with one of: "Compile by Rider MSBuild" task or "Fully Compile by Unity" task (.vscode/tasks.json) and make sure no compile time errors exists in [CompileErrorsAfterUnityRun.txt](CI/CompileErrorsAfterUnityRun.txt).
  2. Tests are run via Unity Editor Test Runner, but that is a long process, as Unity will need to open/import/recompile project, and only then run actual tests. This process can take up to several minutes. Prefer to ask user to manually run test, as to avoid eating agents daily/weekly limits on the process/bash use.
  3. Always run tests via the wrapper [runTestsFromRoot.sh](runTestsFromRoot.sh).
  - The underlying runner is [runTestsBash.sh](.runTestsBash.sh); it shells Unity with -runTests and writes CI/CITestOutput.xml and CI/UnityLogs.log.
  4. Always run [runParsetests.sh](runParsetests.sh) in order to get both potential [Unity Editor compiler errors](CI/CompileErrorsAfterUnityRun.txt) as well as a list of the actual failed tests. See [CI/RunUnityTestsReadme.md](CI/RunUnityTestsReadme.md)
  - IMPORTANT: Unity Editor for this project must be CLOSED before running tests (scripts launch their own instance). See [CI/RunUnityTestsReadme.md](CI/RunUnityTestsReadme.md).

- Non-obvious environment requirements:
  - Unity path is hardcoded for Git Bash: /c/Program Files/Unity/Hub/Editor/6000.1.12f1/Editor/Unity.exe in [runTestsBash.sh](.runTestsBash.sh). Update if Editor is installed elsewhere.
  - VS Code tasks invoke Git Bash explicitly; use those on Windows: [Run Unity Tests](.vscode/tasks.json) and [Parse Unity Tests](.vscode/tasks.json).
  - Rider path (along with it's MSbuild tools) is hardcoded: see [Compile by Rider MSbuild](rebuildSolutionWithRiderMsBuild.sh). Update if Rider version is installed elsewhere.
  - We use edit-mode tests, it has limitation that no unity methods will be automatically invoked, so we should always expose API and treat unity methods as redundant (but necessary) initialization.

- CI output and failure detection:
  - Authoritative results live in [CI/CITestOutput.xml](CI/CITestOutput.xml). Logs in [CI/UnityLogs.log](CI/UnityLogs.log).
  - Unity may not exit on tests failure, but will exit the process and will close opened Unity Editor instance; [parseTestErrors.sh](.parseTestErrors.sh) parses the XML and returns exit code 2 if any <stack-trace> appears. Treat that as the truth. This will also update [CompileErrorsAfterUnityRun.txt](CI/CompileErrorsAfterUnityRun.txt) which can show Unity Editor compile time errors, if the text file is not empty.

- Running a single test (not built into scripts):
  - Add -testFilter "Namespace.ClassName.TestName" to the Unity CLI line in [runTestsBash.sh](.runTestsBash.sh) when needed.
  - Keep the rest of flags identical so CI/CITestOutput.xml stays authoritative and still parsed by parseTestErrors.sh.

- Code style and architectural constraints that are easy to miss:
  - Use one class per file and keep summaries explicit and compact.
  - Clear separation of Authoring data from Runtime data. Do not store runtime references in Authoring data.
  - Single sources of truth (don't duplicate any runtime data). All runtime data is in a single place per entity or in one runtime object/class.
  - Clear command-query method separation, command produce side-effects, query - no side effects.
  - No hidden assumptions.
  - Hot paths should be allocation-free.
  - When possible use structs instead of classes, but make sure to not use structs as keys in HashSet/Dictionaries/Lists (Unity IL2Cpp specific issue with generics based on the value type)
  - On each cross-module boundaries add [INTEGRATION] in summaries to methods; update wiring hub or if doesn't exists create one.
  - Nullable context for POCOs (C# 8 with Nullable (?), but make sure '#nullable enable' statement is at the top os the script).
  - Add concise Range-Condition-Output comments on critical methods.
  - Guardrails for change proposals: do not add new frameworks or packages without explicit approval; stay within C# 8 and Unity 6 constraints [techContext.md](memory-bank/techContext.md).
  - prefer to group logic pieces in a single clearly labeled method. This massively helps in review (user reviews all edits).
  - when using names: prefer "Refresh"/"Simulate" instead of "Update". Avoid names that conflicts with "magic" Unity events/methods. This helps to clearly separate manual method calls, from "magic" Unity events.
  - MVC separation for UI. Model(poco-runtime-data)-view(UGUI/UIToolkit)-controller(monobehaviour, orchestration of bindings, owner of logic and owner of model).
  - single file = single class/enum, single responsibility.
  - prefer simple SOLID principles when planning/executing task.

## Memory bank

Only if user asked "read memory bank": MUST read ALL memory bank files before the actual task - this is not optional. Make sure to review and plan beforehand, if any questions/inconsistencies arise - ask user explicitly. We need to be sure no hidden assumptions are made. Reading memory bank is required:
Source-of-truth docs live under [memory-bank/](memory-bank/): read [techContext](memorybank/techContext.md), [projectrules](memorybank/projectrules.md), [FeatureRouter](memorybank/FeatureRouter.md).

## Core Files (Required)

1. `techContext.md`
   - Technologies used
   - Development setup
   - Technical constraints
   - Dependencies
   - Extensions/plugins/tools usage patterns

2. `projectrules.md`
   - Core Modules and their connections
   - Key technical decisions
   - Key principles and patterns in use
   - Integration map (wiring hub updates)
   - Important project patterns and preferences

3. `FeatureRouter.md`
   - compact routing index from touched systems to authoritative feature docs
   - mandatory read before planning/editing

## Milestones workflow (specification update workflow)

- Features are grouped in Milestones (`DetailedDocs/Milestone1-N.md`)
- `Milestone1-N.md` has a general plan
- When starting to work on a milestone, break it down into tasks and set them under `progress.md` as immediate tasks (we track tasks only in `progress.md`). Coalesce left-over tasks with immediate tasks for the same milestone.
- After tasks are broken down and merged with left-over tasks, proceed to implementation.
- when finishing any milestone tasks, clean up `progress.md`, update `DetailedDocs/MilestoneXXX.md`, update memory bank files, remove obsolete information and clean-up duplicated information.

## Additional documentation

Create additional files/folders within DetailedDocs/ when they help organize:

- Complex feature documentation
- Integration specifications
- API documentation
- Testing strategies
- Deployment/Integration procedures

## Memory bank update

1. When task is finished, trigger **update memory bank**
2. **update memory bank** is also triggered when user requests with **update memory bank**
3. When triggered **update memory bank**, I MUST review every memory bank file, even if some don't require updates. Focus particularly on progress.md and systemPatterns.md as they track current state. Make sure to remove duplicates from the memory bank and stale/obsolete information.

flowchart TD
    Start[Update Process]

    subgraph Process
        P1[Review ALL Files]
        P2[Document Current State]
        P3[Clarify Next Steps]
        P4[Document Insights & Patterns]

        P1 --> P2 --> P3 --> P4
    end

    Start --> Process

## CI/Tests/Verification

- See: [vscode.tasks.json](../../.vscode/tasks.json), [RunUnityTestsReadme.md](../../CI/RunUnityTestsReadme.md)
- Build(rebuild solution): [Fully Compile by Unity](../.vscode/tasks.json) and check [CompileErrorsAfterUnityRun.txt](CI/CompileErrorsAfterUnityRun.txt) for any compilation errors (will be empty if no errors), only full rebuild or running unity tests require Unity Editor, so it is required all unity editors with current project to be closed.
- Tests (from repo root, it is required all unity editors with current project to be closed): "C:\Program Files\Git\bin\bash.exe" ./runTestsFromRoot.sh
  - Ensures CI/CITestOutput.xml refreshed
- Run [runParsetests.sh](runParsetests.sh): 
  - Ensure [Unity Editor compiler errors](CI/CompileErrorsAfterUnityRun.txt) is empty (no compilation errors while running Unity Editor).
  - See output of [runParsetests.sh](runParsetests.sh) to check if there are any failed tests, it will also enumerate them if failed tests exists.

## When finished with task/job

Propose next task and always ask: what should I do next?
  
