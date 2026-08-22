# ClassicAssist.Avalonia feature gap TODO

Gap between this repo and the WPF `ClassicAssist` tree. The three focused TODOs in this
directory (`ECV_TODO.md`, `HOTKEYS_TODO.md`, `MACRO_COMMANDS_TODO.md`) cover their areas in depth;
this file is the broad cross-cutting summary of everything still missing, with pointers.

## Dead buttons (VM wired, no view)

The ViewModel exists and calls `Engine.UIInvoker.InvokeDialog`/`Invoke` by window name, but no
window with that name exists in the Avalonia assembly. `AvaloniaUIInvoker.FindWindowType`
(AvaloniaUIInvoker.cs:176) then prints "Cannot find type: <name>" and no-ops, so the button does
nothing.

- [x] ~~**CustomPropertiesWindow**~~ - "Define Custom Properties" in the Autoloot tab
      (`AutolootViewModel.DefineCustomProperties`, AutolootViewModel.cs:447). Added as
      `Views/Autoloot/CustomPropertiesWindow.axaml`; the `CustomPropertiesViewModel` round-trips
      `ArgumentIndex`/`ClilocIndex` and raises a `Saved` event that open ECV windows subscribe to.
- [x] ~~**ClilocSelectionWindow**~~ - "Choose by Cliloc" from the Custom Properties window
      (`CustomPropertiesViewModel.ChooseFromCliloc`). Added as
      `Views/Autoloot/ClilocSelectionWindow.axaml`.
- [x] ~~**PropertySelectionWindow**~~ - "Choose from Item" from the Custom Properties window
      (`CustomPropertiesViewModel.ChooseFromItem`). Added as
      `Views/Autoloot/PropertySelectionWindow.axaml`.
- [x] ~~**Filter Configure windows**~~ - all five added under `Views/Filters/`
      (`ClilocFilterConfigureWindow`, `RepeatedMessagesFilterConfigureWindow`,
      `SeasonFilterConfigureWindow`, `SoundFilterConfigureWindow`, `ItemIDFilterConfigureWindow`)
      with view models in `ClassicAssist.Shared/UI/ViewModels/Filters/`. The General tab's filter
      rows show a configure button whenever `FilterEntry.IsConfigurable` is set.
      `IConfigurableFilter.Configure()` returns `Task` here rather than `void`: Avalonia has no
      blocking `ShowDialog()`, so the dialogs go through `Engine.UIInvoker.InvokeDialog` and are
      awaited.
- [ ] **Autologin configure / status** - the General tab has no Autologin section at all
      (`AutologinConfigureCommand`, `AutologinStatusWindow` absent).
- [ ] **Backup settings** - General tab "Backup Settings" button absent
      (`BackupSettingsCommand`, `BackupWindow`/`BackupSettingsWindow` absent).

## Missing subsystems (VM + view both absent)

- [ ] **Trap Pouches** - `TrapPouchManager`/`TrapPouchEntry` + `TrapPouchTabControl` agent tab.
      Blocks macro commands `ClearTrapPouch`/`SetTrapPouch`/`UseTrapPouch` and hotkeys
      `Use Trap Pouch`/`Clear Trap Pouches`.
- [x] ~~**Screenshot**~~ - `ScreenshotManager` + `ScreenshotTabControl` agent tab (gallery, info bar,
      death triggers with the mobile-body filter), plus `MainCommands.Snapshot` and the
      `Take Snapshot` hotkey.

      The capture itself is the part that could not be ported. Upstream `BitBlt`s the client window
      from the handle in the plugin header, which is Windows-only and, on every client this repo
      loads into, has no handle to work from: TazUO passes `IntPtr.Zero` on all platforms, ClassicUO
      only fills it in `if (CUOEnviroment.IsWindows)`, and `ClassicUO.Bootstrap` never does. So the
      pixels come from the client's own graphics device instead -
      `ReflectionImpl.CaptureClientFrame` reads FNA's backbuffer (or the render target the client
      composes the frame into, whichever it is using, mirroring its own PrintScreen handler) on the
      client thread via `TickWorkQueue`, and hands the UI a temp file of raw RGBA rather than
      megabytes of base64 over the JSON-RPC link. That works identically on Windows, Linux and macOS
      and captures under Wayland, where taking another window's pixels is not possible at all.
      `AvaloniaScreenshotComposer` draws the watermark and info bar over it and encodes the PNG.

      Two consequences. Upstream's UO-only/fullscreen radio pair is gone - what is captured is the
      frame the client drew, so there is no desktop to include; `Snapshot`'s `fullscreen` argument is
      still accepted and ignored so existing macros run. And a NativeAOT ClassicUO cannot be
      captured at all, since its graphics stack is native code: the tab detects that through
      `CanCaptureClientFrame` and disables itself with a message saying so. That probe deliberately
      does not use `Engine.ReflectionAvailable`, which is true on that client - it loads the plugin
      managed through its bootstrap, and the bootstrap ships a decoy managed `ClassicUO` assembly
      whose `Client.Game` is a stub `GameController`, so the load-path flag, the assembly-name probe
      and the type probe all pass while there is nothing to read.
- [ ] **Name Overrides** - `NameOverrideManager` + `NameOverrideTabControl` agent tab.
- [ ] **Backups** - `Data/Backup/{GoogleDrive,Mega,OneDrive,WebDAV}` + backup/restore UI.
- [ ] **Chat window** - `ChatManager` is ported but there is no `ChatWindow`; the
      `Show Chat Window` hotkey and chat window `Options.ChatWindow*` fields are absent.
- [ ] **GIF recorder** - `GIFRecorderWindow` + `Show GIF Capture` hotkey. The capture side now
      exists (`IHostMethods.CaptureClientFrame`, added for the Screenshot agent), but a recorder
      wants tens of frames a second and each one is a full backbuffer read on the client's thread
      plus a temp file - so the frame transport, not the window, is the open question here.
- [ ] **Public Macros browser** - the "Public Macros" tab (`MacroBrowserControl`,
      `MacroBrowserViewModel`, classicassistant.net API) is absent from the main window.
- [ ] **Macro commands browser** - `MacrosCommandWindow`/`MacrosCommandViewModel` behind the Macros
      tab's "Commands" button; `MacrosTabViewModel.ShowCommands` is still a stub, so no button is
      surfaced for it.
- [x] ~~**Active Objects window**~~ - `ActiveObjectsWindow` + `ActiveObjectsViewModel`: global /
      instance / player aliases, lists, timers and the ignore list, each refreshable with
      remove/clear-all, plus re-target buttons for aliases. Snapshots rather than live bindings, since
      the underlying stores are plain dictionaries mutated from macro threads.
- [x] ~~**Macro editor completion**~~ - `PythonCompletionData` + `AvalonEditCompletionBehaviour`
      (AvaloniaEdit `CompletionWindow` on `TextArea.TextEntered`, hover docs via
      `TextEditor.PointerHover`). Commands are reflected out of ClassicAssist.Shared rather than the
      executing assembly, which upstream could assume were the same. WPF's `CompletionEntry`
      expander (a read-only editor showing the example) is simplified to signature-over-example.
- [x] ~~**Macro debugger (DAP)**~~ - `DebugAdapter/*` (Dap server/session/types, `DebugManager`,
      `VariableInspector`, `GameStateInspector`, `MacroDebugState`) ported to
      `ClassicAssist.Shared/DebugAdapter/`; the DAP hook is wired into `MacroInvoker.OnTrace`
      (breakpoint/stepping/exception pauses) and `MacroManager.OnMacroStarted/OnMacroStopped`
      (thread events); file-backed macros run with their `.py` path as the script filename so
      breakpoints map to the file open in VSCode. The Debug Adapter toggle + port live in the
      Options tab (`Options.DebugAdapterEnabled/Port`, session-only, `DapServer` loopback listener).
- [x] ~~**Macro debugger (in-app)**~~ - `BreakpointMargin` + `AvalonEditBreakpointMarginBehaviour`
      (AvaloniaEdit gutter margin, click to toggle breakpoints, line numbers shifted on edits),
      `AvalonEditPausedLineBehaviour` (translucent-yellow current-line highlight + auto-scroll),
      `MacrosDebuggerControl` (floating Resume/Step/Stop overlay + resizable frame-variable panel,
      double-click an `Item` variable into the Object Inspector), all under
      `ClassicAssist.Avalonia/Misc` and `Views/Macros/`. `MacroEntry.Resume()/Step()` added
      (the pause/breakpoint/frame-variable plumbing itself - `Breakpoints`, `IsPaused`,
      `PausedLineNumber`, `FrameVariables` - was already ported). `AvalonEditCompletionBehaviour`
      gained the frame-variable hover branch (WPF's `AvalonEditShowCompletionTooltipBehaviour`
      folded command-hover and paused-variable-hover together; this repo keeps them in the same
      behaviour for the same reason). `F8`/`F12` bound on the Macros tab alongside the existing
      `F5`. One real bug fixed en route: `AvalonEditBehaviour.SetText` unconditionally replaced
      `Document.Text` on every keystroke (WPF swaps the whole `TextDocument` per macro instead, so
      it never hit this) - full-document `Document.Changed` events on every keystroke would have
      made the breakpoint line-shift logic misfire. Now guarded to a real no-op when the text
      already matches, and the remaining programmatic (macro-switch) replace is flagged via
      `IsProgrammaticTextChange` so the breakpoint margin can tell it apart from a real edit. A
      second bug found post-port (via an ad-hoc `Avalonia.Headless` harness driving the real
      `MacrosTabControl`, since the app needs a live plugin RPC connection to launch normally):
      Avalonia's hit-testing is content-based like WPF's, so a margin only receives pointer events
      where something was actually painted - a fresh `BreakpointMargin` with zero breakpoints
      painted nothing at all and so was **completely unclickable** (confirmed 0/510 sample points
      hit across its bounds). WPF avoided this with a `HitTestCore` override; the Avalonia fix is
      `Render` painting a transparent rect across the full bounds first, which brought it to
      510/510.

## Missing agent features (tab exists, feature dropped)

- [x] ~~**Organizer**~~ - stale entry, already fully implemented: per-entry `SourceContainer`/
      `DestinationContainer` (`OrganizerEntry.cs`), the granular `SetEntrySourceContainerCommand`/
      `ClearEntrySourceContainerCommand`/`SetEntryDestinationContainerCommand`/
      `ClearEntryDestinationContainerCommand` wired per-row in `OrganizerTabControl.axaml`
      (alongside, not replaced by, the combined target-based `SetContainersCommand` - both exist in
      WPF too), and `ReturnExcess` (property + checkbox + `OrganizerManager.ReturnExcess()`).
      (~~"Stop Organizer" category hotkey~~ done - a `_staticOptions` entry persisted under the
      top-level `OrganizerOptions` block, the same shape Dress already used.)
- [x] ~~**Dress**~~ - WPF relabels the Dress/Undress/Undress All buttons to "Stop" while running
      (click again to cancel); Avalonia's `IsDressingOrUndressing` combined flag disabled all three
      buttons while any one ran, so there was no way to click the running button a second time to
      stop it. Split back into `IsDressing`/`IsUndressing`/`IsUndressingAll` (matching WPF), each
      command's handler now calls `DressManager.Stop()` when its own flag is already set instead of
      restarting, and each button's `Content` is a `MultiBinding`/`BooleanToggleLabelConverter`
      swapping between `Strings.Stop` and its idle label. Also added `DressManager.Undress(
      DressAgentEntry)` - `UndressItems` was calling `dae.Undress()` directly, bypassing the
      manager's own `IsDressing`/cancellation-token tracking entirely, so `Stop()` could never have
      cancelled a single-item undress even with a working button.
- [x] ~~**Scavenger**~~ - stale entry, already fully implemented: `ScavengerPriority` enum +
      `ScavengerEntry.Priority` (ordering honored in `ScavengerTabViewModel`'s scavenge loop),
      `CheckWeight`/`MinWeightAvailable` (bound + enforced), and the Cliloc filter
      (`ScavengerClilocFilterEntry`/`ScavengerClilocFilterWindow`, opened via
      `Engine.UIInvoker.InvokeDialog` and applied in the match predicate).
- [x] ~~**Vendor Sell**~~ - added `ContainerSerial` + `SetContainerCommand`/`ResetContainerCommand`
      (`VendorSellTabControl.axaml`'s gear-icon-context-menu pattern, matching Autoloot's), the
      container-filtered sell path in `OnVendorSellDisplayEvent` (only sells items also present in
      the chosen container, capped to its stock), and `InsertMatchAnyCommand` (`Graphic=-1` wildcard
      entry) with the match predicate now honoring `Graphic == -1`. Also ported WPF's per-entry
      `Amount` budget tracking (a `Dictionary<VendorSellAgentEntry, int>` of remaining stock) since
      it lives in the same method - previously each matching stack was capped at `Amount`
      independently, so e.g. two stacks matching one `Amount = 5` rule could sell 10 total instead
      of 5. `ID from target` already existed. `IDisposable` added so the
      `VendorSellDisplayEvent` subscription matches WPF's cleanup (not otherwise invoked yet, since
      nothing currently disposes agent tab view models on tab teardown here).
- [x] ~~**Autoloot**~~ - groups/folders (`AutolootGroup`, `IDraggable` tree infra, New/Remove/
      Move-to-group commands), per-entry `Priority` (`AutolootPriority` enum + `Group`/`Priority` on
      `AutolootEntry`, serialized and honored in `OnCorpseEvent`/debug ordering), `LootHumanoids`
      (corpse-graphic check), `RequeueFailedItems` (new `DragDropOptions` requeue path in
      `ActionPacketQueue`), CSV import (`CSVImportWindow` + `CSVImportViewModel` + minimal
      `CsvReader`; `PropertyEntry.ShortName` and WPF `Properties.json` synced for column matching).

## Missing macro commands (10)

From `MACRO_COMMANDS_TODO.md` - everything not yet checked:

- [ ] `BringClientWindowToFront` (needs a Linux X11/Wayland implementation - not a
      `ReflectionCommands` wrapper upstream)
- [ ] `DisplayQuestPointer`
- [ ] `SetAutologin`
- [ ] `AddMapMarker`, `ClearMapMarkers`, `RemoveMapMarker` (no `MapCommands.cs`)
- [ ] `InterruptSpell`
- [ ] `ClearTrapPouch`, `SetTrapPouch`, `UseTrapPouch` (needs `TrapPouchManager`)

Signature gaps still open:

- [ ] `UseType` lacks `skipQueue`
- [ ] `MessageBox` is commented out in `MainCommands.cs`

## Missing hotkeys (4)

From `HOTKEYS_TODO.md`:

- [x] ~~`Take Snapshot`~~ (`SnapshotCommand`) - with `MainCommands.Snapshot`, which the Screenshot
      agent above provides.
- [x] ~~`Greater Heal / Cure Self`, `Mini Heal / Cure Self`~~ - with the configurable-hotkey
      infrastructure behind them: `HotkeyConfigurationAttribute`, `HotkeyEntry.Configurable`,
      `CureType`, `ConfigureHotkeyCommand` + the Options button on the Hotkeys tab, and the
      `Hotkeys/Options` profile array (serialized/deserialized in `HotkeysTabViewModel`, so a
      profile shared with WPF round-trips - covered by `ProfileRoundTripTests`, which no longer
      defers that path). The Options dialog is `HotkeyOptionsWindow` bound to
      `HotkeyOptionsViewModel`; WPF builds its equivalent Grid imperatively in code-behind,
      whereas this binds an `ItemsControl` so the reflection stays in the view model.
- [ ] `Use Trap Pouch`, `Clear Trap Pouches` (needs `TrapPouchManager`)
- [ ] `Show Chat Window`, `Show GIF Capture` (need the windows above)

## Missing Options

- [x] ~~**Show player/shard name in CUO title** (`SetUOTitle`)~~ - `Options.SetUOTitle`,
      `Engine.SetTitle()` (RPC `IHostMethods.SetTitle` -> plugin native `SetTitle` hook, set to
      `"PlayerName (ShardName)"` when enabled, cleared otherwise), wired into the player-incoming
      packet handler and the Options tab checkbox; serialized in `OptionsTabViewModel`.
- [ ] `Options` class: `Autologin*`, `ChatWindow*`, `LogoutDisconnectedPrompt`,
      `SelectedTabIndex*`, `SlowHandlerThreshold`, `SysTray`
      (~~`DebugAdapterEnabled/Port`~~ done - session-only, not persisted;
      ~~`DisableHotkeysLoad`, `HotkeysStatusGump*`, `MacrosGump*`~~ done)
- [x] ~~`DragDelay`/`DragDelayMS`~~ - `DragItem.ThrottleBeforeSend()` spaces 0x07 packets for
      Sphere-X style shards, called from `Engine.SendPacketToServer(BasePacket)` via a new
      `BasePacket.ThrottleBeforeSend` hook. Unlike WPF the delay is read from the options inside the
      packet rather than passed to the constructor, so the eight call sites keep their shape.
      Checkbox on the General tab.
- [x] ~~`LimitHotkeyTrigger`/`LimitHotkeyTriggerMS`~~ - per-key throttle in
      `Engine.OnHotkeyPressed`. `HotkeyManager.OnHotkeyPressed` now takes `noexecute` and returns
      `(found, filter)`: a throttled press still matches so the key stays withheld from the client,
      it just doesn't run the action.
- [x] ~~`ExpireTargetsMS`~~ - `TargetQueue<T>` already honoured it; it was never serialized or
      exposed. Now persisted and editable under Queue Last Target on the Options tab.
- [x] ~~Options tab layout~~ - `ResponsiveGrid` ported to `Controls/ResponsiveGrid.cs`, along with
      `OptionedCheckBox` and `HorizontalHeaderedContentControl`/`TextBox`/`ComboBox` (from the WPF
      `ClassicAssist.Controls` assembly; templates in `Controls/OptionsControls.Theme.xaml`). The tab
      now uses WPF's five groups - General / Target / Macros / Gumps / Other - in WPF's order, with
      WPF's `MacrosGumpControl` and `QueueLastTargetControl` inlined. Two deliberate differences:
      `MinColumnWidth` is a hard floor rather than WPF's widest-item rule (Avalonia measures wrapping
      text unconstrained as one line, which would collapse the grid to one column), and the Debug
      Adapter row stays a plain CheckBox because its port field is editable while *un*checked.
- [ ] Options tab UI: "Use Cliloc language from ClassicUO", "Logout on disconnected prompt" - both
      blocked on missing `Options`/`AssistantOptions` members (~~Debug Adapter toggle, "Disable
      hotkeys on profile load", "Hotkeys Status Gump", "Limit Hotkey retrigger", "Expire
      Targets"~~ done)
- [ ] General tab UI: Minimize to tray, Saved Passwords, Autologin section, Backup Settings button
      (~~filter Configure buttons, Drag delay~~ done)

## Missing filters

- [x] ~~`SoundFilter`, `ItemIDFilter`~~ - both ported with their configure windows.
      `Data/Filters/Audio/*.json` now ships with `ClassicAssist.Shared`. `BardsMusicFilter` (from
      upstream `develop`) was **removed**: `Skills.json` already has a "Bards Music" sound entry
      covering the same IDs, so it was pure duplication. `GeneralControlViewModel.Deserialize`
      migrates old profiles that had it enabled onto that SoundFilter entry.
- [x] ~~`ClilocFilter`/`RepeatedMessagesFilter` Configure dialogs~~ - see Dead buttons.

## ECV still missing (details in ECV_TODO.md)

- [ ] Organizer panel (toggle, two dropdowns, target button, Play via queued-action)
- [ ] Replace Name, Target Container, Autoloot Container
- [ ] "Move to set" context submenu
- [ ] Padlock overlay icon on locked tiles (cosmetic)
- [ ] `CustomToolbarActions` / `IEntityCollectionViewerAction` extensibility registry
- [ ] Boolean-tree filter groups (flat AND-only profiles instead - deliberate)
- [ ] `EntityCollectionData.NotifyPropertiesUpdated()` / live OPL update of rendered rows
- [ ] `EntityCollectionViewerOptions.Assemblies` round-trip (deliberately skipped)

## Cross-platform shelling out

- [x] ~~URL / folder / editor launching~~ - `Misc/ShellLauncher.cs`. WPF shells out to `explorer.exe`
      and `cmd /c code`, and calls `Process.Start( url )` directly, which throws on .NET Core
      (`UseShellExecute` defaults to false, so the URL is treated as a filename - this was live in
      `ShowMacrosWiki` and the About tab). `OpenInVSCode` tries `code`/`codium`/`code-insiders`,
      wrapping in `cmd.exe` only on Windows where `code` is a `.cmd`, and falls back to the desktop's
      default handler. Macros tab gained the missing Open Modules Folder / Macro Commands Wiki /
      Open in external editor buttons.

## Missing extensions

- [ ] `AutologinExtension`
- [ ] `LogoutOnDisconnectedExtension`
- [ ] `DemiseSearch`
- [ ] `BoatMovementGump` (note: a `BoatMovementGump` exists in Shared, used by debug/gumps -
      verify which one)

## Debug window gaps

- [x] ~~Settings persistence~~ - `AssistantOptions.DebugWindowOptions` (a JObject in Assistant.json,
      same key and shape as WPF). `DebugWindow` deserializes every tab whose DataContext is an
      `ISettingProvider` on open and serializes them back on close; `DebugViewModel.Serialize`/
      `Deserialize` were empty stubs before (and `Serialize` had an inverted null guard that wrote an
      empty object). Unlike WPF it also calls `AssistantOptions.Save()` on close, because the UI is a
      child process that `Environment.Exit`s when the game goes away and so may never reach the
      normal shutdown save.
- [x] ~~Capture toggle~~ - the Main tab's `Running` was named inconsistently and defaulted to **on**,
      so packets accumulated into an unbounded list from the moment the window's view model was
      constructed. Renamed to `Enabled`, defaulted off (opt-in, as WPF), and joined by the
      `IncludeInternal` and `Direction` filters WPF has. All four capture handlers now share one
      `ShouldCapture` gate, which reads a `bool[256]` rather than doing a LINQ scan over 256 entries
      per packet on the network hot path.
- [ ] **Actions** tab (`DebugActionQueueControl`)
- [ ] **Keyboard** tab (`DebugKeyboardControl`)
- [ ] **Packets** tab (`DebugPacketsControl`) / packet queue debug (`DebugPacketQueueControl`)
      (the "Main" tab covers packets loosely; it now has WPF's enable/internal/direction filters, but
      not the separate export or queue views)
