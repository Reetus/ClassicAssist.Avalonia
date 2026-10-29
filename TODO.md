# TODO — project audit findings

Automated audit of the Avalonia port (70 `.axaml`, ~820 `.cs` across `ClassicAssist.Avalonia`,
`ClassicAssist.Shared`, `ClassicAssist.Launcher`, `ClassicAssist.Updater`), looking for XAML/markup
defects, GUI-thread deadlocks, porting bugs and lifecycle leaks. **No code has been changed.**

- Build baseline: `dotnet build ClassicAssist.slnx` → **0 errors, 7 warnings**, all the same
  `NU1902` MessagePack advisory (see [Build & dependencies](#build--dependencies)).
- Every item below was verified against the source (file:line cited). Items in
  [Needs investigation](#needs-investigation) are unconfirmed and should be triaged before fixing.

Priority key: **P0** crash / permanent hang · **P1** leak, race or data loss · **P2** wrong behaviour ·
**P3** markup & silent binding failure · **P4** hygiene.

---

## P0 — crashes, deadlocks and hangs

- [ ] **`GetClipboardText()` deadlocks the UI thread.**
  `ClassicAssist.Avalonia\AvaloniaUIInvoker.cs:159-162` — `return _dispatcher.InvokeAsync( () => Engine.MainWindow.Clipboard?.TryGetTextAsync() ).Result;`
  The lambda resolves to `InvokeAsync<TResult>(Func<Task<TResult>>)`, so `.Result` blocks the UI thread
  on work queued to that same thread → waits forever. Only caller today is
  `ClassicAssist.Shared\UI\ViewModels\Agents\AutolootViewModel.cs:445-447` (`ClipboardPaste`, a
  `RelayCommand`, i.e. UI thread) — so it is **latent**, because `ClipboardPasteCommand` is not bound
  in any `.axaml` (the WPF build wired it to Ctrl+V). Fix before re-binding: make the method
  `async Task<string>` end-to-end, or await `Clipboard.TryGetTextAsync()` directly without the
  dispatcher hop. Secondary: `Clipboard?.TryGetTextAsync()` can return a null `Task<string>`
  (no clipboard platform) which faults the operation with NRE.

- [ ] **Autoloot copy/paste is silently missing from the UI.**
  Same root: `AutolootViewModel.cs:101-103` defines `ClipboardPasteCommand` / `ClipboardCopyCommand`
  and nothing binds them. Confirm the intended binding (Ctrl+C/V on the autoloot grid) and restore it —
  *after* the deadlock above is fixed.

- [ ] **Packet-processing thread can block indefinitely in Scavenger `CheckArea`.**
  `ClassicAssist.Shared\UI\ViewModels\Agents\ScavengerTabViewModel.cs:283-284` —
  `ActionPacketQueue.EnqueueDragDrop(...).Wait()` with no timeout, inside `lock (_scavengeLock)`.
  Reached from `ItemsOnCollectionChanged` (`:215-228`) on the single `IncomingQueue` worker
  (`Engine.cs:427-451`), which also runs `PacketWaitEntries.CheckWait` — so while it is parked, *every*
  wait in the app (autoloot, `WaitForContainerContents`, macro commands) stalls. Can form a wait cycle
  with `ActionPacketQueue` requeue actions that themselves wait for a 0x3C packet only this worker can
  deliver (`ActionPacketQueue.cs:323-343` → `UO\Commands.cs:644-659`). Give the wait a timeout and
  release the lock while waiting.

- [ ] **Queue worker loops have no try/catch — one throwing action kills the worker or the process.**
  `ClassicAssist.Shared\Misc\ThreadPriorityQueue.cs:98` (`_onAction( queueItem )`, unguarded in the
  loop — the only try/catch in the file is `ObjectDisposedException` at `:114`/`:138`),
  `ClassicAssist.Shared\Misc\ThreadQueue.cs:46`,
  `ClassicAssist.Shared\UO\Network\ActionPacketQueue.cs:124`. On .NET an unhandled exception on any
  thread terminates the process (`Program.cs:50-51` only logs). Reachable fault: `Engine.SendPacketToClient`
  → `Host.SendPacketToClient(...).Result` (`Engine.cs:1208-1211`) is not exception-wrapped and an RPC
  fault on disconnect escapes. The hazard is already documented in
  `EntityCollectionViewerViewModel.cs:1829-1831`. Wrap each callback invocation in try/catch and log.

- [ ] **Launcher: NRE crash on Start with no shard selected.**
  `ClassicAssist.Launcher\ViewModels\MainViewModel.cs:422` — `await Utility.ResolveAddress( SelectedShard.Address )`,
  while `StartCommand.CanExecute` (`:225`) only checks client/data paths. `SelectedShard` is set only
  from config (`:142-150`) or user pick, and Avalonia's ComboBox does **no** auto-selection when only
  `ItemsSource` is bound (`Views\MainWindow.axaml:37-39`). First run (ctor returns early at `:33-36`
  with no `Launcher.json`) or a stale `SelectedShard` name → `SelectedShard == null` → NRE inside
  `async void` `RelayCommandAsync.Execute` → process crash. Add `&& SelectedShard != null` to
  `CanExecute` and guard `Start`.

- [ ] **Async-void `ICommand.Execute` turns any command exception into a process crash.**
  `ClassicAssist.Shared\UI\ViewModels\BaseViewModel.cs:174-177` and
  `ClassicAssist.Launcher\ViewModels\BaseViewModel.cs:101-104` — `public async void Execute(...)`,
  no try/catch anywhere in the chain, and no global `DispatcherUnhandledException` handler in
  `App.axaml.cs`. Concrete crash sources: `MainViewModel.Start` (`Process.Start` → `Win32Exception`),
  `Utility.ResolveAddress` (`Dns.GetHostEntryAsync` → `SocketException`; only the null-return path is
  handled), `MainViewModel.CheckPresets`' JSON parse. Either wrap `Execute` in try/catch + user-visible
  error, or register a global handler.

- [ ] **Profile save throws "duplicate key" once the Debug window has been opened twice.**
  `ClassicAssist.Shared\UI\ViewModels\DebugViewModel.cs:138` — `json.Add( "Packets", options )` on a
  fixed key throws `ArgumentException` on the second call. `Views\DebugWindow.axaml:15-17` constructs a
  fresh `DebugViewModel` per window and `MainWindowViewModel.ShowDebugWindow`
  (`MainWindowViewModel.cs:51-54` → `AvaloniaUIInvoker.cs:68-86`) creates a new window every invocation
  with no single-instance guard. `UiHost.cs:41-47` serialises **every** registered `ISettingProvider`,
  and nothing is ever unregistered (see the `BaseViewModel._viewModels` leak below). Save is called with
  no try/catch from `Options.Save` (`Options.cs:466-480`) — and from `Misc\WindowInitializedBehaviour.cs:52-59`
  **in MainWindow's `OnClosing`**, where an exception is fatal (the codebase itself documents this at
  `Views\DebugWindow.axaml.cs:87-88`). Repro: open Debug, close, open again, then exit or switch
  linked profile. Fix both: guard the key, and make DebugWindow single-instance.

- [ ] **`AvaloniaMessageBoxProvider.Show` can hang every awaiter forever.**
  `ClassicAssist.Avalonia\Misc\AvaloniaMessageBoxProvider.cs:22-38` — fire-and-forget `Action` running
  an async lambda; if `ShowDialog` throws (e.g. `Engine.MainWindow` is null — `UiHost.MainWindow` is
  assigned at `App.axaml.cs:55`, *after* `MessageBoxProvider` is installed at `:48`), the exception
  disappears into async-void and `promise` is **never resolved**. Every
  `await Engine.MessageBoxProvider.Show(...)` then hangs: `HotkeysTabViewModel.CheckOverwriteHotkey:512`,
  `OptionsTabViewModel.cs:300,315,331,346,362,372`, … Wrap in try/catch and `TrySetException` /
  `TrySetResult` in a `finally`. Same owner-null fragility in `AvaloniaUIInvoker.InvokeDialog` /
  `GetHueAsync` (`AvaloniaUIInvoker.cs:110,132`).

---

## P1 — leaks, races and data loss

- [ ] **`BaseViewModel` roots every ViewModel ever created in a static list.**
  `ClassicAssist.Shared\UI\ViewModels\BaseViewModel.cs:37,50` — `_viewModels.Add( this )` in the ctor,
  no `Remove`/`Clear` anywhere in the repo (verified). `UiHost.cs:41-61` uses it as a service locator for
  profile save/load, so stale instances also participate in settings serialisation (→ the duplicate-key
  crash above). Every dialog/window VM stays pinned (`EntityCollectionViewerViewModel` cleans up in
  `OnClosed` and is rooted anyway). Needs a weak list or explicit deregistration on window close.

- [ ] **Debug window VM graph leaks and keeps processing live packet traffic forever.**
  `ClassicAssist.Shared\UI\ViewModels\DebugViewModel.cs:47-50` subscribes
  `Engine.PacketReceivedEvent / PacketSentEvent / InternalPacketReceivedEvent / InternalPacketSentEvent`
  with **no `-=` anywhere** (verified); `Queue = new ThreadQueue<PacketEntry>(...)` (`:46`) is
  `IDisposable` and never disposed. `DebugWindow.OnClosing` (`Views\DebugWindow.axaml.cs:83-108`) only
  serialises settings. Same pattern per window: `Debug\DebugGumpViewModel.cs:21,33` (lambda subscriptions
  to `IncomingPacketHandlers.GumpEvent` / `OutgoingPacketHandlers.GumpEvent`, ungated, append every gump
  forever), `DebugBuffIconsViewModel.cs:19`, `DebugMenusViewModel.cs:39`, `DebugSpecialMovesViewModel.cs:18`.
  If capture is enabled, `Items` grows without bound (`:250-258`). Unsubscribe + dispose on close.

- [ ] **Two `MainWindowViewModel`s are constructed; the orphan is pinned by static events.**
  `ClassicAssist.Avalonia\App.axaml.cs:52` sets `DataContext = new MainWindowViewModel()` while
  `Views\MainWindow.axaml:19-21` already declares `<Window.DataContext><viewModels:MainWindowViewModel/></Window.DataContext>`.
  The replaced instance keeps `Engine.UpdateWindowTitleEvent += OnUpdateWindowTitleEvent`
  (`MainWindowViewModel.cs:17`) and an anonymous `AssistantOptions.ProfileChangedEvent` lambda (`:18`)
  that can never be unsubscribed — so every session leaks a VM and does title-update work twice. Remove
  one of the two constructions.

- [ ] **`FilterControl` registers a global `AvaloniaProperty` class handler per instance.**
  `ClassicAssist.Avalonia\Controls\FilterControl.axaml.cs:37` —
  `IsFilterVisibleProperty.Changed.AddClassHandler<FilterControl>(...)` inside the **instance**
  constructor. `AvaloniaProperty.Changed` is property-wide static state: one subscription per control
  ever constructed, never removed, and every change invokes all of them. Move to a static constructor.

- [ ] **`LockStatusValueConverter` leaks a `Bitmap` and an undisposed asset stream on every conversion.**
  `ClassicAssist.Avalonia\Misc\LockStatusValueConverter.cs:25,28,32` — `return new Bitmap( AssetLoader.Open(...) )`
  with no caching (contrast `PathToThumbnailValueConverter` / `PixmapValueConverter`, which do cache).
  The skills grid re-runs `Convert` on every packet-driven skill update while the tab is open →
  unbounded native bitmap + stream growth. Cache the three brushes/bitmaps in statics and dispose the streams.

- [ ] **`AliasCommands` static dictionaries are mutated with no synchronisation.**
  `ClassicAssist.Shared\Data\Macros\Commands\AliasCommands.cs:13` (`_aliases`), `:171` (`_playerAliases`).
  Writers: macro threads (`:73-127`), hotkey dispatch (`HotkeyManager.cs:171`), MCP. Reader/writer on the
  UI thread: `ActiveObjectsViewModel.RefreshPlayerAliases` (`ActiveObjectsViewModel.cs:159-169`,
  `GetAllAliases().ToList()` while a macro mutates). Plain `Dictionary` → torn buckets,
  `IndexOutOfRangeException`, lost aliases. Use `ConcurrentDictionary` or a lock.

- [ ] **`ClilocFilter.Filters` / `SoundFilter.Items` / `ItemIDFilter.Items`: static `ObservableCollection`
  mutated on the UI thread while enumerated on packet threads.**
  `ClassicAssist.Shared\Data\Filters\ClilocFilter.cs:16`, enumerated at `:80-134` from
  `IncomingPacketHandlers` on the `IncomingQueue` worker; mutated from
  `ClilocFilterConfigureViewModel.cs:82` on the UI thread → `InvalidOperationException: Collection was
  modified` inside the packet path (swallowed, message dropped). Same shape for
  `AutolootViewModel.CheckItems`' `Filters.Select(...)` (`AutolootViewModel.cs:256`). Snapshot or lock.

- [ ] **`AutolootViewModel.OnCorpseEvent` holds `lock` across multi-second blocking waits.**
  `ClassicAssist.Shared\UI\ViewModels\Agents\AutolootViewModel.cs:491-550` — `lock (_autolootLock)`
  around `we.Lock.WaitOne( 2000 )` (`:510`), `Thread.Sleep( 1000 )` (`:522`) and `t.Wait( LOOT_TIMEOUT )`
  (`:548`, 5 s per item). Reachable from `Task.Run` (`IncomingPacketHandlers.cs:711`) **and**
  synchronously from the macro thread (`AgentCommands.cs:122`). Serialises unrelated callers, and
  combined with the Scavenger packet-thread stall the `WaitOne(2000)` can time out and silently miss items.

- [ ] **`Engine.SendPacketToServer`/`SendPacketToClient`: rate-limit spin + blocking RPC `.Result` inside
  `lock`, callable from the UI thread.**
  `ClassicAssist.Shared\Engine.cs:611-657` (locks `:613`/`:636`, `Thread.Sleep(1)` spins `:615-618`/`:640-643`,
  RPC `.Result` at `:1203-1211`). UI-thread-reachable via `UOC.SystemMessage` from
  `ScavengerTabViewModel.Insert`, `CountersTabViewModel.Recount`, `OptionsTabViewModel` handlers →
  UI freezes per call; a wedged plugin = indefinite freeze (no timeout on the `.Result`).
  Also one subscriber away from a lock-order inversion with `_autolootLock`.

- [ ] **Closing an EntityCollectionViewer freezes the UI thread until the in-flight queue action finishes.**
  `ClassicAssist.Shared\UI\ViewModels\EntityCollectionViewerViewModel.cs:530-538` (`Cleanup` →
  `_threadQueue?.Dispose()`) runs from `OnClosed` on the **UI thread**
  (`Views\EntityCollectionViewer.axaml.cs:60-67`); `ThreadPriorityQueue.StopThread` does
  `_workerThread.Join()` (`ThreadPriorityQueue.cs:131-148`). Queue actions are long loops with per-item
  `WaitOne( 3000 )` retries (`:2069-2121`) — the UI can park for minutes on a large selection.

- [ ] **`AboutControlViewModel`: timers leak across reconnects; per-player handlers accumulate.**
  `ClassicAssist.Shared\UI\ViewModels\AboutControlViewModel.cs:250-262` — `OnConnectedEvent` creates new
  `Timer`/`_pingTimer` every connect and never disposes the previous ones; `OnDisconnectedEvent`
  (`:187-192`) stops only `_timer`, so `_pingTimer` keeps sending ping packets while disconnected
  (`PingServer`, `:264-289`, includes `we.Lock.WaitOne( 5000 )`). Also `:140-146` + `:171-178`:
  `OnMobileUpdatedEvent` re-adds `player.LastTargetChangedEvent` / `MobileStatusUpdated` handlers on every
  player mobile update / war-mode packet → duplicate handlers accumulate per session.

- [ ] **`Options.CurrentOptions.PropertyChanged` subscription goes stale after profile switch.**
  `ClassicAssist.Shared\UI\ViewModels\BaseViewModel.cs:53` binds to the instance current at construction,
  but `Options.CurrentOptions` is **replaced** on profile change (`Data\AssistantOptions.cs:274`,
  `GeneralControlViewModel.cs:340`, `NewProfileViewModel.cs:57`). Consequence: `[OptionsBinding( Property = "AlwaysOnTop" )]`
  on `MainWindowViewModel` (`:23-28`, bound as `Topmost` in `Views\MainWindow.axaml:14`) stops tracking
  after a linked-profile switch, and every old `Options` instance stays rooted by the accumulated handlers.

- [ ] **`BaseViewModel._viewModels` (`List<T>`) is mutated on the UI thread while save/load iterates it
  from RPC threads.** Constructors run on the UI thread (`BaseViewModel.cs:50`); `Options.Save` is called
  from MCP tool handlers on StreamJsonRpc threads (`ClassicAssist.Shared\Mcp\McpTools.cs:314,346,381`)
  and shutdown (`Engine.cs:336`) → `UiHost.OnOptionsSave/OnOptionsLoad` do `[.. _viewModels]`
  (`BaseViewModel.cs:57`) unsynchronised.

- [ ] **Startup blocks the UI thread on unbounded RPC `.Result` + full data-file load.**
  `ClassicAssist.Avalonia\App.axaml.cs:44-67` → `Engine.InstallRPC` (`Engine.cs:191-244`):
  `GetClientPath().Result`, `GetClientVersion().Result`, `GetWindowHandle().Result`,
  `IsReflectionAvailable().Result` (`:198-201`) have **no timeout**, then `Art/Hues/Cliloc/TileData/Statics/MapInfo.Initialize`
  and `Cliloc.Preload()` (≈1.6 s, per the comment) — all on the UI thread behind the splash. A
  wedged-but-connected plugin leaves the UI blocked forever behind an undismissed splash
  (`SplashWindow` is only closed in the `finally`, `App.axaml.cs:61-66`). StreamJsonRpc has no default
  request timeout — add one.

- [ ] **Gump prompts block forever on no response.**
  `SelectionPromptGump.cs:88`, `MessagePromptGump.cs:74`, `ConfirmPromptGump.cs:68`, `ItemArrayGump.cs:170` —
  `AutoResetEvent.WaitOne()` with no timeout; a lost/unanswered gump hangs the calling macro thread
  indefinitely. (Same as upstream WPF.) Add a timeout with a documented failure result.

---

## P2 — port correctness bugs

- [ ] **Hue picker "Cancel" silently applies hue 0 — dialog result never observed.**
  `ClassicAssist.Avalonia\AvaloniaUIInvoker.cs:126-136` returns `window.SelectedHue` after `ShowDialog`
  with no OK/Cancel distinction; `HuePickerWindow.SelectedHue` defaults to 0 (`Views\HuePickerWindow.axaml.cs:53-57`).
  Callers write it straight into settings: `FriendsTabViewModel.cs:170-176`
  (`Options.CurrentOptions.RehueFriendsHue = hue; MainCommands.Resync();`) and
  `AutolootViewModel.cs:719-721`. Cancelling overwrites the previous hue with 0. Return a nullable /
  dialog result.

- [ ] **`AssistantOptions.SetLanguage` assigns `DefaultThreadCurrentUICulture` twice; `DefaultThreadCurrentCulture` never set.**
  `ClassicAssist.Shared\Data\AssistantOptions.cs:311-312` — the duplicated line is a straight porting
  typo. Only the calling thread gets `CurrentCulture`, so RPC dispatch, macro threads and timers keep the
  OS culture after a language override → divergent numeric/date formatting per thread.

- [ ] **`AudioPlayback.Play` disposes the `SoundPlayer` immediately after starting async playback.**
  `ClassicAssist.Shared\Misc\AudioPlayback.cs:32-40` + `:54-64` — `using (SoundPlayer player = ...)` calls
  `player.Play()` (asynchronous) for `sync == false` then disposes it at end of `using`, cutting off or
  preventing playback. Reachable from macro `PlaySound( path, playSync=False )`
  (`Data\Macros\Commands\MainCommands.cs:214-235`). The `PlaySync` path is fine.

- [ ] **`ShardsViewModel.GetStatus`: `ReadTimeout` does not apply to `ReadAsync`.**
  `ClassicAssist.Launcher\ViewModels\ShardsViewModel.cs:163-169` — `stream.ReadTimeout = 60000;` then
  `await stream.ReadAsync(...)`. `ReadTimeout` only affects synchronous `Read`; the connect is guarded by
  `Task.WhenAny(..., Task.Delay(5s))` (`:153`) but the read is not, so a server that accepts and goes
  silent hangs that ping task until TCP gives up. Wrap the read in `Task.WhenAny` too.

- [ ] **`DialogWindowViewModel.ImagePath` ternary repeats `MessageBoxImage.Warning` — the Information
  icon can never render.** `ClassicAssist.Avalonia\ViewModels\DialogWindowViewModel.cs:23-26` — third
  branch must be `MessageBoxImage.Information`; `dialog-info.png` is unreachable and the image area
  renders empty. Porting typo.

- [ ] **`ConfirmDeleteBehaviour` leaves the button stuck on `PendingIcon`.**
  `ClassicAssist.Avalonia\Misc\ConfirmDeleteBehaviour.cs:94-106` — the confirm branch cancels the 2-second
  icon revert (`:80-92`) but never restores `image.Source = originalIcon`; if the row survives (or
  `_deleteCommand.CanExecute` is false) the icon stays "armed" forever.

- [ ] **`MultiValueSelector.Values` styled property shares one default `ObservableCollection` across all
  controls.** `ClassicAssist.Avalonia\Controls\MultiValueSelector.axaml.cs:44-46` —
  `Register<MultiValueSelector, ObservableCollection<int>>( nameof( Values ), [] )` creates the default
  once; `Values ??= []` in `OnTextBoxKeyDown` (`:186`) sees the shared non-null default and mutates it,
  leaking values between unbound controls.

- [ ] **`MessagePromptGump.OnResponse` indexes `textEntries[0]` without a count check.**
  `ClassicAssist.Shared\UO\Data\Gumps\MessagePromptGump.cs:57` — `textEntries?[0].Value` throws
  `ArgumentOutOfRangeException` for a non-null empty list (malformed/cancelled response), escaping into
  the packet response path. `SelectionPromptGump.cs:68-71` guards its `switches[0]` correctly — make these
  consistent.

- [ ] **`ClilocEditTextBlock.OnTargetClick` maps "no properties" to cliloc 0 instead of "Any" (-1).**
  `ClassicAssist.Avalonia\Controls\ClilocEditTextBlock.axaml.cs:104` — `?? -1` only covers a null
  `item`/`Properties`; an empty `Properties` list yields `FirstOrDefault() == 0`, an invalid cliloc.

- [ ] **Zip-slip in updater module extraction.**
  `ClassicAssist.Updater\ViewModels\MainViewModel.cs:216-220` + `:230-248` — `entry.FullName.Split('/')`
  segments are `Path.Combine`d without rejecting `..` or rooted segments, so a crafted `Modules.zip` entry
  writes outside `Modules/`. Packages come from GitHub releases (moderately trusted) — hardening gap.

- [ ] **Updater accepts `.tar.gz` assets but extracts with `ZipFile`.**
  `PlatformPackage.cs:118-122` treats `.tar.gz` as a package; `MainViewModel.ExtractPackage` (`:466`) only
  reads zip. A release shipping only `.tar.gz` fails at extraction. Either add tar support or filter the
  asset list.

- [ ] **Launcher config save can throw out of `OnClosing`; save is non-atomic.**
  `ClassicAssist.Launcher\Views\MainWindow.axaml.cs:19-27` calls `ClosingCommand.Execute(null)` directly;
  `MainViewModel.Closing` (`MainViewModel.cs:643`) writes `new StreamWriter(...)` with no try/catch — an
  IO error (read-only install dir) throws out of window-close, and a mid-write crash truncates
  `Launcher.json` (no temp-file + rename).

- [ ] **`AvaloniaUIInvoker.FindWindowType` scans the assembly with `GetTypes()` per call, on the caller's
  thread.** `AvaloniaUIInvoker.cs:164-175` — reflection-heavy scan before the dispatcher hop; from UI
  commands (`AboutControlViewModel.ShowItems`) this is jank per window open, and `:171` calls
  `MessageBoxProvider.Show` without `?.` (null until `InstallRPC`, `Engine.cs:121,194`) where `:185` uses `?.`.
  Cache the `Type` lookup.

---

## P3 — XAML / markup

- [x] **`CSVImportWindow` link colour references a resource key that does not exist.**
  `ClassicAssist.Avalonia\Views\Autoloot\CSVImportWindow.axaml:41` —
  `Foreground="{DynamicResource AccentColor}"`. Avalonia's Fluent theme has no standalone `AccentColor`
  key (only `SystemAccentColor*`), and the repo dictionaries define only `SystemAccentColor*`.
  `DynamicResource` to a missing key silently yields null → the "hyperlink" renders in the inherited
  default foreground. Use an accent brush key that actually resolves.

- [x] **`CustomPropertiesWindow` Arguments checkbox can almost certainly never be toggled.**
  `ClassicAssist.Avalonia\Views\Autoloot\CustomPropertiesWindow.axaml:18` declares the `DataGrid` with no
  `IsReadOnly`, while the app-wide style sets it
  (`ClassicAssist.Avalonia\App.axaml:49-52`, `ClassicAssist.Launcher\App.axaml:31-34` —
  `<Style Selector="DataGrid"><Setter Property="IsReadOnly" Value="True"/></Style>`, and no
  `IsReadOnly="False"` exists anywhere). Columns `Name`/`Cliloc` are explicitly `IsReadOnly="True"`
  (`:23-24`) — strong evidence the grid is meant to be editable — so the checkbox column (`:25`) and the
  `Argument_Index` editor gated on it (`:29`) are dead. Set `IsReadOnly="False"` on this grid and keep the
  column-level flags. **Verify by clicking** — the exact failure mode (suppressed vs. revert) was not
  observed at runtime.

- [ ] **Compiled bindings are effectively unused (latent silent-failure risk).**
  No project sets `AvaloniaUseCompiledBindingsByDefault` (zero matches in any `.csproj`/`.props`), and only
  **5 of 70** `.axaml` files declare `x:DataType` (`Views\EntityCollectionViewer.axaml:15`,
  `Views\EntityCollectionViewerSettingsWindow.axaml:11`, `Views\Hotkeys\HotkeyOptionsWindow.axaml:11`,
  `ClassicAssist.Updater\Views\MainWindow.axaml:8`, `ClassicAssist.Updater\Views\ProcessesView.axaml:8`).
  Everywhere else `<Xaml.DataContext><vm/></Xaml.DataContext>` is combined with reflection `{Binding}` —
  the type is right there in the file but not expressed. Binding paths are only checked at runtime
  (silent log errors), reflection-based (slower, trim-unsafe), and `DataTemplate DataType="..."` in those
  files is inert. All currently-used paths were verified to resolve to real members, so this is latent —
  but it is exactly the class of defect that stays invisible here. Consider enabling compiled bindings
  per-file as files are touched.

- [x] **Dead WPF remnant: `Tag="Colored"`.** `ClassicAssist.Avalonia\Views\DialogWindow.axaml:30` — nothing
  reads `Tag` anywhere. Remove.

- [x] **Divergent `StringFormat` escaping in one file.**
  `ClassicAssist.Avalonia\Views\Agents\ScavengerTabControl.axaml:44` uses `StringFormat=0x\{0:x\}` (WPF-style
  backslash escaping — works via XamlX's ported `MeScanner`), while the five other hex-format bindings
  (`CountersTabControl.axaml:29`, `OrganizerTabControl.axaml:44`, `VendorSellTabControl.axaml:23`,
  `DebugGumpControl.axaml:16`, `DebugMenusControl.axaml:16`) use the idiomatic `StringFormat={}0x{0:x}`.
  Normalise to the latter.

- [x] **Unused `xmlns` prefixes pointing at non-existent namespaces.**
  `Views\SkillsTabControl.axaml:9` (`clr-namespace:ClassicAssist.Avalonia.Properties` — no such namespace);
  `Views\Agents\CountersTabControl.axaml:7` and `Views\Agents\ScavengerTabControl.axaml:7`
  (`clr-namespace:ClassicAssist.UI.Misc.ValueConverters` — `ClassicAssist.UI.Misc` exists, `ValueConverters`
  does not). All unused, so they compile. Remove.

- [x] **Duplicate `xmlns` for the same CLR namespace.**
  `Views\ObjectInspectorWindow.axaml:5-6` — `r:` and `resources:` both map to
  `ClassicAssist.Shared.Resources`; `resources:` is never used. Also
  `Controls\TextBoxKey.axaml:5-6,14-19` — `xmlns:i`/`xmlns:ia` used only by a commented-out
  `<i:Interaction.Behaviors>` block marked `<!--TODO-->`. Clean up.

---

## P4 — hygiene

- [ ] **Undisposed resources.**
  `ClassicAssist.Avalonia\Misc\StringToResourceConverter.cs:26` (resource stream per `new Bitmap`, bounded —
  3 icons, cached); `Views\MacrosTabControl.axaml.cs:26-29` (`AssetLoader.Open` stream + `XmlTextReader`,
  once per tab); `ClassicAssist.Launcher\ViewModels\ShardsViewModel.cs:181-185` (`Ping` not disposed);
  `ClassicAssist.Launcher\ViewModels\MainViewModel.cs:488` and
  `ClassicAssist.Shared\UI\ViewModels\AboutControlViewModel.cs:222` (`Process.Start` results);
  `AboutControlViewModel.cs:255-261` (new `System.Timers.Timer`s per `OnConnectedEvent`) and
  `Views\AboutControl.axaml.cs:32` (new timer per pointer-enter);
  `Misc\PathToThumbnailValueConverter.cs:37-63` (thumbnail cache keyed `path|width|mtime`, never evicted —
  a large screenshot gallery grows memory indefinitely).

- [ ] **`async void` outside genuine event handlers.**
  `ClassicAssist.Shared\Data\Hotkeys\Commands\ObjectInspector.cs:6` — `public override async void Execute()`
  invoked from `Task.Run` (`HotkeyManager.cs:173`) with **no SynchronizationContext**: an exception after
  the first `await` is unhandled on a threadpool thread → process crash. Unprotected UI-context handlers:
  `ScavengerTabViewModel.cs:195` (`OpenClilocFilter`), `OptionsTabViewModel.cs:270` (`SelectMacroTextColor`),
  `:291` (`SetLanguageOverride`) — note the neighbouring `Toggle*` handlers *do* have try/catch — and
  `HotkeysTabViewModel.cs:495` (`CheckOverwriteHotkey`, invoked from a **property setter** at `:68`, and uses
  `SelectedItem` after the await at `:523`, so it can assign the hotkey to a different entry than the one
  being edited).

- [ ] **`MacrosGump` static 250 ms timer outlives its purpose.**
  `ClassicAssist.Shared\UO\Data\Gumps\MacrosGump.cs:33,132-138` — `Initialize()` (from
  `OptionsTabViewModel.Deserialize:233-236`) starts `new Timer( o => ResendGump(), null, 1000, 250 )`;
  turning the option off only makes `ResendGump` early-return (`:99-104`) — the timer is never stopped or
  disposed except by the next `Initialize()`. It spins 4×/second for the rest of the process lifetime.

- [ ] **`HexDumpControl` "Copy byte[] declaration" does nothing.**
  `ClassicAssist.Avalonia\Controls\HexDumpControl.axaml.cs:22-46` — the handler builds `prepend` then only
  contains a commented-out `IClipboard` block (`:43-45`). Wire it to `Engine.UIInvoker.SetClipboardText`
  (what `DebugPropertiesControl` uses). Also `FindControl<MenuItem>("ContextCopy")` (`:22,48`) results are
  used without null checks.

- [ ] **`IDisposable` never disposed.**
  `ClassicAssist.Shared\UI\ViewModels\Agents\VendorSellTabViewModel.cs:20,27` (implements `IDisposable`,
  unsubscribes `VendorSellDisplayEvent`, but no caller ever disposes it);
  `MacrosTabViewModel._fileScanTimer` (`MacrosTabViewModel.cs:54`);
  `DebugViewModel.Queue` (`DebugViewModel.cs:46`).

- [ ] **`SetProperty` raises `PropertyChanged` unconditionally (no equality check).**
  `DebugViewModel.PacketEnabledEntry.SetProperty` (`DebugViewModel.cs:353-357`) and `Views\HuePickerWindow.axaml.cs`;
  `HuePickerWindow` also hides Avalonia's `PropertyChanged` name with
  `new event PropertyChangedEventHandler PropertyChanged`.

- [ ] **`AvaloniaDispatcher.Invoke` is fire-and-forget `Post` despite the "Invoke" name.**
  `ClassicAssist.Avalonia\AvaloniaDispatcher.cs:35-38` — diverges from WPF's synchronous `Dispatcher.Invoke`.
  Callers that assume synchronous completion now race: `MacrosTabViewModel.cs:46`
  (`SelectedItem.Macro += str` in a posted delegate — `SelectedItem` can be null and the NRE escapes into
  the dispatcher), `MacroEntry.Execute/Stop` setting `IsRunning`, `AssistantOptions.PlayerInitialized`
  swapping profiles (`Data\AssistantOptions.cs:271-277`). Audit callers or provide a genuinely
  synchronous `Invoke` for those that need it.

- [ ] **`AvaloniaUIInvoker.SetClipboardText` drops the clipboard task.**
  `AvaloniaUIInvoker.cs:154-157` — `_dispatcher.InvokeAsync( () => ...SetTextAsync( text ) )` discards the
  inner `Task` (unobserved failures; back-to-back calls can land out of order). Same null-`Task` risk as
  `GetClipboardText`, and in `DialogWindowViewModel.cs:43` (`await ...Clipboard?.SetTextAsync(...)`).

- [ ] **Wait-handle leaks / misleading API.**
  `UO\Commands.cs:232` (`GetTargetInfoAsync` allocates an `AutoResetEvent` per call, never disposed);
  every `*PromptGump` allocates one per prompt (`SelectionPromptGump.cs:60` etc.);
  `Engine.TickWorkQueue` is a plain unlocked `Queue<Action>` (`Engine.cs:805-810`) — no producers found
  today, but it is a race if anything starts enqueueing;
  `Commands.DragDropAsync` (`UO\Commands.cs:42-61`) holds `lock(_dragDropLock)` across
  `Thread.Sleep(ActionDelayMS)` despite being `async Task` — currently has no callers (dead in this port),
  but any future UI-thread caller would freeze the UI per drag.

- [ ] **Singleton `GetInstance()` double-checked locking without `volatile`.**
  `TargetManager.cs:293-311`, `HotkeyManager.cs:96-108`, `OrganizerManager.cs:340-358`, and
  `CountersManager` / `ChatManager` / `ScavengerManager` / `DressManager` / `SkillManager` /
  `SpellManager` / `ScreenshotManager` / `VendorBuyManager`. Benign on the .NET memory model in practice.

- [ ] **Code-behind reaching into controls by name (fragile under Avalonia template lifetimes).**
  `Views\DebugWindow.axaml.cs:43-46,83-108` (settings serialisation in the window),
  `Views\SkillsTabControl.axaml.cs` (sort persistence),
  `Views\Agents\AutolootTabControl.axaml.cs:29-54,79` (`FindControl<TreeView>("treeView")`, drag/drop,
  `async void OnPointerMoved`), `Controls\HexDumpControl.axaml.cs:22,48`,
  `Controls\EditTextBlock.axaml.cs:74-77`, `Views\MacrosTabControl.axaml.cs:21`.
  Works today; breaks silently if names/templates change.

---

## Build & dependencies

- [ ] **`MessagePack` 2.5.302 has a known moderate-severity vulnerability (NU1902, GHSA-qhrr-8q5h-9q3h).**
  Reported 7× across `ClassicAssist.Avalonia`, `ClassicAssist.HeadlessTests`, `ClassicAssist.Plugin`,
  `Tools\PacketTransportBenchmark` — the only warnings in a clean build. Upgrade MessagePack.

- [ ] **`ClassicAssist.Avalonia.csproj:55-57` references a non-existent `AvaloniaEdit.dll`.**
  `<Reference Include="AvaloniaEdit"><HintPath>AvaloniaEdit.dll</HintPath></Reference>` points at
  `ClassicAssist.Avalonia\AvaloniaEdit.dll`, which does not exist on disk, while `Avalonia.AvaloniaEdit`
  11.3.0 is also package-referenced (`:32`). `App.axaml:4`'s
  `StyleInclude Source="avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml"` and `MacrosTabControl.axaml:7`'s
  `clr-namespace:AvaloniaEdit;assembly=AvaloniaEdit` resolve against whichever assembly wins. If the
  `HintPath` was meant to pin a locally patched `AvaloniaEdit.dll`, its absence silently drops the patch —
  **clarify intent, then either commit the DLL or delete the reference.**

- [ ] **AGENTS.md / BUILDING.md contradictions to reconcile** (from `AGENTS.md` itself): a bare
  `dotnet format ClassicAssist.slnx` is *not* safe to commit despite what `BUILDING.md` suggests. Update
  `BUILDING.md` to point at the scoped procedure in `AGENTS.md` so nobody runs the destructive version.

---

## Needs investigation

Unconfirmed claims — triage before actioning.

1. **`ClassicOptions.ReconnectTime` default mismatch** — `ClassicAssist.Launcher\Models\ClassicOptions.cs:25-26`:
   `[ClassicOption("-reconnect_time", ..., DefaultValue = 60)] ... { get; set; } = 60000;`.
   `ReadClassicOptions` unconditionally overwrites the initializer with `attr.DefaultValue` (60) when a
   config exists but lacks the key. Which unit does the client expect — 60 or 60000?
2. **`double.Parse` without culture** — `ClassicAssist.Shared\UI\ViewModels\Agents\VendorBuyTabViewModel.cs:190`
   parses a server-supplied (dot-decimal) weight with the ambient culture; mis-parses on comma-decimal
   locales. Appears upstream-inherited.
3. **`PlaySound` file path** — `Data\Macros\Commands\MainCommands.cs:223` resolves
   `Engine.StartupPath/Sounds/...`; `StartupPath` is the `ui/` folder (`Engine.cs:398`). If `Sounds/` ships
   at the install root, every file-based `PlaySound` reports "Cannot find sound file". Check the release layout.
4. **Owner-null dialogs during startup** — `AvaloniaUIInvoker.InvokeDialog`/`GetHueAsync`/
   `ShowOpenFileDialogAsync` and `AvaloniaMessageBoxProvider.Show` all pass `Engine.MainWindow`, assigned
   only at `App.axaml.cs:55`. Because `IDispatcher.Invoke` here is a *post*, the lambda usually runs after
   assignment — but any synchronous path opening a dialog earlier would hit `ShowDialog(null)` →
   `ArgumentNullException` and a never-completing `TaskCompletionSource`. No concrete trigger found.
5. **`$parent[MenuItem].DataContext.MoveToGroupCommand`** (`AutolootTabControl.axaml:79`) inside a
   `MenuItem`-generated item container — resolves in every scenario traced, but generated-container
   logical-parent edge cases in Avalonia's `MenuItem` popup hosting are subtle. One-click test of
   "Move to group" would settle it.
6. **Reflection-binding write-back of exotic types** — e.g. `OptionsTabControl.axaml:132`
   `TextBox Text="{Binding CurrentOptions.ExpireTargetsMS}"` string↔int and `:124` `NumericUpDown`
   decimal↔int. Conversion is delegated to `TypeUtilities`; culture edge cases not runtime-verified.
7. **Whether Avalonia's `DataGridCheckBoxColumn` under `IsReadOnly=True` fully suppresses toggling or
   merely reverts after click** (see the `CustomPropertiesWindow` item in P3) — needs a UI click-through.
8. **`ClassicAssist` (out-of-scope WPF project) and `ClassicAssist.UITest`** were not audited; the
   `ClassicAssist.Plugin` multi-target `net472`/`net10.0` project was only checked for cross-target drift
   (none found). If the plugin side is in scope, it needs its own pass.
9. **`AssistantOptions.WindowWidth/Height` round-trip** — saved as `double`
   (`AssistantOptions.cs:101-102`) but read back `ToObject<int>()` (`:163-164`); fractional DIP sizes get
   rounded on reload. Harmless, but confirm it is intended.
