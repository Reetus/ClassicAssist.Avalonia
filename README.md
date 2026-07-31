# ClassicAssist.Avalonia

[Avalonia](https://github.com/AvaloniaUI/Avalonia) port of
[ClassicAssist](https://github.com/Reetus/ClassicAssist/).

> **Status:** under development and usable, but **not** at feature parity with ClassicAssist.
> A number of macro commands and hotkeys are still missing — see [MACRO_COMMANDS_TODO.md](MACRO_COMMANDS_TODO.md)
> and [HOTKEYS_TODO.md](HOTKEYS_TODO.md) for what is currently absent.

## How it works

Avalonia requires the UI to own the process **main** thread, which a plugin loaded into the game
process can never provide. ClassicAssist is therefore split in two:

| Piece | What it is | Where it runs |
| --- | --- | --- |
| `ClassicAssist.dll` | thin plugin (`ClassicAssist.Plugin`) | inside the game process |
| `ui/ClassicAssist.Avalonia` | the assistant and its UI | its own child process |

The plugin launches the UI process and the two talk over RPC (StreamJsonRpc) against the
`IHostMethods` / `IPluginMethods` contracts in `ClassicAssist.Plugin.Shared`. Every packet the client
sends or receives is round-tripped through the UI process so it can be inspected, blocked or
rewritten.

## Which file to point the client at

The client is told about the plugin through the `plugins` array in its `settings.json`. **Which file
you name depends on how your client loads plugins**, and getting it wrong is the most common reason
nothing happens:

| Client | Point it at | Why |
| --- | --- | --- |
| Modern ClassicUO | `ClassicAssistNE.dll` / `ClassicAssistNE.so` / `ClassicAssistNE.dylib` | Only loads plugins natively — it needs a real exported `Install` symbol |
| TazUO | `ClassicAssist.dll` | Tries the native path, then falls back to a managed load of `Assistant.Engine.Install` |
| Legacy ClassicUO/TazUO (Mono) | `framework/ClassicAssist.dll` | .NET Framework build; a modern .NET assembly is rejected outright |

`ClassicAssistNE` is the DNNE native shim; the extension follows the platform. The `NE` suffix is
deliberate — a native binary named `ClassicAssist.dll` would collide with the managed assembly of the
same name.

Both entry points end up in the same place, so a client that supports either will work with either.
The native shim is only built if a C toolchain is present; see below.

Example:

```json
"plugins": [ "/full/path/to/Output/ClassicAssist/ClassicAssist.dll" ]
```

Relative paths are resolved against the client's `Data/Plugins/` directory, so you can also copy the
whole folder there and use `ClassicAssist/ClassicAssist.dll`.

## Building

Requires the .NET 10 SDK.

```bash
dotnet build ClassicAssist.slnx
dotnet test  ClassicAssist.Tests/ClassicAssist.Tests.csproj
```

Everything lands in `Output/ClassicAssist/`, which **is** the deployable folder:

```
Output/ClassicAssist/
├── ClassicAssist.dll          managed plugin (net10.0)
├── ClassicAssistNE.{so,dll,dylib}   native shim, only if clang was available
├── framework/                 .NET Framework plugin for legacy Mono hosts
└── ui/                        the assistant and Avalonia UI
```

Note that the two halves are produced by different projects. Building a single project is usually not
enough to update what actually runs:

| To rebuild | Build |
| --- | --- |
| the plugin | `ClassicAssist.Plugin/ClassicAssist.Plugin.csproj` |
| the assistant / UI (including macro commands) | `ClassicAssist.Avalonia/ClassicAssist.Avalonia.csproj` |

Building `ClassicAssist.Shared` on its own only writes to that project's `bin/` — it does not reach
`Output/ClassicAssist/ui/`.

### The native shim

The shim needs `clang`, so it builds itself only when clang is actually present. That way a plain
`dotnet build` produces everything the machine can produce, and a machine without a C toolchain still
builds rather than failing — you will see a message saying the shim was skipped.

```bash
# force it on or off
dotnet build ClassicAssist.Plugin/ClassicAssist.Plugin.csproj -p:EnableDnne=true
```

To confirm the export exists:

```bash
nm -D --defined-only ClassicAssistNE.so | grep ' T Install'
```

The shim is deliberately not built for the .NET Framework target: that build exists for a Mono host,
which always takes the managed path.

See [BUILDING.md](BUILDING.md) for the details behind the two-process split, the DNNE load context and
the raw-function-pointer header exchange.

## License

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
