# AGENTS.md

Notes for coding agents working in this repository. See `BUILDING.md` for the architecture, build
layout and release process; this file covers only what an agent needs on top of that.

## Build and test

```bash
dotnet build ClassicAssist.slnx
dotnet test  ClassicAssist.Tests/ClassicAssist.Tests.csproj
```

Output goes to `Output/ClassicAssist/`. If the build reports `MSB3026` / `MSB3061` (could not copy /
delete), a ClassicAssist or client process is holding the output DLLs - close it and rebuild. These
are not code warnings and need no source change.

## Code style

Match the surrounding code: spaces inside parentheses (`Foo( a, b )`), braces on every control
statement, explicit types rather than `var`, file-scoped namespaces in the net10.0 projects.
`.editorconfig` is the source of truth and is enforced during build.

Style rules are enforced only in **`ClassicAssist.Avalonia`, `ClassicAssist.Launcher` and
`ClassicAssist.Shared`**. The other projects (`ClassicAssist.Plugin`, `ClassicAssist.Plugin.Shared`,
`ClassicAssist.Tests`, `ClassicAssist.HeadlessTests`, `ClassicAssist.Updater`, `Tools/*`) are out of
scope - do not reformat them. `BUILDING.md` explains why.

## Resolving code warnings

Get the list from a clean build, deduplicated (each warning is printed twice):

```bash
dotnet build ClassicAssist.slnx --no-incremental 2>&1 \
  | grep -E 'warning [A-Z]+[0-9]+' | sed -E 's/ \[[^]]*\]$//' | sort -u
```

### Do not run `dotnet format ClassicAssist.slnx` unscoped

Despite what `BUILDING.md` suggests, a bare `dotnet format` on the solution is not safe to commit
as-is. It runs whitespace, style **and analyzer** fixers across every project, including the
out-of-scope ones, and in practice it has:

- rewritten `PluginEngine.Install` into `async Task InstallAsync` (a VSTHRD analyzer fix) in the
  multi-targeted plugin project;
- deleted comment blocks and XML doc comments, including in `ClassicAssist.Shared`.

If it has already been run, discard it (`git checkout -- <paths>`) rather than trying to salvage the
diff - and remember that also discards any unrelated uncommitted work in those paths.

### Safe procedure

1. **Formatting (IDE0055) first, whitespace only, one enforced project at a time:**

   ```bash
   for p in ClassicAssist.Avalonia ClassicAssist.Launcher ClassicAssist.Shared; do
     dotnet format whitespace $p/$p.csproj
   done
   git diff -w --stat   # should show nothing but files you had already changed
   ```

2. **Then style rules one diagnostic at a time**, staging after each so every rule's diff can be
   reviewed on its own:

   ```bash
   dotnet format style $p/$p.csproj --diagnostics IDE0031 --severity info
   git diff                      # review
   git add -A
   ```

3. **After every step, check for damage:**

   ```bash
   git diff | grep -E '^-\s*(//|///)'       # removed comments - should be empty
   grep -rn '^<<<<<<<' --include=*.cs .     # conflict markers from multi-targeted projects
   git diff | grep -E '^\+.*\b(async|await)\b'  # unexpected async rewrites
   ```

   Large deletion counts deserve a read even when the checks pass - make sure the fixer only
   collapsed code (null checks, initialisers) and did not drop logic.

4. **Fix by hand** whatever has no code fix (e.g. `IDE0060` unused parameter - often an event
   handler signature that must stay, in which case discard the parameter with `_` or suppress
   locally with a reason) and anything outside the IDE rules:
   - `CS0618` / `CS0612` (obsolete API): migrate to the replacement the message names; if there is
     none, leave it and say so rather than suppressing silently.
   - `CS0169` (unused field): delete it, after confirming nothing reaches it via reflection.
   - `VSTHRD002` (sync wait on a task): do not let a fixer convert call chains to async; fix the
     one call site, or leave it with a justification if the sync wait is deliberate.

5. **Rebuild and run the tests.** The warning count for the enforced projects should be zero, and the
   build must still succeed for both `net10.0` and `net472` targets.

Never silence a warning by lowering its severity in `.editorconfig` or adding `#pragma` without
saying why in a comment next to it.
