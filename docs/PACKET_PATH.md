# Packet path: wait rules and batching

How a packet gets from the game client to the UI process, why that used to stall the client, and
how the plugin now decides which packets it has to wait for.

## The problem

The client calls the plugin's packet filter for every packet it sends or receives, and the client's
own thread blocks inside that call (`PluginEngine.Filter`) until the UI process answers over the
pipe. The UI needs to answer only for packets it might drop or rewrite (filters), but it was
being asked about all of them.

Each round trip is cheap: about 120-135 µs on MessagePack across processes, even when the UI side
does nothing but copy the packet. The server sends in bursts, though. Walking into a busy housing
area delivers hundreds of `0xF3` (world item), `0xDC` (OPL revision) and `0xD6` (properties)
packets at once, up to 1,863 in one burst in the capture below. The client handles them back to back
on one thread, so it is held up for the sum of their round trips: 545 ms for that burst.

A single packet's round trip always looked fine. The cost only shows when you add up a burst, which
is why earlier per-packet diagnostics (`PacketDiagnostics`, `PacketHandlerTiming`) found nothing
wrong.

## How it works now

The UI tells the plugin which packets it needs to see before they reach the client: its **wait
rules**. For any other packet the plugin doesn't ask. It lets the packet through immediately and
queues a copy in a **batch**, which reaches the UI as a single one-way message.

```
client thread ──► Filter(packet)
                   ├─ MustWait?  yes ──► flush batch ──► OnPacketReceive/Send (blocking, as before)
                   └─            no  ──► append to batch, return "pass" at once
OnTick / batch full / disconnect ──► flush batch ──► OnPacketBatch (one-way)
```

### Wait rules

`PacketWaitRule` (in `ClassicAssist.Plugin.Shared/PacketWaitRules.cs`) is a packet id, a direction
and optional byte conditions, with the same shape as the UI's own `PacketFilterCondition`. Rules can
be written as patterns:

| Pattern | Meaning |
|---|---|
| `65` | every incoming `0x65` |
| `1D ?? ?? ?? ?? FF` | incoming `0x1D` whose byte 5 is `0xFF` |
| `BF ?? ?? 00 08` | incoming `0xBF` with sub-command `0x0008` |

`??`, `..` and `*` match any byte. `PacketWaitRule.FromPattern( pattern, outgoing )` parses one, and
`ToString()` prints it back with a `<` (incoming) or `>` (outgoing) prefix.

The plugin compiles the set into `PacketWaitRuleSet`: one table entry per packet id and direction,
so the check costs a single lookup for most packets. Matching **errs towards waiting**. Waiting
when it wasn't needed only costs a round trip, while batching a packet the UI meant to change
silently breaks that filter. So:

- a condition rules a packet out only if it is in range, not negated, and its bytes differ;
- `Negate` never narrows a rule, because the UI's `PacketFilter.MatchFilter` ignores it too;
- an unconditional rule for an id covers every packet with that id.

Until the UI sends its first set, the plugin uses `PacketWaitRuleSet.WaitForEverything`, which is
the old behaviour. That is also what an older UI, or one that never sends rules, gets.

### Batches

`PacketBatchWriter` (`PacketBatch.cs`) packs packets into one blob. Each entry is a flags byte (bit
0 = outgoing), a little-endian int32 length, then the packet bytes. The plugin copies a batched packet
straight from the client's buffer into the batch, with no intermediate array.

The plugin flushes the batch as `IPluginMethods.OnPacketBatch( byte[] packed )`:

- before every packet it has to wait for, so the UI always sees packets in client order;
- on each `OnTick`, after the plugin's own tick work queue and before the UI's `OnTick`, so batched
  packets reach the UI within a client frame;
- when the batch reaches 64 KB or 512 packets;
- on `OnDisconnected` and `OnClientClosing`. On `Detach` (the UI went away) it discards the
  batch instead and resets the rules to wait-for-everything.

The UI unpacks the batch and feeds each packet through the same `Engine.OnPacketReceive` /
`OnPacketSend` path as before, so handlers, journal, wait entries and so on see exactly what they
did. The only difference is that the client has already been given the packet. If a filter matches
a batched packet anyway, that's a gap in the rules: the UI logs it once per packet id.

### Ordering

The plugin sends batches and blocking calls in the order the client produced them, but StreamJsonRpc
would otherwise dispatch incoming calls concurrently on the thread pool. The UI's `JsonRpc` (both
the MessagePack pipe and the JSON/TCP path in `ClassicAssist.Avalonia/Program.cs`) therefore runs
with a `NonConcurrentSynchronizationContext`, which processes calls one at a time in arrival order.
This costs nothing: the client was already waiting on each call in turn.

### Where the rules come from

`PacketWaitRegistry` (UI side) rebuilds the full set whenever one of its sources changes and pushes
it with `IHostMethods.SetPacketWaitRules`. That call returns only once the plugin has switched to
the new set. Runtime filters added through `Engine`'s API wait for it, so a macro's filter is in
force before the macro moves on. Option changes are coalesced.

Changes are coalesced (about 20 ms). State with no change event of its own, such as profile
switches, the friends list, or edits inside the item-ID and sound filter lists, is picked up by a
check every 250 ms, which pushes only if the set actually changed. If building the set fails, the
registry pushes "wait on everything" rather than leave the plugin on a set that might be too narrow.

| Source | Waits on | While |
|---|---|---|
| Runtime filters (`Engine.AddReceiveFilter`, `AddSendPreFilter`, `AddSendPostFilter`) | the filter's id: one rule per condition, because `PacketFilter.MatchFilterAll` claims a packet when any condition holds; every packet with the id if it has no conditions | registered |
| `IncomingPacketFilters` `1C` | all `0x1C` | repeated-messages filter on |
| `IncomingPacketFilters` `C1`, `CC` | all, or with only the cliloc filter on, just the filtered cliloc numbers (offset 14) | either filter on |
| `IncomingPacketFilters` `20`, `77`, `78`, `F3` | the rehued serials (offset 1, 1, 3, 4), plus friends when friends are rehued | rehue list non-empty |
| `OutgoingPacketFilters` `05`, `06`, `80`, `91` | always | always (rare client requests) |
| Speech commands, outgoing `03`, `AD` | always | always (a line is only known to be a command from its text) |
| Weather | `65` | enabled |
| Light level | `4E`, `4F` | enabled |
| Season | `BC`, and `BF` sub-command `0x08` (map change, so the season is resent before the client moves on) | enabled |
| Sound | `54` with each enabled sound id (offset 2), or all `54` past 256 ids | enabled |
| Item ID | `F3`, `25`, `1A` with each enabled source id (offsets 8, 5, 7), and all `3C` (container contents, where items sit at varying offsets) | enabled with enabled entries |
| Any other `DynamicFilterEntry` | every incoming packet | enabled |

The last row is the default `GetWaitRules()`: a new option filter that doesn't override it makes
the plugin wait on everything while it's on, which is slow but correct. Every filter that exists
today overrides it.

The `JsonRpc` dispatches incoming calls one at a time, so nothing it dispatches may block on a later
incoming call. Hotkey actions already run through `Task.Run`. `OnTick` used to do its work inline on
the dispatch thread and now hands it to a tick thread of its own (`Engine.QueueTick`, coalesced), so
a slow tick can't delay the next packet the client is waiting on.

### Limits

- **Rule changes take effect from the next packet.** A packet the plugin has already checked
  against the old set can still go out in a batch. Toggling a filter option can therefore let one
  client frame's worth of matching packets through unfiltered. Awaiting the push closes this for
  macros.
- **Batched packets can only be observed.** Anything that needs to drop or rewrite a packet has to
  produce a wait rule, or it won't work.
- **Plugin and UI must ship together.** The new contract members (`OnPacketBatch`,
  `SetPacketWaitRules`) are on both builds of the plugin, net10.0 and the net472 build the Mono
  TazUO loads.

## Measuring it

`Tools/PacketTransportBenchmark` replays a real session captured by CACL (`--packet-log
--packet-log-relay`; format in CACL's `docs/packet-log.md`). It sends the client-leg packets through
the real pipe and MessagePack transport, in the bursts they arrived in, and reports how long the
client thread is blocked:

```
PacketTransportBenchmark replay <log.jsonl> [fast] [rules=default|<file>]
```

`fast` skips the idle gaps between bursts. `rules=` decides per packet with `PacketWaitRuleSet`
exactly as the plugin does; a rules file holds one pattern per line, `>` for outgoing, `#` for
comments. `contracts` round-trips every RPC member over the production formatter.

Results from a 97 s session in a busy housing area, 64,988 client-leg packets, UI side reduced to copy and
compare (so these are the floor; the real UI adds its own work to every waited call):

| Plugin behaviour | Client time blocked | Bursts longer than one 60 fps frame | Worst burst |
|---|---|---|---|
| Wait on every packet (before) | 12.0% | 229 | 263 ms (1,863 `DC`/`F3`) |
| Default-profile wait rules, everything else batched | 0.36% | 1 | 35 ms |

Both rows are at the original pacing. The default-profile rules are `65 4E 4F` incoming and
`05 06 80 91 03 AD` outgoing: weather and light level on, the built-in outgoing filters, and speech.
With them, almost every packet is batched, and the median per-packet cost on the client's thread
drops from about 150 µs to 0.1 µs.

The `fast` mode replays the whole session back to back, which buries the UI under batches. That's
far more than real traffic ever delivers, so its worst waited call (about 190 ms, queued behind the
backlog) is a stress result, not a latency to expect in play.
