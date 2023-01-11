﻿# ClassicAssist.Avalonia

[Avalonia](https://github.com/AvaloniaUI/Avalonia) port for
[ClassicAssist](https://github.com/Reetus/ClassicAssist/)

ClassicAssist.Avalonia is under development but is in a useable state, features
are being added constantly.

## Installation

Add the full path to ClassicAssist.Avalonia.exe to the plugin section of the
ClassicUO settings.json config file. When running ClassicUO via `mono`, set the
`MONO_PATH` directory to the plugin's main folder.

Example:

```
MONO_PATH=./ClassicAssist.Avalonia-macos-latest/ \
DYLD_LIBRARY_PATH=./ClassicUO/bin/Debug/osx \
mono ./ClassicUO/bin/Debug/ClassicUO.exe -plugins $(pwd)/ClassicAssist.Avalonia-macos-latest/ClassicAssist.Avalonia.exe
```

### Mac-specific 

1. When using the plugin downloaded from GitHub, you may be prompted that the
   authenticy of various executables cannot be verified, and therefore blocked
   from loading. You will need to go to System Preferences to allow the three
   libraries to be loaded without verification:

- `libAvaloniaNative.dylib`
- `libHarfBuzzSharp.dylib`
- `libSkiaSharp.dylib`
### Linux-specific

TODO

## Troubleshooting

1. ClassicUO starts, but plugin does not load. Possibly with an error like:

```
06:34:33 |  Error   | Plugin threw an error during Initialization. Exception has been thrown by the target of an invocation.   at System.Reflection.RuntimeMethodInfo.Invoke (System.Object obj, System.Reflection.BindingFlags invokeAttr, System.Reflection.Binder binder, System.Object[] parameters, System.Globalization.CultureInfo culture) [0x00083] in <d8f7443201d046bba21dd0e544991bc5>:0 
  at System.Reflection.MethodBase.Invoke (System.Object obj, System.Object[] parameters) [0x00000] in <d8f7443201d046bba21dd0e544991bc5>:0 
  at ClassicUO.Network.Plugin.Load () [0x00404] in <a9bfc21b3c9b4501a0fff70af202b994>:0  Could not resolve field token 0x04000001, due to: Could not load type of field 'Assistant.Engine:<MainWindow>k__BackingField' (1) due to: Could not load file or assembly 'Avalonia.Controls, Version=0.10.0.0, Culture=neutral, PublicKeyToken=null' or one of its dependencies. assembly:/Users/kevineady/UO/ClassicAssist.Avalonia-macos-latest/ClassicAssist.Avalonia.exe type:Engine member:(null)   at (wrapper managed-to-native) System.Reflection.RuntimeMethodInfo.InternalInvoke(System.Reflection.RuntimeMethodInfo,object,object[],System.Exception&)
  at System.Reflection.RuntimeMethodInfo.Invoke (System.Object obj, System.Reflection.BindingFlags invokeAttr, System.Reflection.Binder binder, System.Object[] parameters, System.Globalization.CultureInfo culture) [0x0006a] in <d8f7443201d046bba21dd0e544991bc5>:0
```

Solution: You are missing the `MONO_PATH` environmental variable; set it to the
plugin's directory.

2. ClassicUO fails to start. Possibly with an error like:

```
06:36:24 |  Trace   | Running game...
[ERROR] FATAL UNHANDLED EXCEPTION: System.DllNotFoundException: libFNA3D.0.dylib assembly:<unknown assembly> type:<unknown type> member:(null)
  at (wrapper managed-to-native) Microsoft.Xna.Framework.Graphics.FNA3D.FNA3D_PrepareWindowAttributes()
  at Microsoft.Xna.Framework.SDL2_FNAPlatform.CreateWindow () [0x00001] in <862046ce91544f70b4ccd941bd46d589>:0 
  at Microsoft.Xna.Framework.Game..ctor () [0x0010b] in <862046ce91544f70b4ccd941bd46d589>:0 
  at ClassicUO.GameController..ctor () [0x00024] in <a9bfc21b3c9b4501a0fff70af202b994>:0 
  at ClassicUO.Client.Run () [0x0001a] in <a9bfc21b3c9b4501a0fff70af202b994>:0 
  at ClassicUO.Bootstrap.Main (System.String[] args) [0x00339
```

Solution: You are missing the `DYLD_LIBRARY_PATH` environmental variable; set it
to the correct native libraries folder within ClassicUO (eg. `./bin/Debug/osx`
on Mac)

## Known Issues 

### Mac-specific

1. When exiting ClassicUO, you will see a native mono crash; see issues at
   [Avalonia](https://github.com/AvaloniaUI/Avalonia/issues/4423),
   [SkiaSharp](https://github.com/mono/SkiaSharp/issues/1442)

### Linux-specific

Need to symlink libdl.so to libdl.so.2

![image](https://user-images.githubusercontent.com/6239195/211748863-681850a0-3275-4386-895a-46dff587556c.png)

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
