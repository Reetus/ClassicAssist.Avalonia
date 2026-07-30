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

Modern ClassicUO only does the native path, so it needs a real exported `Install` symbol. That build
is opt-in because it requires `clang`:

```bash
sudo apt install clang
dotnet build ClassicAssist.Plugin/ClassicAssist.Plugin.csproj -p:EnableDnne=true
```

This produces `ClassicAssistNE.so` (`.dll` on Windows) next to the managed `ClassicAssist.dll`.
**Point the client at that file, not at `ClassicAssist.dll`** — the "NE" suffix is DNNE's default and
is worth keeping, because on Windows a native binary named `ClassicAssist.dll` would collide with the
managed assembly of the same name. Override with `DnneNativeBinaryName` if you really need to.

Two things the shim needs that are easy to miss, both handled by the `EnableDnne` property group in
`ClassicAssist.Plugin.csproj`:

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

- The plugin targets `net9.0`; TazUO ships a self-contained `net10.0` runtime and rolls forward
  automatically. The UI sets `RollForward=LatestMajor` so it runs on whatever runtime is installed.
- `cuoapi.dll` is referenced with `<Private>false</Private>` — compile-time only. It must not be
  copied to the output; the client's own copy is what binds at run time. See the comment in
  `ClassicAssist.Plugin.csproj` before changing this.
