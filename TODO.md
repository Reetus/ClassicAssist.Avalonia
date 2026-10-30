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

- [x] **`GetClipboardText()` deadlocks the UI thread.**
  `ClassicAssist.Avalonia\AvaloniaUIInvoker.cs:159-162` — `return _dispatcher.InvokeAsync( () => Engine.MainWindow.Clipboard?.TryGetTextAsync() ).Result;`
  The lambda resolves to `InvokeAsync<TResult>(Func<Task<TResult>>)`, so `.Result` blocks the UI thread
  on work queued to that same thread → waits forever. Only caller today is
  `ClassicAssist.Shared\UI\ViewModels\Agents\AutolootViewModel.cs:445-447` (`ClipboardPaste`, a
  `RelayCommand`, i.e. UI thread) — so it is **latent**, because `ClipboardPasteCommand` is not bound
  in any `.axaml` (the WPF build wired it to Ctrl+V). Fix before re-binding: make the method
  `async Task<string>` end-to-end, or await `Clipboard.TryGetTextAsync()` directly without the
  dispatcher hop. Secondary: `Clipboard?.TryGetTextAsync()` can return a null `Task<string>`
  (no clipboard platform) which faults the operation with NRE.

- [x] **Autoloot copy/paste is silently missing from the UI.**
  Same root: `AutolootViewModel.cs:101-103` defines `ClipboardPasteCommand` / `ClipboardCopyCommand`
  and nothing binds them. Confirm the intended binding (Ctrl+C/V on the autoloot grid) and restore it —
  *after* the deadlock above is fixed.

- [x] **Packet-processing thread can block indefinitely in Scavenger `CheckArea`.**
  `ClassicAssist.Shared\UI\ViewModels\Agents\ScavengerTabViewModel.cs:283-284` —
  `ActionPacketQueue.EnqueueDragDrop(...).Wait()` with no timeout, inside `lock (_scavengeLock)`.
  Reached from `ItemsOnCollectionChanged` (`:215-228`) on the single `IncomingQueue` worker
  (`Engine.cs:427-451`), which also runs `PacketWaitEntries.CheckWait` — so while it is parked, *every*
  wait in the app (autoloot, `WaitForContainerContents`, macro commands) stalls. Can form a wait cycle
  with `ActionPacketQueue` requeue actions that themselves wait for a 0x3C packet only this worker can
  deliver (`ActionPacketQueue.cs:323-343` → `UO\Commands.cs:644-659`). Give the wait a timeout and
  release the lock while waiting.

- [x] **Queue worker loops have no try/catch — one throwing action kills the worker or the process.**
  `ClassicAssist.Shared\Misc\ThreadPriorityQueue.cs:98` (`_onAction( queueItem )`, unguarded in the
  loop — the only try/catch in the file is `ObjectDisposedException` at `:114`/`:138`),
  `ClassicAssist.Shared\Misc\ThreadQueue.cs:46`,
  `ClassicAssist.Shared\UO\Network\ActionPacketQueue.cs:124`. On .NET an unhandled exception on any
  thread terminates the process (`Program.cs:50-51` only logs). Reachable fault: `Engine.SendPacketToClient`
  → `Host.SendPacketToClient(...).Result` (`Engine.cs:1208-1211`) is not exception-wrapped and an RPC
  fault on disconnect escapes. The hazard is already documented in
  `EntityCollectionViewerViewModel.cs:1829-1831`. Wrap each callback invocation in try/catch and log.

- [x] **Launcher: NRE crash on Start with no shard selected.**
  `ClassicAssist.Launcher\ViewModels\MainViewModel.cs:422` — `await Utility.ResolveAddress( SelectedShard.Address )`,
  while `StartCommand.CanExecute` (`:225`) only checks client/data paths. `SelectedShard` is set only
  from config (`:142-150`) or user pick, and Avalonia's ComboBox does **no** auto-selection when only
  `ItemsSource` is bound (`Views\MainWindow.axaml:37-39`). First run (ctor returns early at `:33-36`
  with no `Launcher.json`) or a stale `SelectedShard` name → `SelectedShard == null` → NRE inside
  `async void` `RelayCommandAsync.Execute` → process crash. Add `&& SelectedShard != null` to
  `CanExecute` and guard `Start`.

- [x] **Async-void `ICommand.Execute` turns any command exception into a process crash.**
  `ClassicAssist.Shared\UI\ViewModels\BaseViewModel.cs:174-177` and
  `ClassicAssist.Launcher\ViewModels\BaseViewModel.cs:101-104` — `public async void Execute(...)`,
  no try/catch anywhere in the chain, and no global `DispatcherUnhandledException` handler in
  `App.axaml.cs`. Concrete crash sources: `MainViewModel.Start` (`Process.Start` → `Win32Exception`),
  `Utility.ResolveAddress` (`Dns.GetHostEntryAsync` → `SocketException`; only the null-return path is
  handled), `MainViewModel.CheckPresets`' JSON parse. Either wrap `Execute` in try/catch + user-visible
  error, or register a global handler.

- [x] **Profile save throws "duplicate key" once the Debug window has been opened twice.**
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

- [x] **`AvaloniaMessageBoxProvider.Show` can hang every awaiter forever.**
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

- [x] **`BaseViewModel` roots every ViewModel ever created in a static list.**
  `ClassicAssist.Shared\UI\ViewModels\BaseViewModel.cs:37,50` — `_viewModels.Add( this )` in the ctor,
  no `Remove`/`Clear` anywhere in the repo (verified). `UiHost.cs:41-61` uses it as a service locator for
  profile save/load, so stale instances also participate in settings serialisation (→ the duplicate-key
  crash above). Every dialog/window VM stays pinned (`EntityCollectionViewerViewModel` cleans up in
  `OnClosed` and is rooted anyway). Needs a weak list or explicit deregistration on window close.

- [x] **Debug window VM graph leaks and keeps processing live packet traffic forever.**
  `ClassicAssist.Shared\UI\ViewModels\DebugViewModel.cs:47-50` subscribes
  `Engine.PacketReceivedEvent / PacketSentEvent / InternalPacketReceivedEvent / InternalPacketSentEvent`
  with **no `-=` anywhere** (verified); `Queue = new ThreadQueue<PacketEntry>(...)` (`:46`) is
  `IDisposable` and never disposed. `DebugWindow.OnClosing` (`Views\DebugWindow.axaml.cs:83-108`) only
  serialises settings. Same pattern per window: `Debug\DebugGumpViewModel.cs:21,33` (lambda subscriptions
  to `IncomingPacketHandlers.GumpEvent` / `OutgoingPacketHandlers.GumpEvent`, ungated, append every gump
  forever), `DebugBuffIconsViewModel.cs:19`, `DebugMenusViewModel.cs:39`, `DebugSpecialMovesViewModel.cs:18`.
  If capture is enabled, `Items` grows without bound (`:250-258`). Unsubscribe + dispose on close.

- [x] **`UiHost` settings save/load should guard each provider individually.**
  `ClassicAssist.Avalonia\UiHost.cs:41-47` serialises every `ISettingProvider` reached through
  `BaseViewModel._viewModels` with no per-provider `try/catch`, so one bad provider still aborts the
  whole profile save (and `Options.Save` runs inside MainWindow's `OnClosing`, where an exception is
  fatal). `DebugViewModel` now self-guards, but the loop-level guard belongs here so no future
  provider can repeat the failure. Same for the load/deserialise path.

- [x] **`DebugJournalViewModel` / `DebugVendorBuyViewModel` need the same `IDisposable` teardown.**
  Residual from the Debug window teardown work. `ClassicAssist.Shared\UI\ViewModels\Debug\DebugJournalViewModel.cs:64,69`
  (`IncomingPacketHandlers.JournalEntryAddedEvent`) and `Debug\DebugVendorBuyViewModel.cs:40,44`
  (`VendorBuyDisplayEvent`) subscribe/unsubscribe only inside their `Enabled` setter, which nothing
  calls on window close — so if capture is on at close they keep handling traffic forever. Neither
  class implements `IDisposable` (`: BaseViewModel` only). `DebugWindow.OnClosed` already disposes any
  tab VM that implements `IDisposable`, so adding the interface is all that is needed.

- [x] **Two `MainWindowViewModel`s are constructed; the orphan is pinned by static events.**
  `ClassicAssist.Avalonia\App.axaml.cs:52` sets `DataContext = new MainWindowViewModel()` while
  `Views\MainWindow.axaml:19-21` already declares `<Window.DataContext><viewModels:MainWindowViewModel/></Window.DataContext>`.
  The replaced instance keeps `Engine.UpdateWindowTitleEvent += OnUpdateWindowTitleEvent`
  (`MainWindowViewModel.cs:17`) and an anonymous `AssistantOptions.ProfileChangedEvent` lambda (`:18`)
  that can never be unsubscribed — so every session leaks a VM and does title-update work twice. Remove
  one of the two constructions.

- [x] **`FilterControl` registers a global `AvaloniaProperty` class handler per instance.**
  `ClassicAssist.Avalonia\Controls\FilterControl.axaml.cs:37` —
  `IsFilterVisibleProperty.Changed.AddClassHandler<FilterControl>(...)` inside the **instance**
  constructor. `AvaloniaProperty.Changed` is property-wide static state: one subscription per control
  ever constructed, never removed, and every change invokes all of them. Move to a static constructor.

- [x] **`LockStatusValueConverter` leaks a `Bitmap` and an undisposed asset stream on every conversion.**
  `ClassicAssist.Avalonia\Misc\LockStatusValueConverter.cs:25,28,32` — `return new Bitmap( AssetLoader.Open(...) )`
  with no caching (contrast `PathToThumbnailValueConverter` / `PixmapValueConverter`, which do cache).
  The skills grid re-runs `Convert` on every packet-driven skill update while the tab is open →
  unbounded native bitmap + stream growth. Cache the three brushes/bitmaps in statics and dispose the streams.

- [x] **`AliasCommands` static dictionaries are mutated with no synchronisation.**
  `ClassicAssist.Shared\Data\Macros\Commands\AliasCommands.cs:13` (`_aliases`), `:171` (`_playerAliases`).
  Writers: macro threads (`:73-127`), hotkey dispatch (`HotkeyManager.cs:171`), MCP. Reader/writer on the
  UI thread: `ActiveObjectsViewModel.RefreshPlayerAliases` (`ActiveObjectsViewModel.cs:159-169`,
  `GetAllAliases().ToList()` while a macro mutates). Plain `Dictionary` → torn buckets,
  `IndexOutOfRangeException`, lost aliases. Use `ConcurrentDictionary` or a lock.

- [x] **`ClilocFilter.Filters` / `SoundFilter.Items` / `ItemIDFilter.Items`: static `ObservableCollection`
  mutated on the UI thread while enumerated on packet threads.**
  `ClassicAssist.Shared\Data\Filters\ClilocFilter.cs:16`, enumerated at `:80-134` from
  `IncomingPacketHandlers` on the `IncomingQueue` worker; mutated from
  `ClilocFilterConfigureViewModel.cs:82` on the UI thread → `InvalidOperationException: Collection was
  modified` inside the packet path (swallowed, message dropped). Same shape for
  `AutolootViewModel.CheckItems`' `Filters.Select(...)` (`AutolootViewModel.cs:256`). Snapshot or lock.

- [x] **`AutolootViewModel.OnCorpseEvent` holds `lock` across multi-second blocking waits.**
  `ClassicAssist.Shared\UI\ViewModels\Agents\AutolootViewModel.cs:491-550` — `lock (_autolootLock)`
  around `we.Lock.WaitOne( 2000 )` (`:510`), `Thread.Sleep( 1000 )` (`:522`) and `t.Wait( LOOT_TIMEOUT )`
  (`:548`, 5 s per item). Reachable from `Task.Run` (`IncomingPacketHandlers.cs:711`) **and**
  synchronously from the macro thread (`AgentCommands.cs:122`). Serialises unrelated callers, and
  combined with the Scavenger packet-thread stall the `WaitOne(2000)` can time out and silently miss items.

- [x] **`Engine.SendPacketToServer`/`SendPacketToClient`: rate-limit spin + blocking RPC `.Result` inside
  `lock`, callable from the UI thread.**
  `ClassicAssist.Shared\Engine.cs:611-657` (locks `:613`/`:636`, `Thread.Sleep(1)` spins `:615-618`/`:640-643`,
  RPC `.Result` at `:1203-1211`). UI-thread-reachable via `UOC.SystemMessage` from
  `ScavengerTabViewModel.Insert`, `CountersTabViewModel.Recount`, `OptionsTabViewModel` handlers →
  UI freezes per call; a wedged plugin = indefinite freeze (no timeout on the `.Result`).
  Also one subscriber away from a lock-order inversion with `_autolootLock`.

- [x] **Closing an EntityCollectionViewer freezes the UI thread until the in-flight queue action finishes.**
  `ClassicAssist.Shared\UI\ViewModels\EntityCollectionViewerViewModel.cs:530-538` (`Cleanup` →
  `_threadQueue?.Dispose()`) runs from `OnClosed` on the **UI thread**
  (`Views\EntityCollectionViewer.axaml.cs:60-67`); `ThreadPriorityQueue.StopThread` does
  `_workerThread.Join()` (`ThreadPriorityQueue.cs:131-148`). Queue actions are long loops with per-item
  `WaitOne( 3000 )` retries (`:2069-2121`) — the UI can park for minutes on a large selection.

- [x] **`AboutControlViewModel`: timers leak across reconnects; per-player handlers accumulate.**
  `ClassicAssist.Shared\UI\ViewModels\AboutControlViewModel.cs:250-262` — `OnConnectedEvent` creates new
  `Timer`/`_pingTimer` every connect and never disposes the previous ones; `OnDisconnectedEvent`
  (`:187-192`) stops only `_timer`, so `_pingTimer` keeps sending ping packets while disconnected
  (`PingServer`, `:264-289`, includes `we.Lock.WaitOne( 5000 )`). Also `:140-146` + `:171-178`:
  `OnMobileUpdatedEvent` re-adds `player.LastTargetChangedEvent` / `MobileStatusUpdated` handlers on every
  player mobile update / war-mode packet → duplicate handlers accumulate per session.

- [x] **`Options.CurrentOptions.PropertyChanged` subscription goes stale after profile switch.**
  `ClassicAssist.Shared\UI\ViewModels\BaseViewModel.cs:53` binds to the instance current at construction,
  but `Options.CurrentOptions` is **replaced** on profile change (`Data\AssistantOptions.cs:274`,
  `GeneralControlViewModel.cs:340`, `NewProfileViewModel.cs:57`). Consequence: `[OptionsBinding( Property = "AlwaysOnTop" )]`
  on `MainWindowViewModel` (`:23-28`, bound as `Topmost` in `Views\MainWindow.axaml:14`) stops tracking
  after a linked-profile switch, and every old `Options` instance stays rooted by the accumulated handlers.

- [x] **`BaseViewModel._viewModels` (`List<T>`) is mutated on the UI thread while save/load iterates it
  from RPC threads.** Constructors run on the UI thread (`BaseViewModel.cs:50`); `Options.Save` is called
  from MCP tool handlers on StreamJsonRpc threads (`ClassicAssist.Shared\Mcp\McpTools.cs:314,346,381`)
  and shutdown (`Engine.cs:336`) → `UiHost.OnOptionsSave/OnOptionsLoad` do `[.. _viewModels]`
  (`BaseViewModel.cs:57`) unsynchronised.

- [x] **`PacketWaitRegistry.Push` holds `_pushLock` across a blocking RPC.**
  *Not in the original list — an omission when the audit reports were consolidated; the threading
  audit raised it as Medium. Recorded here for completeness now that it is fixed.*
  `ClassicAssist.Shared\UO\Network\PacketWaitRegistry.cs` held `_pushLock` across `task.Wait( timeout )`,
  so a debounce-timer background push held the lock up to 2 s and `PushNow` (packet thread via
  `RehueList.Add`, macro/filter setup via `Filters.cs`) stalled for that plus its own 500 ms budget.

- [x] **Startup blocks the UI thread on unbounded RPC `.Result` + full data-file load.**
  `ClassicAssist.Avalonia\App.axaml.cs:44-67` → `Engine.InstallRPC` (`Engine.cs:191-244`):
  `GetClientPath().Result`, `GetClientVersion().Result`, `GetWindowHandle().Result`,
  `IsReflectionAvailable().Result` (`:198-201`) have **no timeout**, then `Art/Hues/Cliloc/TileData/Statics/MapInfo.Initialize`
  and `Cliloc.Preload()` (≈1.6 s, per the comment) — all on the UI thread behind the splash. A
  wedged-but-connected plugin leaves the UI blocked forever behind an undismissed splash
  (`SplashWindow` is only closed in the `finally`, `App.axaml.cs:61-66`). StreamJsonRpc has no default
  request timeout — add one.

- [x] **Gump prompts block forever on no response.**
  `SelectionPromptGump.cs:88`, `MessagePromptGump.cs:74`, `ConfirmPromptGump.cs:68`, `ItemArrayGump.cs:170` —
  `AutoResetEvent.WaitOne()` with no timeout; a lost/unanswered gump hangs the calling macro thread
  indefinitely. (Same as upstream WPF.) Add a timeout with a documented failure result.

---

## P2 — port correctness bugs

- [x] **Hue picker "Cancel" silently applies hue 0 — dialog result never observed.**
  `ClassicAssist.Avalonia\AvaloniaUIInvoker.cs:126-136` returns `window.SelectedHue` after `ShowDialog`
  with no OK/Cancel distinction; `HuePickerWindow.SelectedHue` defaults to 0 (`Views\HuePickerWindow.axaml.cs:53-57`).
  Callers write it straight into settings: `FriendsTabViewModel.cs:170-176`
  (`Options.CurrentOptions.RehueFriendsHue = hue; MainCommands.Resync();`) and
  `AutolootViewModel.cs:719-721`. Cancelling overwrites the previous hue with 0. Return a nullable /
  dialog result.

- [x] **`AssistantOptions.SetLanguage` assigns `DefaultThreadCurrentUICulture` twice; `DefaultThreadCurrentCulture` never set.**
  `ClassicAssist.Shared\Data\AssistantOptions.cs:311-312` — the duplicated line is a straight porting
  typo. Only the calling thread gets `CurrentCulture`, so RPC dispatch, macro threads and timers keep the
  OS culture after a language override → divergent numeric/date formatting per thread.

- [x] **`AudioPlayback.Play` disposes the `SoundPlayer` immediately after starting async playback.**
  `ClassicAssist.Shared\Misc\AudioPlayback.cs:32-40` + `:54-64` — `using (SoundPlayer player = ...)` calls
  `player.Play()` (asynchronous) for `sync == false` then disposes it at end of `using`, cutting off or
  preventing playback. Reachable from macro `PlaySound( path, playSync=False )`
  (`Data\Macros\Commands\MainCommands.cs:214-235`). The `PlaySync` path is fine.

- [x] **`ShardsViewModel.GetStatus`: `ReadTimeout` does not apply to `ReadAsync`.**
  `ClassicAssist.Launcher\ViewModels\ShardsViewModel.cs:163-169` — `stream.ReadTimeout = 60000;` then
  `await stream.ReadAsync(...)`. `ReadTimeout` only affects synchronous `Read`; the connect is guarded by
  `Task.WhenAny(..., Task.Delay(5s))` (`:153`) but the read is not, so a server that accepts and goes
  silent hangs that ping task until TCP gives up. Wrap the read in `Task.WhenAny` too.

- [x] **`DialogWindowViewModel.ImagePath` ternary repeats `MessageBoxImage.Warning` — the Information
  icon can never render.** `ClassicAssist.Avalonia\ViewModels\DialogWindowViewModel.cs:23-26` — third
  branch must be `MessageBoxImage.Information`; `dialog-info.png` is unreachable and the image area
  renders empty. Porting typo.

- [x] **`ConfirmDeleteBehaviour` leaves the button stuck on `PendingIcon`.**
  `ClassicAssist.Avalonia\Misc\ConfirmDeleteBehaviour.cs:94-106` — the confirm branch cancels the 2-second
  icon revert (`:80-92`) but never restores `image.Source = originalIcon`; if the row survives (or
  `_deleteCommand.CanExecute` is false) the icon stays "armed" forever.

- [x] **`MultiValueSelector.Values` styled property shares one default `ObservableCollection` across all
  controls.** `ClassicAssist.Avalonia\Controls\MultiValueSelector.axaml.cs:44-46` —
  `Register<MultiValueSelector, ObservableCollection<int>>( nameof( Values ), [] )` creates the default
  once; `Values ??= []` in `OnTextBoxKeyDown` (`:186`) sees the shared non-null default and mutates it,
  leaking values between unbound controls.

- [x] **`MessagePromptGump.OnResponse` indexes `textEntries[0]` without a count check.**
  `ClassicAssist.Shared\UO\Data\Gumps\MessagePromptGump.cs:57` — `textEntries?[0].Value` throws
  `ArgumentOutOfRangeException` for a non-null empty list (malformed/cancelled response), escaping into
  the packet response path. `SelectionPromptGump.cs:68-71` guards its `switches[0]` correctly — make these
  consistent.

- [x] **`ClilocEditTextBlock.OnTargetClick` maps "no properties" to cliloc 0 instead of "Any" (-1).**
  `ClassicAssist.Avalonia\Controls\ClilocEditTextBlock.axaml.cs:104` — `?? -1` only covers a null
  `item`/`Properties`; an empty `Properties` list yields `FirstOrDefault() == 0`, an invalid cliloc.

- [x] **Zip-slip in updater module extraction.**
  `ClassicAssist.Updater\ViewModels\MainViewModel.cs:216-220` + `:230-248` — `entry.FullName.Split('/')`
  segments are `Path.Combine`d without rejecting `..` or rooted segments, so a crafted `Modules.zip` entry
  writes outside `Modules/`. Packages come from GitHub releases (moderately trusted) — hardening gap.

- [x] **Updater accepts `.tar.gz` assets but extracts with `ZipFile`.**
  *Note:* the real path is `ClassicAssist.Updater\Services\PlatformPackage.cs` (not
  `ClassicAssist.Updater\PlatformPackage.cs` as originally written). It needed no change — with tar
  supported end-to-end, its `IsArchive` acceptance is now truthful.

- [x] **Updater extraction needs unit tests.**
  *Also corrected a false claim in the `b7efd6bf` commit message.* It said the `.. ` / `...`
  Win32 trailing-dot/space spoofs "normalise to a parent and fail the prefix test". Measured on
  .NET 10 Windows, they collapse to **the root itself** (`root\`), which trivially passes
  `StartsWith( root + \ )` — so `TryResolveEntryPath` **accepted** them. The write then threw
  mid-loop and aborted the extraction. The final gate now also rejects names resolving to exactly
  the root (`destinationFull.Length <= prefix.Length`), which no legitimate entry does.
  The zip-slip rejection table and the tar.gz round-trip were verified with a scratch harness against
  the compiled assembly (traversal names, rooted paths, backslash separators, `.. ` / `...`
  Win32-normalisation spoofs, symlink/fifo entries) but `ClassicAssist.Tests\UpdaterTests.cs` was
  outside that work's ownership. Add: a traversal-rejection table for `TryResolveEntryPath`, and a
  tar.gz round-trip covering a good file plus hostile and non-regular entries.
  `PlatformPackage.cs:118-122` treats `.tar.gz` as a package; `MainViewModel.ExtractPackage` (`:466`) only
  reads zip. A release shipping only `.tar.gz` fails at extraction. Either add tar support or filter the
  asset list.

- [x] **Launcher config save can throw out of `OnClosing`; save is non-atomic.**
  `ClassicAssist.Launcher\Views\MainWindow.axaml.cs:19-27` calls `ClosingCommand.Execute(null)` directly;
  `MainViewModel.Closing` (`MainViewModel.cs:643`) writes `new StreamWriter(...)` with no try/catch — an
  IO error (read-only install dir) throws out of window-close, and a mid-write crash truncates
  `Launcher.json` (no temp-file + rename).

- [x] **`AvaloniaUIInvoker.FindWindowType` scans the assembly with `GetTypes()` per call, on the caller's
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

- **Undisposed resources.**
  - [x] `ClassicAssist.Avalonia\Misc\StringToResourceConverter.cs:26` — resource stream per `new Bitmap` (bounded: 3 icons, cached).
  - [x] `ClassicAssist.Avalonia\Views\MacrosTabControl.axaml.cs:26-29` — `AssetLoader.Open` stream + `XmlTextReader`, once per tab.
  - [x] `ClassicAssist.Launcher\ViewModels\ShardsViewModel.cs:181-185` — `Ping` not disposed.
  - [x] `ClassicAssist.Launcher\ViewModels\MainViewModel.cs:488` — `Process.Start` result (disposed via `using`; disposal releases only our handle and never terminates the launched client).
  - [x] `ClassicAssist.Shared\UI\ViewModels\AboutControlViewModel.cs:222` — `Process.Start` result.
  - [x] `ClassicAssist.Shared\UI\ViewModels\AboutControlViewModel.cs:255-261` (new `System.Timers.Timer`s per `OnConnectedEvent`) and `ClassicAssist.Avalonia\Views\AboutControl.axaml.cs:32` (new timer per pointer-enter).
  - [x] `ClassicAssist.Avalonia\Misc\PathToThumbnailValueConverter.cs:37-63` - thumbnail cache keyed `path|width|mtime`, never evicted; a large screenshot gallery grows memory indefinitely.

- [x] **`async void` outside genuine event handlers.**
  `ClassicAssist.Shared\Data\Hotkeys\Commands\ObjectInspector.cs:6` — `public override async void Execute()`
  invoked from `Task.Run` (`HotkeyManager.cs:173`) with **no SynchronizationContext**: an exception after
  the first `await` is unhandled on a threadpool thread → process crash. Unprotected UI-context handlers:
  `ScavengerTabViewModel.cs:195` (`OpenClilocFilter`), `OptionsTabViewModel.cs:270` (`SelectMacroTextColor`),
  `:291` (`SetLanguageOverride`) — note the neighbouring `Toggle*` handlers *do* have try/catch — and
  `HotkeysTabViewModel.cs:495` (`CheckOverwriteHotkey`, invoked from a **property setter** at `:68`, and uses
  `SelectedItem` after the await at `:523`, so it can assign the hotkey to a different entry than the one
  being edited).

- [x] **`MacrosGump` static 250 ms timer outlives its purpose.**
  `ClassicAssist.Shared\UO\Data\Gumps\MacrosGump.cs:33,132-138` — `Initialize()` (from
  `OptionsTabViewModel.Deserialize:233-236`) starts `new Timer( o => ResendGump(), null, 1000, 250 )`;
  turning the option off only makes `ResendGump` early-return (`:99-104`) — the timer is never stopped or
  disposed except by the next `Initialize()`. It spins 4×/second for the rest of the process lifetime.

- [x] **`HexDumpControl` "Copy byte[] declaration" does nothing.**
  `ClassicAssist.Avalonia\Controls\HexDumpControl.axaml.cs:22-46` — the handler builds `prepend` then only
  contains a commented-out `IClipboard` block (`:43-45`). Wire it to `Engine.UIInvoker.SetClipboardText`
  (what `DebugPropertiesControl` uses). Also `FindControl<MenuItem>("ContextCopy")` (`:22,48`) results are
  used without null checks.

- [x] **`IDisposable` never disposed.**
  `ClassicAssist.Shared\UI\ViewModels\Agents\VendorSellTabViewModel.cs:20,27` (implements `IDisposable`,
  unsubscribes `VendorSellDisplayEvent`, but no caller ever disposes it);
  `MacrosTabViewModel._fileScanTimer` (`MacrosTabViewModel.cs:54`);
  `DebugViewModel.Queue` (`DebugViewModel.cs:46`).

- **`SetProperty` raises `PropertyChanged` unconditionally (no equality check).**
  - [x] `ClassicAssist.Shared\UI\ViewModels\DebugViewModel.cs` (`PacketEnabledEntry.SetProperty`) — now
    returns early on equality; `EnableDisable` previously fired 256 notifications per click.
  - [x] `ClassicAssist.Avalonia\Views\HuePickerWindow.axaml.cs` — same unconditional `SetProperty`; it
    also hides Avalonia's `PropertyChanged` name with `new event PropertyChangedEventHandler PropertyChanged`.

- [x] **`AvaloniaDispatcher.Invoke` is fire-and-forget `Post` despite the "Invoke" name.**
  *Naming/docs half done:* `Invoke` now documents that it posts and is not WPF's synchronous
  `Dispatcher.Invoke`, and `AvaloniaDispatcher` gained a genuinely synchronous `InvokeSynchronous`
  (class-only — `IDispatcher` lives in `ClassicAssist.Shared` and cannot be changed here). The
  caller-side races are tracked separately below.

- [x] **`IDispatcher.Invoke` call sites that assume synchronous completion.**
  *Two residues deliberately left and documented in code:* (a) `AssistantOptions.PlayerInitialized`
  cannot be made locally correct — the block must run on the UI thread (its providers serialise
  UI-bound collections), blocking the packet thread would violate the documented
  "the caller may be a packet handler the game thread is waiting on" invariant, and splitting the
  block would break its atomicity against UI-thread manual profile switches. It needs a
  continuation-based completion signal instead of a post. (b) a narrow supersede window in
  `MacroEntry.Execute` (~2-4 s if a previous run fails to die within the bounded joins) — closing it
  needs run identity on `StoppedEvent`, which is an interface change.
  The `Invoke`→`Post` divergence is documented now, but these callers still race because they read or
  gate state that only changes inside the posted delegate. Enumerated by the dispatcher work; files
  were owned by other work that round.
  1. `ClassicAssist.Shared\Data\Macros\MacroEntry.cs:343` — `Execute()` posts `IsRunning = true` then
     starts the macro; `Stop()` (`:356`) gates on `IsRunning`, so a prompt `Stop()` silently does
     nothing and callers cannot observe `IsRunning` after `Execute` returns.
  2. `MacroEntry.cs:298-306` (`OnStoppedEvent`) vs `:280-287` (`OnPausedEvent`) — the stop path reads
     `IsPaused`/`AutoResetEvent` that the pause path only sets inside a posted delegate, so a stop
     racing a pause skips `AutoResetEvent?.Set()` and the debugger's wait handle is never released.
  3. `MacroEntry.cs:359` / `:467` — posted `IsRunning = false` / `IsPaused = false`; UI state briefly
     stale after `Stop()`/`Resume()` (same family, milder).
  4. `ClassicAssist.Shared\Data\AssistantOptions.cs:271-277` — `PlayerInitialized` posts the whole
     `Options.Save` / `CurrentOptions` swap / `Options.Load` / `ProfileChangedEvent`; the RPC thread
     continues immediately, so the rest of the connection flow reads the old profile.
  5. `ClassicAssist.Shared\UI\ViewModels\SkillsTabViewModel.cs:249-272` + `:288-306` —
     `OnSkillsListEvent` posts `Items.Clear()` + per-item `Add` while `OnSkillUpdatedEvent` (on the
     same packet worker) searches `Items` and mutates the *old* entry the in-flight rebuild discards,
     so the rebuilt entry never receives the update.
  Adjacent/mild: `ScreenshotTabViewModel.cs:517-525` (watcher thread reads `Screenshots` before posting
  `Remove`), `GeneralControlViewModel.cs:265` (safe via FIFO ordering today, fragile).
  Also: `InvokeSynchronous` is only reachable through the concrete `AvaloniaDispatcher`;
  `Engine.Dispatcher` is typed `IDispatcher` in `ClassicAssist.Shared`, so Shared call sites need a
  cast or an interface member (plus `InlineDispatcher`) before they can use it.
  `ClassicAssist.Avalonia\AvaloniaDispatcher.cs:35-38` — diverges from WPF's synchronous `Dispatcher.Invoke`.
  Callers that assume synchronous completion now race: `MacrosTabViewModel.cs:46`
  (`SelectedItem.Macro += str` in a posted delegate — `SelectedItem` can be null and the NRE escapes into
  the dispatcher), `MacroEntry.Execute/Stop` setting `IsRunning`, `AssistantOptions.PlayerInitialized`
  swapping profiles (`Data\AssistantOptions.cs:271-277`). Audit callers or provide a genuinely
  synchronous `Invoke` for those that need it.

- [x] **`AvaloniaUIInvoker.SetClipboardText` drops the clipboard task.**
  `AvaloniaUIInvoker.cs:154-157` — `_dispatcher.InvokeAsync( () => ...SetTextAsync( text ) )` discards the
  inner `Task` (unobserved failures; back-to-back calls can land out of order). Same null-`Task` risk as
  `GetClipboardText`, and in `DialogWindowViewModel.cs:43` (`await ...Clipboard?.SetTextAsync(...)`).

- [x] **Wait-handle leaks / misleading API.**
  `UO\Commands.cs:232` (`GetTargetInfoAsync` allocates an `AutoResetEvent` per call, never disposed);
  every `*PromptGump` allocates one per prompt (`SelectionPromptGump.cs:60` etc.);
  `Engine.TickWorkQueue` is a plain unlocked `Queue<Action>` (`Engine.cs:805-810`) — no producers found
  today, but it is a race if anything starts enqueueing;
  `Commands.DragDropAsync` (`UO\Commands.cs:42-61`) holds `lock(_dragDropLock)` across
  `Thread.Sleep(ActionDelayMS)` despite being `async Task` — currently has no callers (dead in this port),
  but any future UI-thread caller would freeze the UI per drag.

- [x] **Singleton `GetInstance()` double-checked locking without `volatile`.**
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

- [x] **Single-instance DebugWindow drops the Object Inspector's pre-navigation.**
  `ClassicAssist.Shared\UI\ViewModels\ObjectInspectorViewModel.cs:437` opens `DebugWindow` with ctor
  params (`[typeof( DebugPropertiesViewModel ), properties]`, via the `DebugWindow( Type, object )`
  ctor at `Views\DebugWindow.axaml.cs:58`) to pre-select the Properties tab. The new single-instance
  reuse in `AvaloniaUIInvoker.Invoke` activates an already-open window and discards those params, so
  "Inspect object" while the Debug window is open just focuses it instead of navigating. Needs a
  public "select tab + populate" API on `DebugWindow`.

- [x] **`DialogWindowViewModel` awaits a possibly-null clipboard task.**
  `ClassicAssist.Avalonia\ViewModels\DialogWindowViewModel.cs:43` —
  `await ( (Window) _window.Window ).Clipboard?.SetTextAsync( ... )` NREs when there is no platform
  clipboard. Same null-`Task` shape as the `SetClipboardText` bug now fixed in `AvaloniaUIInvoker`;
  this file was outside that work's ownership.

- [ ] **`LockedObservableCollection` / `LockedDictionary` `CopyTo` can leave a `null` tail after a concurrent shrink.**
  Applies to both snapshot-collection types (`ClilocFilter.cs` and `MacroEntry.cs`), which share the
  design.
  Empirically confirmed (scratch probe against the same design): `Enumerable.ToArray` sizes its
  buffer from `ICollection<T>.Count` and then calls `CopyTo` in a *separate* call. Growth between the
  two is handled (the bounded copy avoids `ArgumentException`), but **shrink** leaves the tail as
  `default(T)`, so `foreach ( var f in Filters.ToArray() ) { f.Cliloc }` NREs. `ClilocFilter.Serialize`
  (`Filters.ToArray()` on RPC threads while the UI edits the list) and
  `IncomingPacketFilters.cs:92` are exposed.
  Narrow window and strictly rarer than the `InvalidOperationException` it replaces, and both paths
  are caught upstream (`ProcessIncomingQueue`'s catch; and the per-provider `UiHost` guard) — but it
  should not rely on that. Recommended fix: give the class an explicit `Snapshot()` returning
  `List<T>` and migrate the racing call sites off `ToArray()`, or make `CopyTo` pad the tail from the
  snapshot so no slot is ever `default(T)`. Note the two calls cannot be made atomic — `Buffer<T>`
  assumes a non-concurrent collection.

- [x] **`MacroEntry.Aliases` has the same unsynchronised-dictionary race.**
  Per-macro plain `Dictionary` mutated by `SetMacroAlias`/`UnsetAlias` and read by
  `ActiveObjectsViewModel.RefreshInstanceAliases`. Same shape as the `AliasCommands` item now fixed,
  but the type and call sites live in `MacroEntry.cs` / `MacrosTabViewModel.cs`, outside that work's
  ownership. (Raised by the static-state work.)

- [ ] **`MainWindow` never disposes its tab view models.**
  `VendorSellTabViewModel` and `MacrosTabViewModel` now have safe, idempotent `Dispose`
  implementations (VendorSell unsubscribes `VendorSellDisplayEvent`; Macros stops/disposes
  `_fileScanTimer` and detaches its `Engine.Connected`/`DisconnectedEvent` handlers — both of which
  also leaked), but nothing calls them: both VMs are constructed in XAML
  (`Views\MacrosTabControl.axaml:20`, `Views\Agents\VendorSellTabControl.axaml:11`) and hosted by
  `MainWindow`, which only closes at app shutdown. Needed fix: walk the tab controls on window close
  and `Dispose()` every `DataContext is IDisposable`, exactly as `DebugWindow.OnClosed`
  (`Views\DebugWindow.axaml.cs:126-151`) already does. Low urgency — these are app-lifetime views —
  but the wiring is what makes the `IDisposable` implementations meaningful.

## Build & dependencies

- [ ] **`MessagePack` 2.5.302 has a known moderate-severity vulnerability (NU1902, GHSA-qhrr-8q5h-9q3h).**
  *Decision recorded: to be done as a **focused follow-up**, not in parallel with other work.* It
  touches the MessagePack RPC contract shared between the UI process and the plugin across four
  projects, including `ClassicAssist.Plugin` (multi-targets `net472` + `net10.0`), so it needs the
  RPC boundary verified end-to-end in isolation.
  Reported 7× across `ClassicAssist.Avalonia`, `ClassicAssist.HeadlessTests`, `ClassicAssist.Plugin`,
  `Tools\PacketTransportBenchmark` — the only warnings in a clean build. Upgrade MessagePack.

- [x] **`ClassicAssist.Avalonia.csproj:55-57` references a non-existent `AvaloniaEdit.dll`.**
  `<Reference Include="AvaloniaEdit"><HintPath>AvaloniaEdit.dll</HintPath></Reference>` points at
  `ClassicAssist.Avalonia\AvaloniaEdit.dll`, which does not exist on disk, while `Avalonia.AvaloniaEdit`
  11.3.0 is also package-referenced (`:32`). `App.axaml:4`'s
  `StyleInclude Source="avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml"` and `MacrosTabControl.axaml:7`'s
  `clr-namespace:AvaloniaEdit;assembly=AvaloniaEdit` resolve against whichever assembly wins. If the
  `HintPath` was meant to pin a locally patched `AvaloniaEdit.dll`, its absence silently drops the patch —
  **clarify intent, then either commit the DLL or delete the reference.**

- [x] **AGENTS.md / BUILDING.md contradictions to reconcile** (from `AGENTS.md` itself): a bare
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
