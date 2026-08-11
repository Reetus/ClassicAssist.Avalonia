# Entity Collection Viewer (ECV) TODO

Gap between this repo's Entity Collection Viewer and the WPF `ClassicAssist` tree. Originally this
doc described a bare browse/sort/refresh port with everything else missing; several passes since
have closed most of that gap (see the `[x]` items throughout) - what's left is mainly the Organizer
panel, the Extensibility registry, and the boolean-tree filter groups, each called out below with
why they're still out.

Old (WPF) source lives under `ClassicAssist/ClassicAssist/UI/Views/ECV/` and
`ClassicAssist/ClassicAssist/UI/ViewModels/EntityCollectionViewerViewModel.cs` (2035 lines). New
(Avalonia) source is `ClassicAssist.Avalonia/ClassicAssist.Shared/UI/ViewModels/EntityCollectionViewerViewModel.cs`
plus `ClassicAssist.Avalonia/ClassicAssist.Avalonia/Views/EntityCollectionViewer.axaml(.cs)` and
`EntityCollectionViewerSettingsWindow.axaml(.cs)`.

## Toolbar & window chrome

- [x] **Filter toggle + panel** - ported as a right-sized MVP rather than a line-for-line port: a
      `FilterIcon` toggle button shows/hides a `DataGrid` of flat, AND-only conditions (Property /
      Operator / Value rows), backed by `EntityCollectionViewerViewModel.FilterConditions`
      (`ObservableCollection<AutolootConstraintEntry>`) and `Constraints`
      (`ObservableCollection<PropertyEntry>`, loaded from `Data/Properties.json` +
      `Properties.Custom.json` exactly like `AutolootViewModel` does). Evaluation reuses
      `AutolootHelpers.ConstraintsToPredicates` directly - no parallel filter-predicate system was
      built. **Deliberately not ported**: the boolean-tree groups (`And`/`Or`/`Not`, nested
      sub-groups), the ECV-specific extra constraints (Name substring, TileFlags, Distance, Organizer
      Match, Is Multi), and profile persistence (`FilterProfiles.json`, add/remove/rename/switch) -
      see the Filter section below, which still describes the full old-side shape as the reference
      for if/when that's wanted.
- [ ] **Organizer toggle + panel** - toggles `EntityCollectionViewerOrganizerControl.xaml`.
      Not present. See "Organizer panel" section below. (Explicitly deferred again when this pass's
      other items were scoped.)
- [x] **Always-on-top toggle** - `Options.AlwaysOnTop`, `Window.Topmost` bound to it directly
      (`Topmost="{Binding Options.AlwaysOnTop}"`), no separate toggle command - the `PinIcon`
      `ToggleButton`'s `IsChecked` two-way binding is the only write path (see the Show Child Items
      double-toggle bug this pattern was chosen specifically to avoid: a `ToggleButton` with both a
      two-way `IsChecked` bind *and* a `Command` that also flips the same property fires the flip
      twice per click). Persisted now via `EntityCollectionViewerOptions.json` - see Settings/Options
      persistence, which supersedes this entry's earlier "session-only" state.
- [x] **Sort-style submenu with icons, "None" style, and toggle-to-None** - the Avalonia
      `EntityCollectionSortStyle` enum now has `None` and `Weight`
      (`ClassicAssist.Shared/UI/Models/EntityCollectionSortStyle.cs`), matching old exactly. The
      toolbar's plain `ComboBox` was replaced with an icon-triggered `Menu`/`MenuItem` (`SortIcon`
      header, `ToggleType="CheckBox"` sub-items via a new `EnumMatchToBooleanConverter`), and
      `ChangeSortStyleCommand` reproduces `SortStyle = SortStyle == val ? None : val`. `GetSorter()`
      returns `null` for `None` (Rebuild/`ToEntityCollectionData` now skip `.OrderBy` when the
      comparer is null) and a new `WeightThenSerialComparer`
      (`ClassicAssist.Shared/Misc/EntityComparers.cs`) for `Weight`, ported line-for-line from old
      including the `PaladinNecromancerClassTooltips` cliloc-weight branch.
- [x] **Open All Containers** - ported as a deliberately simpler single pass: sends `UseObject` to
      every currently-known container in `Collection` (one queued action, cancellable, status shows
      progress), then refreshes. Old-side's version chases newly-discovered nested containers as they
      stream in via a raw packet-wait per container (`PacketFilterInfo`/`Engine.PacketWaitEntries`,
      version-dependent gump-packet offset) and iterates until nothing new turns up; that recursive
      wait-and-discover loop was **not** ported - re-running the command (or turning on Show Child
      Items first) covers most of the same ground manually. Also not ported: `Options
      .OpenContainersIgnore`/`OpenContainersOnlyKnownContainers` (no ignore-list, no
      known-gump-ID-only filtering - everything flagged `TileFlags.Container` is opened).
- [x] **Combine Stacks** - ported faithfully (`CombineStacks()`, same destination/source stack
      selection loop, same `StackNamesMatch`/`GetNameMinusAmount` tooltip-name-matching helpers,
      same queued-and-cancellable shape). **Not ported**: `Options.CombineStacksIgnore` - there is no
      exclusion list, so nothing is excluded from combining.
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
- [x] **Hide Locked Items toggle** - `Options.HideLockedItems` ported as a live predicate applied
      inside `Rebuild()` (Avalonia has no `CollectionView`/`ICollectionView.Filter` to hook the way
      WPF does, so it's a `.Where(!IsLocked)` over the freshly-built `Entities` list instead). This
      surfaced a real gap while wiring it up: `Rebuild()` recreates every `EntityCollectionData` from
      scratch on each call, so `IsLocked` - previously set directly on those doomed instances - was
      silently lost on the next server update. First fixed with a session-only `HashSet<int>`, then
      superseded by the real `Options.LockedItems` once Settings/Options persistence landed - see
      Locking.
- [x] **Enable Hotkeys toggle** - `Options.EnableHotkeys` + `HotkeyActionCommand` ported, reproducing
      old's indirection: the B/C/K/G/D `KeyBinding`s in `EntityCollectionViewer.axaml` now go through
      `HotkeyActionCommand` (gated on `Options.EnableHotkeys`) instead of straight to the context
      commands, so turning hotkeys off doesn't also disable the same actions from the context menu.
      Ctrl+C stays bound directly to `CopyToClipboardCommand`, ungated, matching old. Persisted - see
      Settings/Options persistence. (Distinct from the already-ported global "Grid Container Viewer"
      hotkey command that *opens* an ECV window - see Hotkeys section.)
- [x] **Configure button** - `ConfigureCommand` opens `EntityCollectionViewerSettingsWindow` via
      `Engine.UIInvoker.InvokeDialog`; see Settings/Options persistence.
- [ ] **`CustomToolbarActions` ItemsControl** - renders one button per registered
      `IEntityCollectionViewerAction` (`CustomToolbarActions`, declared line 282, populated line
      196 from `EntityCollectionViewerExtensions.ToolbarActions`). Not ported; see Extensibility.

## Context menu

Old: `UI/Views/ECV/ContextMenu.xaml` (a `ContextMenu` resource named `ContextMenu`, merged in via
`ListStyles.xaml`, applied to each item's `StackPanel`). **Ported** to a plain
`<ListBox.ContextMenu>` in `EntityCollectionViewer.axaml` (Avalonia's `ContextMenu` inherits the
owning control's `DataContext` directly, so none of WPF's `BindingProxy` indirection is needed) plus
the corresponding commands/handlers on `EntityCollectionViewerViewModel`. Everything below is done
except where noted:

- [x] **Use item** - `ContextUseItemCommand`, sends `UseObject` for every selected item via
      `ActionPacketQueue.EnqueueActionPackets`.
- [x] **Move to backpack** - `ContextMoveToBackpackCommand`.
- [x] **Move to container** - `ContextMoveToContainerCommand` - prompts for a target via
      `Commands.GetTargetSerialAsync` if none given.
- [x] **Move to bank** - `ContextMoveToBankCommand`.
- [x] **Move to ground** - `ContextMoveToGroundCommand` - prompts for a drop location via
      `Commands.GetTargetInfoAsync`.
- [ ] **Move to set** submenu - still explicitly out of scope. `Options.ContainerSets` exists and is
      now editable (see Settings/Options persistence), but nothing reads it back out into a context
      menu submenu yet - building and persisting the sets was the ask, not this consumer.
- [x] **Open container** - `ContextOpenContainerCommand`.
- [x] **Drop to ground** - `ContextDropToGroundCommand`, probes the 8 tiles around the player for a
      free spot via `MapInfo.ItemCanFit` (`ClassicAssist.Shared/UO/Data/Map.cs`), same as old. Used to
      just drop at the player's own feet instead, which a mobile occupies and gets rejected server-side
      - fixed.
- [x] **Lock item / Unlock item** - `ContextToggleLockCommand`, visibility toggled by
      `SelectedItemsAllLocked`, exactly as old. The lock flag itself
      (`EntityCollectionData.IsLocked`) is now ported too, move commands skip locked items, and
      `Options.LockedItems` persists lock state to disk - see Locking. The padlock overlay icon on
      locked tiles is still not drawn (cosmetic only, doesn't affect behavior).
- [x] **Context menu request** - `ContextContextMenuRequestCommand`.
- [x] **Equip Item** - `EquipItemCommand`, via the already-existing `Commands.EquipItem`. Layer
      resolution needed a local `GetLayer(int id)` helper in the view model since this port's
      `StaticTile` has no dedicated `Layer` field the way old's does - it's the same tiledata.mul
      byte, just exposed as `StaticTile.Quality` (see Data model gaps).
- [x] **Hide** - `HideItemCommand`.
- [x] **Custom Actions** submenu plumbing (`CustomContextActions` + `ContextCustomActionCommand`)
      exists so a future caller can populate it the same ad-hoc way old-side does, but nothing
      constructs this Avalonia view model with any entries yet, so in practice the submenu is
      always empty. Distinct from the toolbar's `IEntityCollectionViewerAction` registry, which
      remains unported - see Extensibility.
- [x] **Target** - `ContextTargetCommand`, using the already-ported `Commands.WaitForTarget` for the
      "wait for an active target cursor" step.
- [x] **Target Owner** - `ContextTargetOwnerCommand`.
- [x] **Copy to clipboard** - `CopyToClipboardCommand`, via `Engine.UIInvoker.SetClipboardText`
      (the cross-process-safe path already used by `AutolootViewModel`) rather than WPF's direct
      `Clipboard.SetText`, since the ECV view model can't touch UI-framework clipboard APIs
      directly in this repo's out-of-process architecture.
- [x] **Double-click drills into a container OR opens the Object Inspector** - unchanged, already
      ported both sides.

Note on the old queued-action/status-row wrapping (`EnqueueAction`/`QueueAction`) that every one of
these handlers went through old-side: it's still not ported (see Queued actions & cancellation
below) - commands here run inline via `async`/`await` instead of through a cancellable queue with UI
feedback. The packet-level ordering it protected against is still handled by `ActionPacketQueue`
underneath; what's missing is only the progress/cancel UI.

## Filter

**Update:** the filter panel, the 5 ECV-only constraints, and profile persistence are all now
ported (see Toolbar section for the panel itself). Key divergence from old, confirmed with the user
rather than assumed: **no boolean-tree groups**. Old's `EntityCollectionFilterEntry` holds nested
`EntityCollectionFilterGroup`s with their own `And`/`Or`/`Not` operators; this port instead gives
each profile one flat, AND-only `ObservableCollection<AutolootConstraintEntry>`
(`FilterProfile.Conditions`). There is also no per-condition `Enabled` toggle (old's
`EntityCollectionFilterItem.Enabled`) - remove a row to disable it.

The other structural simplification: rather than introducing old's parallel
`EntityCollectionFilterItem`/`PropertyEntry` model, this port **extended the shared Autoloot types
themselves** so `AutolootHelpers.ConstraintsToPredicates` could be reused as-is for filtering:
`PropertyType` gained `Predicate`/`PredicateWithValue`, `PropertyEntry` gained a `Predicate` field
(`Func<Entity, AutolootConstraintEntry, bool>`), and `AutolootConstraintEntry` gained `Additional`
(string). This is additive - `AutolootViewModel` and everything else already using these types is
unaffected, since the new members are only populated by what registers them.

Old: `UI/Views/ECV/Filter/EntityCollectionFilterControl.xaml(.cs)`,
`EntityCollectionFilterViewModel.cs`, `Filter/Models/{EntityCollectionFilterEntry,
EntityCollectionFilterGroup, EntityCollectionFilterItem, GroupItem}.cs`.

- [ ] **Boolean-tree filter groups** - deliberately not ported; see above. `GroupItem` and
      `EntityCollectionFilterGroup` have no Avalonia equivalent at all.
- [x] **Filter conditions reuse the Autoloot constraint system** - `EntityCollectionViewerViewModel
      .RegisterFilterOnlyConstraints()` registers the same 5 built-ins old did: **Name** (substring
      match over item properties or name), **TileFlags**, **Distance**, **Organizer Match** (matches
      an entry in an existing Organizer profile via `OrganizerManager`), and **Is Multi**
      (`ArtDataID == 2`) - ported line-for-line off old's `EntityCollectionFilterViewModel`
      constructor, adapted to the extended `PropertyEntry`/`AutolootConstraintEntry` above.
      Plugin-contributed constraints (old's `AutolootManager.LoadAssemblies`) now arrive through
      `AutolootPropertyRegistration.LoadPluginProperties`, called last so a plugin can inspect or
      replace what is already registered; `AutolootViewModel` calls it at both its constraint-building
      sites, so the two lists stay identical.
- [x] **Filter profiles persist to `FilterProfiles.json`** - `LoadFilterProfiles`/
      `SaveFilterProfiles` on the view model, `AddProfileCommand`/`RemoveProfileCommand`, and
      `SelectedProfile`'s setter (which swaps `FilterConditions`' contents and re-applies if a filter
      is currently active - old's `SetActiveProfile`). Renaming is inline via an `EditTextBlock`
      bound to `SelectedProfile.Name` rather than a separate rename command/dialog. Saved shape is a
      flat `{ LastProfileID, Profiles: [{ ID, Name, Conditions: [...] }] }` - simpler than old's
      nested-group JSON since there's nothing recursive to serialize. **Always written** in this flat
      shape, but **read compatible** with an existing WPF-written `FilterProfiles.json`:
      `GetConditionTokens` falls back to old's `Groups[].Items[]`/`Constraint.Name` shape when
      `Conditions` isn't present, as long as the file doesn't use boolean-tree nesting (`Children` is
      silently skipped, not flattened, since there's no sound way to fold Or/Not semantics into a
      flat AND list). A WPF file's first save from this port permanently rewrites it to the flat
      shape.

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

**Update: ported.** `ClassicAssist.Shared/Data/Misc/EntityCollectionViewerOptions.cs` mirrors old's
`Serialize`/`Deserialize` field-for-field, so `EntityCollectionViewerOptions.json` written by either
side loads in the other (see the Filter section's note on `FilterProfiles.json` for the equivalent
claim there). `EntityCollectionViewerViewModel.Options` is loaded in the constructor
(`LoadOptions()`) and XAML now binds straight to `Options.AlwaysOnTop`/`Options.ShowChildItems`/etc.
rather than through VM-level mirror properties - `Options.PropertyChanged` (`OnOptionsChanged`)
triggers `Rebuild()` for the display-affecting ones and always saves. Collection *mutations*
(`LockedItems.Add`, `CombineStacksIgnore.Add`, etc.) don't raise `PropertyChanged` on `Options`
itself, so `ContextToggleLock` and `Configure` (the Settings window handler) call `SaveOptions()`
explicitly after touching those.

Old: `Data/Misc/EntityCollectionViewerOptions.cs`, `UI/Views/ECV/EntityCollectionViewerSettingsWindow.xaml`
+ `EntityCollectionViewerSettingsViewModel.cs`.

Every persisted field on `EntityCollectionViewerOptions` and where it's edited:

- [x] `AlwaysOnTop` (bool) - toolbar toggle (see Toolbar section).
- [x] `ShowChildItems` (bool) - toolbar toggle, persisted.
- [x] `HideLockedItems` (bool) - toolbar toggle, persisted; drives the `Rebuild()`-time filter (no
      `CollectionView` equivalent needed - see Locking).
- [x] `EnableHotkeys` (bool) - toolbar toggle, persisted; gates `HotkeyActionCommand`.
- [x] `SortStyle` (`EntityCollectionSortStyle`) - persisted; a new ECV window now opens to whatever
      sort was last chosen, matching old.
- [x] `LockedItems` (`ObservableCollection<int>`, serial numbers) - the actual lock-state store now;
      see Locking (the `HashSet<int>` workaround from the Hide Locked Items pass was replaced by
      this).
- [x] `ContainerSets` (`ObservableCollection<ContainerSet>`) - editable in the Settings window
      (add/remove sets, target-add/remove serials per set). **Not consumed anywhere yet** - the
      context menu's "Move to set" submenu that would read these back out is still explicitly out of
      scope (see Context menu section); this only builds and persists the sets themselves.
- [x] `CombineStacksIgnore` / `OpenContainersIgnore` - editable in the Settings window (ID and Cliloc
      use the reusable `GraphicEditTextBlock`/`ClilocEditTextBlock` controls - see below; Hue stays a
      plain `DataGridTextColumn`) and now actually consulted by `CombineStacks()`/`OpenAllContainers()`,
      which previously ignored them entirely.
- [x] `OpenContainersOnlyKnownContainers` (bool) - checkbox in the Settings window; `ContainerGumpIDs.json`
      was copied over from old-side (`ClassicAssist.Shared/Data/ContainerGumpIDs.json`, wasn't in the
      Avalonia tree at all before this) since the feature is a no-op without it.
- [x] `Assemblies` (`ObservableCollection<Assembly>`) - **not round-tripped, and checked to confirm
      that's fine.** `EntityCollectionViewerOptions.Deserialize` skips this key entirely rather than
      `Assembly.LoadFile`-ing whatever paths it finds; an old-side file's `Assemblies` array is
      silently dropped on next save from this port. Old-side, this collection is itself never read
      back out anywhere (checked) - `Assembly.LoadFile` in its `Deserialize` is called purely for the
      load side effect (getting the DLL into the process so `AutolootPropertyRegistration`'s
      reflection scan over loaded assemblies picks up its constraint types), not for the resulting
      `ObservableCollection<Assembly>` itself. Avalonia's global `AssistantOptions.Assemblies` +
      `PluginAssemblies.InvokeInitialize` already loads plugin DLLs the same way at startup, so
      constraint types reach the filter list regardless (see the Filter section) - this per-window
      list was redundant with that even on old-side.
- [x] **Settings window** - `EntityCollectionViewerSettingsWindow.axaml` +
      `EntityCollectionViewerSettingsViewModel`, opened via `ConfigureCommand` (new toolbar button,
      `ConfigureIcon`). Structurally simpler than old on purpose: one view model instead of three
      (`CombineStacksSettingsViewModel`/`OpenContainersSettingsViewModel`/
      `ContainerSetsSettingsViewModel`), and a single "OK" button that just closes (old's OK/Cancel
      pair was already documented as functionally identical - both just close, since the bound
      `Options` is mutated in place either way). ID and Cliloc use `Controls/GraphicEditTextBlock` and
      `Controls/ClilocEditTextBlock` - reusable controls (not scoped to this window), each wrapping
      the base `EditTextBlock` and owning their own picker/target logic rather than routing through
      view model commands, so any caller can just drop one in and bind `ID`/`Cliloc`. `EditTextBlock`
      itself gained a `Buttons` content property to host them (ported from old's base `EditTextBlock`,
      which already had one). Unlike old's `GraphicEditTextBlock`/`ClilocEditTextBlock`, these don't
      cross-wire sibling ID/Cliloc/Hue columns on target (old's `RelativeSource`+`BindingProxy` trick
      for reaching a sibling `DataGridTemplateColumn`'s control doesn't map cleanly to Avalonia) - each
      field is targeted independently. Hue stays a plain `DataGridTextColumn`, same as old's
      `HueEditTextBlock` (no picker button there either).

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

- [x] **Ported.** `QueueAction` (`ClassicAssist.Shared/UI/ViewModels/QueueAction.cs`), `QueueActions`,
      `_threadQueue` (reusing the already-ported `ThreadPriorityQueue<T>`), `EnqueueAction`,
      `QueueActions_CollectionChanged` and `ProcessQueue` all exist on
      `EntityCollectionViewerViewModel` now, matching old's shape line-for-line. A status-row
      `ItemsControl` with a per-row cancel button was added below the item list in
      `EntityCollectionViewer.axaml`. The already-ported context commands (move-to-backpack/bank/
      container/ground, drop-to-ground, use item, open container, context menu request, target,
      target owner, equip item) were rewired through it. **Not rewired** because they're not ported
      at all yet: combine stacks, open all containers, autoloot container, organizer's Play - those
      still need this same treatment whenever they're built.

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

- [x] **The lock/unlock behavior itself is ported**: `EntityCollectionData.IsLocked`,
      `SelectedItemsAllLocked`, and `ContextToggleLockCommand` all exist now (see Context menu), and
      the move-to-* / drop-to-ground context commands skip locked items exactly like old's
      `.Where(i => !i.IsLocked)` filters.
- [x] **Hide Locked Items is now ported** - see Toolbar section. Getting it working first surfaced
      (and fixed) a real bug: `IsLocked` lived only on `EntityCollectionData` instances that
      `Rebuild()` discards and recreates on every collection change, so it silently reset on the next
      server update. A session-only `HashSet<int> _lockedSerials` on the view model fixed that
      initially.
- [x] **On-disk persistence is now ported too.** `Options.LockedItems` (real, from Settings/Options
      persistence) replaced that `HashSet<int>` outright - `ContextToggleLock` adds/removes serials
      there directly and calls `SaveOptions()` explicitly (collection mutations don't raise
      `Options.PropertyChanged`), and `Rebuild()` restores `IsLocked` from `Options.LockedItems` the
      same way it used to from the `HashSet`. Locks now survive both a rebuild and closing/reopening
      the window, matching old.
- [x] **Padlock overlay icon** - `EntityCollectionViewer.axaml`'s item template now overlays a 12x12
      `Assets/lock.png` (the same padlock used by `SkillsTabControl`'s `LockStatusValueConverter`,
      rather than porting old's separate `EntityCollectionViewerViewModel.PadlockIcon` resource) at the
      bottom-right of the art image, inset 2px, `IsVisible` bound to `IsLocked`. Old positions it flush
      against the corner (`ListStyles.xaml:24-27`/`39-41`); this port insets it slightly instead.
      Needed `EntityCollectionData` to start raising `PropertyChanged` for `IsLocked` (previously a
      plain auto-property) - `ContextToggleLock` flips it on rows already on screen rather than
      rebuilding them, so without notification the icon would only ever reflect the state as of the
      last `Rebuild()`, not live toggles.

## Sorting

- [x] **Sort by enum value is ported** - `SortStyle` property (Avalonia VM line 134) triggers
      `Rebuild()` on set; `GetSorter()` (line 191) maps `Name`/`Serial`/`Hue`/`Quantity` to the same
      `NameThenSerialComparer`/`SerialComparer`/`HueThenAmountComparer`/`QuantityThenSerialComparer`
      types the old code uses, defaulting to `IDThenSerialComparer` - this matches old's
      `GetComparer` (line 1500) case-for-case for the styles that exist on both enums.
- [x] **Enum now has both members**: `None` (`GetSorter()` returns `null`, and both `Rebuild()`'s
      child-flattening path and `EntityCollectionDataExtensions.ToEntityCollectionData` skip
      `.OrderBy` when the comparer is null) and `Weight` (`WeightThenSerialComparer`, ported
      line-for-line including the AOS-tooltip cliloc-weight branch). Default changed from `ID` to
      `None` to match old.
- [x] **Persistence is now ported** - `ChangeSortStyle` sets `Options.SortStyle`, persisted like
      every other `Options` field; a new ECV window opens to the last-chosen sort, matching old. See
      Settings/Options persistence.

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
- [x] **In-window hotkeys are now fully ported.** B/C/K/G/D route through `HotkeyActionCommand`
      (gated on the now-ported `EnableHotkeys`, matching old's `Options.EnableHotkeys` gate) instead
      of straight to the context commands, reproducing old's indirection
      (`ClassicAssist/UI/Views/EntityCollectionViewer.xaml:205-212`). Ctrl+C is bound directly to
      `CopyToClipboardCommand` in both old and new, deliberately *not* gated by
      `EnableHotkeys`/`HotkeyActionCommand` - it was never part of that indirection old-side either.
      `Options.EnableHotkeys` is persisted across window opens - see Settings/Options persistence.

## Data model gaps

- [x] **`EntityCollectionData.IsLocked`** - now ported (see Locking), persisted via
      `Options.LockedItems` and raising `PropertyChanged` (see below - needed for both the padlock
      overlay and this).
- [x] **`EntityCollectionData.NotifyPropertiesUpdated()`** - ported. `EntityCollectionData` now
      extends `SetPropertyNotifyChanged` (it was a plain class with no `INotifyPropertyChanged` at
      all) and re-raises `PropertyChanged` for `Name`/`FullName`/`Pixmap` (old's `Bitmap`).
      `EntityCollectionViewerViewModel.OnItemPropertiesUpdated` subscribes to
      `IncomingPacketHandlers.ItemPropertiesUpdatedEvent`, matching old: re-applies a name override
      before refreshing the row (OPL overwrites `Item.Name` with the server value, clobbering a
      user rename otherwise), calls `NotifyPropertiesUpdated()`, then re-sorts the row's position if
      a property-derived sort is active, since names/properties routinely arrive after the row was
      already inserted.
- [ ] **`EntityCollectionData.FullName`** - present on both sides with matching logic
      (join item `Properties` text with `\r\n`, fall back to `Name`); not a gap, listed only to
      confirm it was checked.
- [x] **`EntityCollectionSortStyle` enum now has `None` and `Weight`** - see Sorting.
- [x] **`StaticTile` has no `Layer` field, unlike old's** (`ClassicAssist.Shared/UO/Data/StaticTile.cs`
      vs `ClassicAssist/ClassicAssist/UO/Data/StaticTile.cs`) - it's the same tiledata.mul byte at
      the same offset, just already exposed under the name `Quality` and already relied on that way
      by `Commands.EquipItem`/`EquipType` (`ClassicAssist.Shared/UO/Commands.cs`). Not a parsing bug,
      just a naming divergence - the ECV's Equip Item command and `CopyToClipboard`'s layer line
      both work around it with a local `GetLayer(int id)` helper rather than adding a `TileData.GetLayer`
      static to match old, since nothing else in this port calls it by that name yet.
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
- `Options.ShowChildItems` toggle (recursively flattens container contents) - persisted now, see
  Settings/Options persistence
- `ShowProperties` toggle (full property tooltip/label vs. just the name) - works, not persisted -
  matches old, which has no `ShowProperties` field on `EntityCollectionViewerOptions` either
- Basic sort by enum value for the styles that exist on both sides (Name/Serial/Hue/ID/Quantity) -
  see Sorting for what's missing
- Refresh (including the `_customRefresh` hook for a caller-supplied re-fetch)
- Status label (`{0} items, {1} selected, {2} total amount}`) tracking selection
- The `Grid Container Viewer` global hotkey that opens an ECV window (full port, see Hotkeys)
- The item context menu and its commands (see Context menu), except "Move to set", which is
  intentionally excluded, and the queue/status-row UI the old commands ran through (see Queued
  actions & cancellation)
