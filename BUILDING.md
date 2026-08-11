# Building and running (Linux / TazUO)

## Why there are two processes

Avalonia on Linux requires the UI to own the process **main** thread. A plugin DLL loaded into the
game process can never satisfy that, so ClassicAssist is split in two:

| Piece | What it is | Where it runs |
| --- | --- | --- |
| `ClassicAssist.dll` | thin plugin (`ClassicAssist.Plugin` project) | inside ClassicUO/TazUO |
| `ui/ClassicAssist.Avalonia` | the whole assistant + Avalonia UI | its own child process |

The plugin starts the UI process and the two talk over a named pipe using StreamJsonRpc, against the
`IHostMethods` / `IPluginMethods` contracts in `ClassicAssist.Plugin.Shared`. Every packet the client
sends or receives is round-tripped through the UI process so it can inspect, block or rewrite it.

## Build

```bash
dotnet build ClassicAssist.slnx           # everything
dotnet test  ClassicAssist.Tests/ClassicAssist.Tests.csproj
```

Everything lands in `Output/ClassicAssist/`, which *is* the deployable folder.

## Releases and the updater

`ClassicAssist.Updater` ships inside that folder and updates it in place. It reads the GitHub
releases API - by default
`https://api.github.com/repos/Reetus/ClassicAssist.Avalonia/releases`, overridable via
`ReleasesURL` in `updater.settings.json` beside the binaries - so cutting a release is the only
publishing step; there is no separate manifest to keep in sync.

A release is expected to carry **one archive per platform**, named with its runtime identifier:

```
ClassicAssist-<version>-win-x64.zip
ClassicAssist-<version>-linux-x64.zip
ClassicAssist-<version>-osx-x64.zip
ClassicAssist-<version>-osx-arm64.zip
```

Those four are produced by `.github/workflows/release.yml`, run by hand from the Actions tab with
the version as its only required input. Each platform builds on its own runner in parallel - what
differs between the packages is the apphosts and the DNNE native shim, and both need the target's
own toolchain - and a final job attaches all four to one draft release tagged with the version.
Drafts are invisible to the updater, so nothing reaches users until the release is published.

Packages are trimmed to the platform they are for before zipping: a runtime-agnostic build carries
native binaries for every RID under the sun, ~245MB of a ~290MB tree, and the updater reads the
whole download into memory on a five minute timeout. The keep list lives in the workflow matrix, by
OS family rather than by architecture, and the build fails if trimming leaves a tree with no Skia in
it.

Matching is on separator-delimited tokens, not exact names, so the surrounding convention can
change without breaking updaters already in the wild. A release carrying exactly one archive and no
platform naming is taken as-is, which is what makes a single-package repository work before
per-platform builds exist. A release with nothing for the running platform is skipped rather than
offered and then failed on.

The update applies in two stages, because an updater cannot overwrite its own binaries: the copy in
the install downloads and extracts the package to a temp folder, then hands over to the updater
*inside* that package with `--stage Install` to perform the copy.

Before copying anything, every file the package would overwrite is probed for write access, and the
whole update is refused if any of them fail - a partial copy would leave an install made of two
versions. Windows locks files that a running client has mapped, so this catches an open client
there; on Linux and macOS a mapped assembly is not locked, and what covers that case is closing the
running clients first (detected via `/proc/<pid>/maps` on Linux).

## Code style

`.editorconfig` is enforced during build (`EnforceCodeStyleInBuild` in `Directory.Build.props`), so
style violations surface as build warnings rather than IDE-only hints. To apply the fixes:

```bash
dotnet format ClassicAssist.slnx                      # apply
dotnet format ClassicAssist.slnx --verify-no-changes  # check only, for CI
```

Rules are enforced for **`ClassicAssist.Avalonia`, `ClassicAssist.Launcher` and
`ClassicAssist.Shared`** — the projects that target `net10.0` only. Everywhere else the `Style`
category is `none`, so those projects build without style warnings and `dotnet format` leaves them
alone. Within the enforced scope each rule is opted in individually in `.editorconfig`, with a
comment explaining why.

The WPF tree is net48 / C# 7.3 and cannot use the modern constructs enabled here — collection
expressions, `new()`, `not` patterns, file-scoped namespaces — so expect those to differ when
diffing the two trees.

The scope is not arbitrary. `ClassicAssist.Plugin` and `ClassicAssist.Plugin.Shared` multi-target
`net10.0;net472`, and `dotnet format` computes fixes per target framework: where `#if` blocks make
the two syntax trees diverge it writes **git conflict markers into the source** rather than
reconciling them. `PluginEngine.cs`, with 54 preprocessor directives, cannot be auto-formatted at
all. Narrowing a project to one framework to work around this is **not** safe either — IDE0005 then
strips usings referenced only from `#if NETFRAMEWORK` regions, breaking the net472 build.

If you do widen the scope, check for damage afterwards:

```bash
grep -rn '^<<<<<<<' --include=*.cs .
```

Bulk reformats are listed in `.git-blame-ignore-revs`. GitHub applies it automatically; for local
`git blame`, run once:

```bash
git config blame.ignoreRevsFile .git-blame-ignore-revs
```

## Deploy

```bash
cp -r Output/ClassicAssist <TazUO>/Data/Plugins/
```

Then point the client at it in the TazUO `settings.json`:

```json
"plugins": [ "ClassicAssist/ClassicAssist.dll" ]
```

Paths in `plugins` are relative to `<TazUO>/Data/Plugins/`.

## DNNE (optional, for modern ClassicUO)

TazUO tries `dlopen` first and falls back to a managed load of `Assistant.Engine.Install`. On Linux
the native attempt always fails immediately (no standalone `libdl.so` on modern distros), so the
managed path is what runs and **no native shim is needed**.

Modern ClassicUO only does the native path, so it needs a real exported `Install` symbol. Building the
shim requires `clang`, so it turns itself on only when clang is actually present — a plain
`dotnet build` produces every artifact the machine can produce, and a machine without a C toolchain
still builds rather than failing (with a message saying the shim was skipped). Force it either way:

```bash
sudo apt install clang
dotnet build ClassicAssist.Plugin/ClassicAssist.Plugin.csproj -p:EnableDnne=true
```

This produces `ClassicAssistNE.so` (`.dll` on Windows) next to the managed `ClassicAssist.dll`.
**Point the client at that file, not at `ClassicAssist.dll`** — the "NE" suffix is DNNE's default and
is worth keeping, because on Windows a native binary named `ClassicAssist.dll` would collide with the
managed assembly of the same name. Override with `DnneNativeBinaryName` if you really need to.

### Why the plugin never uses `Marshal.GetDelegateForFunctionPointer`

DNNE activates the plugin through hostfxr's `load_assembly_and_get_function_pointer`, which **always**
uses an `IsolatedComponentLoadContext`. There is no opt-out. So under DNNE the plugin loads its own
`cuoapi`, and its `CUO_API` delegate types are a different identity from the client's even though the
file and version are identical.

That breaks the obvious way of exchanging the `PluginHeader`:

- `Marshal.GetDelegateForFunctionPointer<T>` on a host pointer throws `InvalidCastException`, because
  the pointer is a managed thunk it tries to unwrap back into a delegate type we don't share.
- A delegate *we* publish makes the client throw the same way when it reads the header back.
- Where it doesn't throw it silently corrupts: the runtime falls back to a real marshalling stub, and
  `ref byte[]` carries no element count across one, so packet buffers arrive with a length unrelated
  to the count beside them. That was the `ArgumentException` out of `FilterPacket`.

So `PluginEngine` exchanges the header **entirely through raw function pointers** — everything we
publish is `[UnmanagedCallersOnly]`, everything we consume is invoked through a
`delegate* unmanaged[Cdecl]`. A calli is just an address and a signature, so the same registration is
correct on every host and every load path.

One consequence: `OnRecv` / `OnSend` are left null. They take `ref byte[]`, which is not blittable and
so cannot be exposed as a native pointer at all. Modern ClassicUO, ClassicUO.Bootstrap and TazUO all
prefer `OnRecv_new` / `OnSend_new` and only fall back to the old pair when those are null, so nothing
is lost. `cuoapi`'s `PluginHeader` stops at `SetTitle`, so those four slots are reached by hand at
offsets 184 / 192 / 200 / 208 — which assumes the client passes the long header. Every current client
does.

Two more things the shim needs that are easy to miss, both handled by the `EnableDnne` property group
in `ClassicAssist.Plugin.csproj`:

- **`Assistant.Engine.NativeInstall`** carries `[UnmanagedCallersOnly(EntryPoint = "Install")]`; DNNE
  only exports methods with that attribute. It is a *separate* method from `Install` on purpose —
  `UnmanagedCallersOnly` forbids managed callers, and `MethodInfo.Invoke` on such a method throws,
  which would break the reflection path every client on Linux depends on.
- **A `runtimeconfig.json`** beside the managed assembly, since the shim starts the runtime through
  hostfxr. Class libraries do not emit one by default, so without it `dlsym` finds `Install` and the
  call then fails to activate the runtime.

To check the export after building:

```bash
nm -D --defined-only ClassicAssistNE.so | grep ' T Install'
```

## Notes

- The plugin multi-targets `net10.0` and `net472`; the latter lands in `framework/` and exists for
  legacy Mono hosts. The UI sets `RollForward=LatestMajor` so it runs on whatever runtime is
  installed.
- `cuoapi.dll` is referenced with `<Private>false</Private>` for the managed build — compile-time
  only, since the client's own copy is what binds at run time. The DNNE build sets `Private=true`,
  because the isolated load context has to resolve it from the plugin folder. Either way the plugin
  no longer depends on the two sides agreeing on delegate identity; see above.
