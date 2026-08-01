# Entity Collection Viewer (ECV) TODO

Gap between this repo's Entity Collection Viewer and the WPF `ClassicAssist` tree. The Avalonia
version currently has only the bare browse/sort/refresh feature set; everything else on this page
is missing or diverged.

Old (WPF) source lives under `ClassicAssist/ClassicAssist/UI/Views/ECV/` and
`ClassicAssist/ClassicAssist/UI/ViewModels/EntityCollectionViewerViewModel.cs` (2035 lines). New
(Avalonia) source is `ClassicAssist.Avalonia/ClassicAssist.Shared/UI/ViewModels/EntityCollectionViewerViewModel.cs`
(278 lines) plus `ClassicAssist.Avalonia/ClassicAssist.Avalonia/Views/EntityCollectionViewer.axaml(.cs)`.

The Avalonia view model's own doc comment already says the filter editor, the organizer, the
queued move/loot actions and the settings window are not yet ported, and neither are the toolbar
commands that drive them. This doc fills in the specifics under each of those headings, plus a few
things that comment doesn't call out (context menu, locking, clipboard, hotkey wiring).

## Toolbar & window chrome

- [ ] **Filter toggle + panel** - old toolbar has a filter icon toggle that shows/hides the
      `EntityCollectionFilterControl` (`UI/Views/ECV/Filter/EntityCollectionFilterControl.xaml`).
      Not present in the Avalonia toolbar at all. See "Filter" section below for what it drives.
- [ ] **Organizer toggle + panel** - toggles `EntityCollectionViewerOrganizerControl.xaml`.
      Not present. See "Organizer panel" section below.
- [ ] **Always-on-top toggle** - `ToggleAlwaysOnTopCommand`
      (`EntityCollectionViewerViewModel.cs:365`, handler `ToggleAlwaysOnTop` around line 1336) sets
      `Options.AlwaysOnTop` and is persisted **per-ECV-window** via `EntityCollectionViewerOptions`.
      The Avalonia window (`EntityCollectionViewer.axaml.cs:37`) instead reads the **global** app
      option `Options.CurrentOptions.AlwaysOnTop` once at construction, with no toggle and no
      independent per-window state - every ECV window just follows whatever the main app window's
      always-on-top setting is.
- [ ] **Sort-style submenu with icons, "None" style, and toggle-to-None** -
      `ChangeSortStyleCommand` (line 222, handler `ChangeSortStyle` at line 1478) does
      `SortStyle = SortStyle == val ? EntityCollectionSortStyle.None : val` - clicking the active
      sort style again turns sorting off. The old `EntityCollectionSortStyle` enum
      (`UI/Models/EntityCollectionSortStyle.cs`) has a `None` member plus `Weight`, neither of which
      exist on the Avalonia enum (`ClassicAssist.Shared/UI/Models/EntityCollectionSortStyle.cs`,
      currently `Name, Serial, Hue, ID, Quantity`). The Avalonia UI is a plain `ComboBox` bound
      straight to `SortStyle` - no toggle-off, no icons, no "None", no "Weight".
- [ ] **Open All Containers** - `OpenAllContainersCommand` (line 306, handler at line 1247) walks
      the collection and sends `UseObject`/drag-drop-style packets to open every nested container,
      respecting `Options.OpenContainersIgnore` and `Options.OpenContainersOnlyKnownContainers`.
      No equivalent toolbar button or command in Avalonia.
- [ ] **Combine Stacks** - `CombineStacksCommand` (line 236, handler `CombineStacks` at line 858)
      merges same-name/same-properties item stacks together via queued drag-drops, skipping
      anything matched by `Options.CombineStacksIgnore`. Not ported.
- [ ] **Replace Name** - `ReplaceNameCommand` (line 318, handler `ReplaceName` at line 799) lets the
      user rename an item via a captured in-game "look" journal label (renames feed
      `_nameOverrides`, which the Avalonia `EntityCollectionData` extension already has the
      *storage* for - see Data model gaps - but nothing populates it from a look request). Not
      ported.
- [ ] **Target Container** - `TargetContainerCommand` (line 361, handler `TargetContainer` at line
      959) retargets the whole viewer at a newly-targeted container. Not ported.
- [ ] **Autoloot Container** - `AutolootContainerCommand` (line 220, handler `AutolootContainer` at
      line 446) runs the collection through the autoloot filter/predicate system and loots matches.
      Not ported.
- [ ] **Hide Locked Items toggle** - `ToggleHideLockedItemsCommand` (line 369, handler at line 1750)
      flips `Options.HideLockedItems`, persists it, and applies a `CollectionView` filter
      (`ApplyHideLockedItemsFilter`, line 1803) that hides any `EntityCollectionData` with
      `IsLocked == true`. Depends on the Locking feature below, none of which exists in Avalonia.
- [ ] **Enable Hotkeys toggle** - `ToggleEnableHotkeysCommand` (line 372, handler at line 1764) and
      `HotkeyActionCommand` (line 375, handler `HotkeyAction` at line 1776) let the *ECV window
      itself* respond to single-key hotkeys (B/C/K/G/D per the context menu's `InputGestureText`)
      while it has focus, gated on `Options.EnableHotkeys`. Not ported. (Distinct from the
      already-ported global "Grid Container Viewer" hotkey command that *opens* an ECV window -
      see Hotkeys section.)
- [ ] **Configure button** - `ConfigureCommand` (line 237, handler `Configure` at line 752) opens
      `EntityCollectionViewerSettingsWindow`. Not ported; see Settings/Options persistence.
- [ ] **`CustomToolbarActions` ItemsControl** - renders one button per registered
      `IEntityCollectionViewerAction` (`CustomToolbarActions`, declared line 282, populated line
      196 from `EntityCollectionViewerExtensions.ToolbarActions`). Not ported; see Extensibility.

## Context menu

Old: `UI/Views/ECV/ContextMenu.xaml` (a `ContextMenu` resource named `ContextMenu`, merged in via
`ListStyles.xaml`, applied to each item's `StackPanel`). Avalonia has **no context menu at all** on
the ECV item template (`EntityCollectionViewer.axaml`) - confirmed by grepping the whole Avalonia
tree for `ContextMenu` near anything ECV-related; nothing partial exists. Every entry below is
missing:

- [ ] **Use item** - `ContextUseItemCommand` (`ContextMenu.xaml:12-13`; VM line 274, handler
      `ContextUseItem` at line 1608).
- [ ] **Move to backpack** (gesture `B`) - `ContextMoveToBackpackCommand` (xaml:14-15; VM line 245,
      handler `ContextMoveToBackpack` at line 1723).
- [ ] **Move to container** (gesture `C`) - `ContextMoveToContainerCommand` (xaml:16-17; VM line
      251, handler `ContextMoveToContainer` at line 1622) - prompts for a target if none given.
- [ ] **Move to bank** (gesture `K`) - `ContextMoveToBankCommand` (xaml:18-19; VM line 248, handler
      `ContextMoveToBank` at line 761).
- [ ] **Move to ground** (gesture `G`) - `ContextMoveToGroundCommand` (xaml:20-21; VM line 254,
      handler `ContextMoveToGround` at line 513).
- [ ] **Move to set** submenu - `ContextMoveToSetCommand`, items sourced from
      `Options.ContainerSets` (xaml:22-43; VM line 257, handler `ContextMoveToSet` at line 591).
      Depends on `EntityCollectionViewerOptions.ContainerSets` and the Settings dialog's
      "Container Sets" tab - see Settings/Options persistence.
- [ ] **Open container** - `ContextOpenContainerCommand` (xaml:44-45; VM line 259, handler
      `ContextOpenContainer` at line 735).
- [ ] **Drop to ground** (gesture `D`) - `ContextDropToGroundCommand` (xaml:46-47; VM line 263,
      handler `ContextDropToGround` at line 543).
- [ ] **Lock item / Unlock item** - two mutually-exclusive `MenuItem`s bound to the same
      `ContextToggleLockCommand`, visibility toggled by `SelectedItemsAllLocked` (xaml:49-76; VM
      line 271, handlers `ContextToggleLock`/`ApplyLockState` around lines 1184-1218). See Locking.
- [ ] **Context menu request** - `ContextContextMenuRequestCommand` sends the server's native
      right-click context menu packet (xaml:78-79; VM line 239, handler `ContextMenuRequest` at
      line 1855). The underlying `ContextMenuRequest`/`ContextMenuClick` packets and the
      `ContextMenu()`/`WaitForContext()` macro commands already exist in
      `ClassicAssist.Shared/UO/Network/Packets/` and `ActionCommands.cs` - only the ECV wiring is
      missing.
- [ ] **Equip Item** - `EquipItemCommand` (xaml:80-81; VM line 298, handler `EquipItem` at line
      1369).
- [ ] **Hide** - `HideItemCommand`, removes the item from the viewer's own list without touching
      the server (xaml:82-83; VM line 300, handler `HideItem` at line 1176).
- [ ] **Custom Actions** submenu - items sourced from `CustomContextActions` (a
      `ObservableCollection<KeyValuePair<string, Action<Item>>>`, declared line 280), commands via
      `ContextCustomActionCommand` (xaml:84-105; VM line 242, handler `ContextCustomAction` at line
      492). Note this is a *different, older* extensibility mechanism than
      `IEntityCollectionViewerAction`/`EntityCollectionViewerExtensions` used for toolbar actions -
      populated by direct `CustomContextActions.Add(...)` calls (line 147), not a registry. See
      Extensibility.
- [ ] **Target** - `ContextTargetCommand` (xaml:107-108; VM line 266, handler `ContextTarget` at
      line 1100).
- [ ] **Target Owner** - `ContextTargetOwnerCommand`, targets the mobile/container the selected
      item is inside of rather than the item itself (xaml:109-110; VM line 268, handler
      `ContextTargetOwner` at line 423).
- [ ] **Copy to clipboard** - not on the context menu XAML but bound elsewhere in the old UI;
      `CopyToClipboardCommand` (VM line 394, handler `CopyToClipboard` at line 396). Worth folding
      into this section's port since it has no other home.
- [ ] **Double-click drills into a container OR opens the Object Inspector** - this part *is*
      ported (`ItemDoubleClickCommand` exists both sides and behaves the same way); noted here only
      so it isn't mistaken for a missing context-menu-adjacent feature.

## Filter

Old: `UI/Views/ECV/Filter/EntityCollectionFilterControl.xaml(.cs)`,
`EntityCollectionFilterViewModel.cs`, `Filter/Models/{EntityCollectionFilterEntry,
EntityCollectionFilterGroup, EntityCollectionFilterItem, GroupItem}.cs`. Entirely absent from
Avalonia - confirmed by grep, no filter-related file or type exists anywhere in the tree.

- [ ] **Boolean-tree filter groups** - a filter "profile" (`EntityCollectionFilterEntry`) holds a
      collection of `EntityCollectionFilterGroup`s, each of which has its own `Items` (leaf
      conditions) plus a nested `Children` collection of sub-groups and a `BooleanOperation`
      (`And`/`Or`/`Not`) combining them (`Filter/Models/EntityCollectionFilterGroup.cs:22-62`).
      Evaluation is `EvaluateGroup` in the main view model (line 1442), applied through
      `ApplyFilters`/`ApplyFiltersCommand` (line 218, handler at line 1392).
- [ ] **Filter conditions reuse the Autoloot constraint system** - each leaf
      `EntityCollectionFilterItem` (`Filter/Models/EntityCollectionFilterItem.cs`) has a
      `PropertyEntry` `Constraint`, an `AutolootOperator` (`Equal`/`NotEqual`/`GreaterThan`/
      `LessThan`/`NotPresent`), a `Value`, `Additional` (string), and `Values` (int list).
      `EntityCollectionFilterViewModel`'s constructor (`Filter/EntityCollectionFilterViewModel.cs:71-202`)
      registers built-in constraints beyond what `AutolootManager.LoadProperties` already supplies:
      **Name** (substring match over item properties or name), **TileFlags**, **Distance**,
      **Organizer Match** (does the item match an entry in an existing Organizer profile), and
      **Is Multi** (multi/house-addon check via `ArtDataID == 2`) - plus whatever
      `AutolootManager.LoadAssemblies` contributes from loaded plugin assemblies. So "what can you
      filter on" is effectively the entire autoloot property/predicate surface, not a fixed list.
- [ ] **Filter profiles persist to `FilterProfiles.json`** (`LoadFilterProfiles`/
      `SaveFilterProfiles`, lines 325-441) - add/remove/rename profiles, switch between them
      (`AddProfileCommand`, `RemoveProfileCommand`, `ChangeProfileCommand`), each independently
      re-applied live if a filter is currently active (`SetActiveProfile`, line 280).
  This whole subsystem needs a from-scratch port: model classes, the constraint list, the
  group/profile editor UI, and the JSON persistence. There is no partial Avalonia analog to build
  on other than the pre-existing Autoloot `PropertyEntry`/`AutolootOperator` types it depends on.

## Organizer panel

Old: `UI/Views/ECV/EntityCollectionViewerOrganizerControl.xaml(.cs)` +
`EntityCollectionViewerOrganizerViewModel.cs`. What it actually does: a small toolbar-adjacent
strip with an "Organizer:" dropdown (bound to `OrganizerManager.GetInstance().Items` - the same
saved Organizer *presets* configured on the main app's Agents > Organizer tab), a "Destination ID:"
dropdown (aliases `backpack`/`bank` plus any ad-hoc target added via the crosshair button, which
calls `Commands.GetTargetInfoAsync()`), and a "Play" button. Clicking Play queues
`_manager.Organize(entry, Collection, destinationContainer: serial, ...)` - i.e. it runs an
*existing* Organizer preset's item-matching rules against **this ECV window's item collection**
specifically (rather than the preset's own configured source container), moving matches to the
chosen destination. It shares the same `QueueAction`/cancel-button status-row mechanism as
everything else (`EntityCollectionViewerOrganizerViewModel.cs:104-114`).

- [ ] **Not ported at all.** However, the backing infrastructure already exists on the Avalonia
      side and does not need to be built: `OrganizerManager`, `OrganizerEntry`, `OrganizerItem`
      (`ClassicAssist.Shared/Data/Organizer/`) are already fully ported and in use by
      `Views/Agents/OrganizerTabControl.axaml` + `OrganizerTabViewModel.cs`. What's missing is
      specifically the ECV-embedded panel: the toggle, the two dropdowns, the target button, and
      wiring `Organize(entry, Collection, destinationContainer, token)` through the (also missing)
      queued-action mechanism described below.

## Settings/Options persistence

Old: `Data/Misc/EntityCollectionViewerOptions.cs` (persisted to `EntityCollectionViewerOptions.json`
in the startup dir via `LoadOptions`/`SaveOptions`, VM lines 703-722), edited through
`UI/Views/ECV/EntityCollectionViewerSettingsWindow.xaml` +
`EntityCollectionViewerSettingsViewModel.cs`. Avalonia's view model has **no `Options` property and
no persistence of any kind** - confirmed by grep, no `EntityCollectionViewerOptions` or
`EntityCollectionViewerSettings*` type exists anywhere in the Avalonia tree. `ShowChildItems`,
`ShowProperties` and `SortStyle` are plain in-memory properties that reset to their defaults every
time a new ECV window opens.

Every persisted field on `EntityCollectionViewerOptions` and where it's edited:

- [ ] `AlwaysOnTop` (bool) - toolbar toggle (see Toolbar section).
- [ ] `ShowChildItems` (bool) - **is** ported as a live property/toggle, but not persisted across
      window opens the way `Options.ShowChildItems` is old-side.
- [ ] `HideLockedItems` (bool) - toolbar toggle; drives the CollectionView filter. Depends on
      Locking.
- [ ] `EnableHotkeys` (bool) - toolbar toggle; gates `HotkeyActionCommand`.
- [ ] `SortStyle` (`EntityCollectionSortStyle`) - **is** ported as a live property, but old-side
      persists the last-used sort style across window opens; Avalonia always starts at `ID`.
- [ ] `LockedItems` (`ObservableCollection<int>`, serial numbers) - the actual lock-state store; see
      Locking.
- [ ] `ContainerSets` (`ObservableCollection<ContainerSet>`, each a `Name` + `ObservableCollection<int>`
      of serials) - edited via the Settings window's "Container Sets" tab
      (`EntityCollectionViewerSettingsWindow.xaml:56-64`,
      `UI/Views/ECV/Settings/ContainerSetsSettingsControl.xaml`); consumed by the context menu's
      "Move to set" submenu.
- [ ] `CombineStacksIgnore` / `OpenContainersIgnore` (both
      `ObservableCollection<CombineStacksOpenContainersIgnoreEntry>`, each an item `ID` + `Cliloc` +
      `Hue` to exclude) - edited via the Settings window's "Combine stacks" and "Open All
      Containers" group boxes
      (`EntityCollectionViewerSettingsWindow.xaml:38-54`,
      `Settings/CombineStacksSettingsControl.xaml`, `Settings/OpenContainersSettingsControl.xaml`).
      Feed `CombineStacksCommand` and `OpenAllContainersCommand` respectively.
- [ ] `OpenContainersOnlyKnownContainers` (bool) - checkbox in the Settings window's "Open All
      Containers" group (`EntityCollectionViewerSettingsWindow.xaml:48-50`); only auto-opens
      containers whose gump ID the client already knows about.
- [ ] `Assemblies` (`ObservableCollection<Assembly>`) - loaded plugin assemblies contributing
      custom filter constraints/actions; persisted as file paths, reloaded via `Assembly.LoadFile`
      on deserialize (`EntityCollectionViewerOptions.cs:163-180`). Windows-assembly-loading concept
      that likely needs rethinking for the cross-platform port rather than a direct copy.
- [ ] **Settings window itself** - two-column dialog: left column has "Combine stacks" and "Open
      All Containers" group boxes, right column has "Container Sets"
      (`EntityCollectionViewerSettingsWindow.xaml:27-65`), OK/Cancel at the bottom (OK is a no-op
      command - the bound `Options` object is mutated in place, so OK/Cancel really only differ by
      whether the window closes; there's no save/rollback distinction to preserve).

## Extensibility (custom toolbar/context actions)

Old: `UI/Views/ECV/Extensibility/{IEntityCollectionViewerAction, IEntityCollectionViewerContext,
EntityCollectionViewerActionContext, EntityCollectionViewerActionViewModel,
EntityCollectionViewerExtensions}.cs`. Not present anywhere in Avalonia (grep confirms).

Two distinct, unrelated mechanisms exist old-side and both need a home:

- [ ] **Toolbar actions (`IEntityCollectionViewerAction`)** - a real static registry
      (`EntityCollectionViewerExtensions.RegisterToolbarAction`/`UnregisterToolbarAction`,
      `Extensibility/EntityCollectionViewerExtensions.cs:53-78`). A plugin author implements
      `IEntityCollectionViewerAction` (`Name`, `Icon`, `CanExecute(IEntityCollectionViewerContext)`,
      `Execute(IEntityCollectionViewerContext)`) and calls `RegisterToolbarAction` from their
      assembly's static `Initialize()`. Each ECV instance reads the registry when opened, wraps
      each entry in an `EntityCollectionViewerActionViewModel` (binds `Name`/`Icon`, builds an
      `ExecuteCommand` that re-queries `CanExecute` as selection changes) and renders them via the
      toolbar's `CustomToolbarActions` `ItemsControl`. `IEntityCollectionViewerContext` (`Collection`,
      `SelectedItems`, `ShowProperties`, `Refresh()`, `EnqueueAction(Func<CancellationToken,bool>,
      string)`) is the sandboxed view of the viewer state/services a plugin action gets - built
      per-invocation by the private `EntityCollectionViewerActionContext` implementation.
- [ ] **Context actions (`CustomContextActions`)** - a much thinner, non-registry mechanism: an
      `ObservableCollection<KeyValuePair<string, Action<Item>>>` directly populated by
      `CustomContextActions.Add(...)` calls (VM line 147) rather than through a public API like the
      toolbar one. Rendered as the context menu's "Custom Actions" submenu
      (`ContextMenu.xaml:84-105`). Worth deciding during the port whether to unify this with the
      toolbar registry or keep it as its own (undocumented, ad-hoc) extension point - old-side
      these are genuinely two different systems, not one shared abstraction.

## Queued actions & cancellation

Old: `QueueActions` (`ObservableCollection<QueueAction>`, declared line 314),
`ThreadQueue` (`ThreadPriorityQueue<QueueAction>`, declared line 363, constructed line 193),
`QueueAction` class (bottom of the file, lines 2013-2034) with a `Status` string, a
`CancellationTokenSource`, and its own `CancelCommand`. Almost every long-running command above
(move-to-*, combine stacks, open all containers, autoloot container, organizer's Play, context menu
request, use item) goes through `EnqueueAction(Func<QueueAction, Task<bool>> action, string
message)` (line 1875) rather than running inline. `QueueActions_CollectionChanged` (line 771) feeds
new entries into `ThreadQueue`, which serializes them through `ProcessQueue` (line 789) at
`QueuePriority.Low`; each queue entry renders as a status row at the bottom of the ECV window with
live status text and a cancel button, and removes itself from `QueueActions` on completion (line
796).

- [ ] **None of this exists in Avalonia.** There's no bottom status-row list, no
      `ThreadPriorityQueue` use in the ECV view model, and no `QueueAction` type. This is also *why*
      none of the context-menu commands have anywhere meaningful to run through yet even once the
      commands themselves are ported - most of them are written old-side assuming they can enqueue
      onto this and return immediately, surfacing progress/cancellation through the status row
      rather than blocking the UI thread.

## Locking

Old: `Options.LockedItems` (`ObservableCollection<int>` of serials, on
`EntityCollectionViewerOptions`), `EntityCollectionData.IsLocked` (bool, settable,
`ClassicAssist/UI/ViewModels/EntityCollectionData.cs:33-37`), `SelectedItemsAllLocked` (computed
property gating which of Lock/Unlock shows in the context menu), `ApplyLockState` (line 1184,
toggles lock state for the current selection and calls `SaveOptions`), `ContextToggleLockCommand`
(line 271, handler `ContextToggleLock` at line 1197), plus the padlock overlay icon drawn on locked
tiles (`ListStyles.xaml:24-27`, `LockIcon` `Image` bound to
`EntityCollectionViewerViewModel.PadlockIcon`, shown via a `DataTrigger` on `IsLocked` at
`ListStyles.xaml:39-41`). A locked item is skipped by move commands (e.g.
`ContextMoveToContainer` filters `SelectedItems.Where(i => !i.IsLocked)`, line 1624) and can be
hidden entirely via Hide Locked Items (see Toolbar section).

- [ ] **Nothing here is ported.** `ClassicAssist.Avalonia`'s `EntityCollectionData` (
      `ClassicAssist.Shared/UI/ViewModels/EntityCollectionData.cs`) has no `IsLocked` property at
      all, so there's no per-item lock state to render a padlock over, filter on, or skip during a
      move - all of which are currently moot anyway since none of the move commands exist yet
      either. Needs `Options.LockedItems` persistence (part of Settings/Options above) plus the
      `IsLocked` flag, the padlock overlay, `SelectedItemsAllLocked`, and the toggle-lock context
      command.

## Sorting

- [x] **Sort by enum value is ported** - `SortStyle` property (Avalonia VM line 134) triggers
      `Rebuild()` on set; `GetSorter()` (line 191) maps `Name`/`Serial`/`Hue`/`Quantity` to the same
      `NameThenSerialComparer`/`SerialComparer`/`HueThenAmountComparer`/`QuantityThenSerialComparer`
      types the old code uses, defaulting to `IDThenSerialComparer` - this matches old's
      `GetComparer` (line 1500) case-for-case for the styles that exist on both enums.
- [ ] **Enum is missing two members**: `None` (turns sorting off entirely - old's comparer switch
      returns `null` for it, line 1504-1506) and `Weight` (`WeightComparer`, referenced at old line
      1516). See Toolbar section for the toggle-to-None UI behavior that depends on `None` existing.
- [ ] **No persistence** - old persists the last-chosen `SortStyle` into `Options.SortStyle` inside
      `ChangeSortStyle` (line 1489-1490); Avalonia's `SortStyle` always starts at `ID` (Avalonia VM
      line 62) regardless of what was last selected.

## Hotkeys

- [x] **`EntityCollectionViewerHotkey` (the "Grid Container Viewer" global hotkey that opens an
      ECV window) is a faithful, complete port.** Compared
      `ClassicAssist/ClassicAssist/Data/Hotkeys/Commands/EntityCollectionViewerHotkey.cs` against
      `ClassicAssist.Avalonia/ClassicAssist.Shared/Data/Hotkeys/Commands/EntityCollectionViewerHotkey.cs`
      line by line: same target-container-then-resolve-entity logic, same
      item/mobile/fallback-to-bare-serial branching, same "wait for container contents if never
      opened" call. The only differences are mechanical, not behavioral: Avalonia routes window
      creation through `Engine.UIInvoker?.Invoke("EntityCollectionViewer", ...)` instead of `new
      EntityCollectionViewer { DataContext = ... }; window.Show()`, which is correct for this
      repo's out-of-process architecture (see the `classicassist-linux-out-of-process-architecture`
      note) and not a gap.
- [ ] **In-window hotkeys are a separate, unported feature** - see "Enable Hotkeys toggle" under
      Toolbar & window chrome. `Options.EnableHotkeys` + `HotkeyActionCommand` let the ECV window
      itself respond to B/C/K/G/D while focused; this has nothing to do with the global hotkey
      system and isn't touched by the comparison above.

## Data model gaps

- [ ] **`EntityCollectionData` is missing `IsLocked`** (see Locking) and is missing
      **`NotifyPropertiesUpdated()`** - old-side (`ClassicAssist/UI/ViewModels/EntityCollectionData.cs:98-108`)
      re-raises `PropertyChanged` for `Name`/`FullName`/`Bitmap` when an OPL packet updates the
      underlying entity's name/properties/hue *after* the row was already created and displayed.
      Avalonia's `EntityCollectionData`
      (`ClassicAssist.Shared/UI/ViewModels/EntityCollectionData.cs`) is a **plain class with no
      `INotifyPropertyChanged` at all** - old-side it extends `SetPropertyNotifyChanged`. Practical
      effect: if an item's server-side properties/name/hue arrive after it's already rendered in
      the grid, the Avalonia tile has no way to update itself short of a full `Rebuild()`. (Old's
      `OnItemPropertiesUpdated`, VM line 984, is exactly the wiring that calls
      `NotifyPropertiesUpdated()` on the affected row when that happens - also unported, since it
      has nothing to call.)
- [ ] **`EntityCollectionData.FullName`** - present on both sides with matching logic
      (join item `Properties` text with `\r\n`, fall back to `Name`); not a gap, listed only to
      confirm it was checked.
- [ ] **`EntityCollectionSortStyle` enum missing `None` and `Weight`** - see Sorting.
- [x] **Everything else on `EntityCollectionData` matches**: `Entity`, `IsCoin` (same three coin
      graphic IDs), `Pixmap`/`Bitmap` (same stack-size-dependent coin-graphic bump, same mount
      layer → statue-graphic substitution via `MountIDEntries`), `Name`/`GetName` (same mount-name
      fallback through tile data) are all semantically identical, just renamed
      `Bitmap`→`Pixmap`/`ImageSource`→`Pixmap` type for the out-of-process-safe image representation
      (already-known, intentional divergence per the class's own doc comment).

## Already ported / working

Not gaps - confirmed present and functioning in the Avalonia view model
(`ClassicAssist.Shared/UI/ViewModels/EntityCollectionViewerViewModel.cs`):

- Browsing a collection and constructing it from an `ItemCollection`
- Drilling into a container via double-click (opens a new ECV window, or the Object Inspector for
  a non-container entity)
- Live tracking of the source collection (`OnCollectionChanged` → `Rebuild()`), with `Cleanup()`
  correctly unsubscribing so a closed window doesn't keep rebuilding itself forever
- `ShowChildItems` toggle (recursively flattens container contents) - not persisted, but the toggle
  itself works
- `ShowProperties` toggle (full property tooltip/label vs. just the name) - works, not persisted
- Basic sort by enum value for the styles that exist on both sides (Name/Serial/Hue/ID/Quantity) -
  see Sorting for what's missing
- Refresh (including the `_customRefresh` hook for a caller-supplied re-fetch)
- Status label (`{0} items, {1} selected, {2} total amount}`) tracking selection
- The `Grid Container Viewer` global hotkey that opens an ECV window (full port, see Hotkeys)
