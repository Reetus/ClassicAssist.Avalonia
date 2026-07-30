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

## Notes

- The plugin targets `net9.0`; TazUO ships a self-contained `net10.0` runtime and rolls forward
  automatically. The UI sets `RollForward=LatestMajor` so it runs on whatever runtime is installed.
- `cuoapi.dll` is referenced with `<Private>false</Private>` — compile-time only. It must not be
  copied to the output; the client's own copy is what binds at run time. See the comment in
  `ClassicAssist.Plugin.csproj` before changing this.
