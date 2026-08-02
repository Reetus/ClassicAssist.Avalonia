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
- [ ] **Filter Configure windows** - `ClilocFilter.Configure()` and
      `RepeatedMessagesFilter.Configure()` are commented out in
      `ClassicAssist.Shared/Data/Filters/`; the General tab's filter rows therefore have no
      configure button. WPF has `ClilocFilterConfigureWindow`,
      `RepeatedMessagesFilterConfigureWindow`, `SeasonFilterConfigureWindow`,
      `SoundFilterConfigureWindow`, `ItemIDFilterConfigureWindow` under `UI/Views/Filters/`.
- [ ] **Autologin configure / status** - the General tab has no Autologin section at all
      (`AutologinConfigureCommand`, `AutologinStatusWindow` absent).
- [ ] **Backup settings** - General tab "Backup Settings" button absent
      (`BackupSettingsCommand`, `BackupWindow`/`BackupSettingsWindow` absent).

## Missing subsystems (VM + view both absent)

- [ ] **Trap Pouches** - `TrapPouchManager`/`TrapPouchEntry` + `TrapPouchTabControl` agent tab.
      Blocks macro commands `ClearTrapPouch`/`SetTrapPouch`/`UseTrapPouch` and hotkeys
      `Use Trap Pouch`/`Clear Trap Pouches`.
- [ ] **Screenshot** - `ScreenshotManager` + `ScreenshotTabControl` agent tab
      (screenshots of the client window, mobile filters). Also blocks the `Take Snapshot` hotkey.
- [ ] **Name Overrides** - `NameOverrideManager` + `NameOverrideTabControl` agent tab.
- [ ] **Backups** - `Data/Backup/{GoogleDrive,Mega,OneDrive,WebDAV}` + backup/restore UI.
- [ ] **Chat window** - `ChatManager` is ported but there is no `ChatWindow`; the
      `Show Chat Window` hotkey and chat window `Options.ChatWindow*` fields are absent.
- [ ] **GIF recorder** - `GIFRecorderWindow` + `Show GIF Capture` hotkey. Screen capture of the
      client is a plugin-side capability this repo does not have.
- [ ] **Public Macros browser** - the "Public Macros" tab (`MacroBrowserControl`,
      `MacroBrowserViewModel`, classicassistant.net API) is absent from the main window.
- [ ] **Macro debugger (DAP)** - `DebugAdapter/*`, `MacrosDebuggerControl`, breakpoints,
      variable inspector, and the Debug Adapter toggle in Options are all absent.

## Missing agent features (tab exists, feature dropped)

- [ ] **Organizer** - per-entry Source/Destination container display and Set/Clear Source &
      Destination commands (only a combined target-based "Set Containers" exists); `Return_Excess`.
- [ ] **Dress** - no "Stop" button (stop exists only via `DressManager.Stop()` macro/hotkey).
- [ ] **Scavenger** - `CheckWeight`/`MinWeightAvailable`, per-entry `Priority`
      (no `ScavengerPriority` enum), Cliloc filter (`ScavengerClilocFilterEntry` +
      `ScavengerClilocFilterWindow`).
- [ ] **Vendor Sell** - `ContainerSerial`, `SetContainer`/`ResetContainer`, `ID from target`,
      `Match Any ID`.
- [x] ~~**Autoloot**~~ - groups/folders (`AutolootGroup`, `IDraggable` tree infra, New/Remove/
      Move-to-group commands), per-entry `Priority` (`AutolootPriority` enum + `Group`/`Priority` on
      `AutolootEntry`, serialized and honored in `OnCorpseEvent`/debug ordering), `LootHumanoids`
      (corpse-graphic check), `RequeueFailedItems` (new `DragDropOptions` requeue path in
      `ActionPacketQueue`), CSV import (`CSVImportWindow` + `CSVImportViewModel` + minimal
      `CsvReader`; `PropertyEntry.ShortName` and WPF `Properties.json` synced for column matching).

## Missing macro commands (15)

From `MACRO_COMMANDS_TODO.md` - everything not yet checked:

- [ ] `GetPlayerAlias`, `PromptMacroAlias`, `PromptPlayerAlias`, `SetPlayerAlias`,
      `UnsetPlayerAlias` (no player-alias store)
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

## Missing hotkeys (8)

From `HOTKEYS_TODO.md`:

- [ ] `Take Snapshot` (`SnapshotCommand`)
- [ ] `Greater Heal / Cure Self`, `Mini Heal / Cure Self` (need configurable-hotkey
      infrastructure: `HotkeyConfigurationAttribute`, `HotkeyEntry.Configurable`, configure
      command in `HotkeysTabViewModel`, `CureType` enum)
- [ ] `Use Trap Pouch`, `Clear Trap Pouches` (needs `TrapPouchManager`)
- [ ] `Show Chat Window`, `Show GIF Capture` (need the windows above)

## Missing Options

- [ ] `Options` class: `Autologin*`, `ChatWindow*`, `DisableHotkeysLoad`, `DragDelay/MS`,
      `HotkeysStatusGump*`, `LimitHotkeyTrigger/MS`, `LogoutDisconnectedPrompt`, `MacrosGump*`
      (text color/height/width/transparent), `DebugAdapterEnabled/Port`, `SelectedTabIndex*`,
      `SetUOTitle`, `SlowHandlerThreshold`, `SysTray`
- [ ] Options tab UI: Debug Adapter toggle, "Disable hotkeys on profile load", "Hotkeys Status
      Gump", "Limit Hotkey retrigger", "Show player/shard name in CUO title", "Use Cliloc
      language from ClassicUO"
- [ ] General tab UI: filter Configure buttons, Minimize to tray, Drag delay, Saved Passwords,
      Autologin section, Backup Settings button

## Missing filters

- [ ] `SoundFilter`, `ItemIDFilter` (and their configure windows). Note: `BardsMusicFilter`
      (from upstream `develop`) is present here instead - not a gap, just a different set.
- [ ] `ClilocFilter`/`RepeatedMessagesFilter` Configure dialogs (stubbed out - see Dead buttons).

## ECV still missing (details in ECV_TODO.md)

- [ ] Organizer panel (toggle, two dropdowns, target button, Play via queued-action)
- [ ] Replace Name, Target Container, Autoloot Container
- [ ] "Move to set" context submenu
- [ ] Padlock overlay icon on locked tiles (cosmetic)
- [ ] `CustomToolbarActions` / `IEntityCollectionViewerAction` extensibility registry
- [ ] Boolean-tree filter groups (flat AND-only profiles instead - deliberate)
- [ ] `EntityCollectionData.NotifyPropertiesUpdated()` / live OPL update of rendered rows
- [ ] `EntityCollectionViewerOptions.Assemblies` round-trip (deliberately skipped)

## Missing extensions

- [ ] `AutologinExtension`
- [ ] `LogoutOnDisconnectedExtension`
- [ ] `DemiseSearch`
- [ ] `BoatMovementGump` (note: a `BoatMovementGump` exists in Shared, used by debug/gumps -
      verify which one)

## Debug window gaps

- [ ] **Actions** tab (`DebugActionQueueControl`)
- [ ] **Keyboard** tab (`DebugKeyboardControl`)
- [ ] **Packets** tab (`DebugPacketsControl`) / packet queue debug (`DebugPacketQueueControl`)
      (a new "Main" tab covers packets loosely)
