# SignalsLink.Testing — persistent rigs

Optional server-side integration test mod for Vintage Story 1.22. Requires Signals and the current SignalsLink build. Use a creative test world. Commands require creative mode and `controlserver`; normal operation does not require a player nearby.

## Build and launch

```powershell
dotnet build SignalsLink.Testing/SignalsLink.Testing.csproj -p:SkipPublishCopy=true
```

This does not copy anything into Publish. Build/publish SignalsLink normally yourself so that the game loads the two new chest classes and assets. Set SignalsLink.Testing as the Visual Studio startup project and select **Debug SignalsLink Testing**. The existing profile opens **signalsLink testing** and adds the testing build alongside SignalsLink from Publish.

For manual installation, put only SignalsLink.Testing.dll and its modinfo.json in a separate mod folder. No testing mod is required on clients. The two chests belong to the main SignalsLink mod and appear in the creative inventory; neither has a recipe.

## Commands

| Command | Result |
|---|---|
| `/sltest add chute-basic` | Builds another persistent rig near the player. |
| `/sltest run chute-basic` | Same as add. |
| `/sltest run` | Creates the first rig if none exist; otherwise starts all saved rigs. |
| `/sltest start [id\|all]` | Resumes existing rigs without rebuilding or reseeding them. |
| `/sltest stop [id\|all]` | Switches rigs off; all blocks, wires and inventories remain. `cancel` is an alias. |
| `/sltest status [id]` | Shows state, successful cycles, failure transitions, stock counters, pins and position. |
| `/sltest results [id]` | Same on-demand summary; no client dialog is needed. |
| `/sltest list` | Lists available rig types. |
| `/sltest upgrade` | Preflights all registered rigs, then rebuilds all at their saved positions with current layout; resets inventory and counters, retains IDs, starts all. |
| `/sltest clean id` | Explicitly removes one stopped rig's known blocks/wires, preserving terrain and modified/foreign devices. |

Each rig gets an ID such as `chute-basic-1`. Move to another clear area to add another rig. There is no one-rig concurrency limit. There are no unsolicited chat messages, including cycle success/failure notifications.

## Placement and persistence

Stand near a clear, level **9 × 5** patch of solid ground with **7 blocks of free height**. The builder searches for ground four blocks to your +X. It does not level terrain or build a platform. The rig is a vertical stack from recycler on the ground through drain, ordinary target chest, tested chute, ordinary source chest, filler, to bottomless supply. Three switches, a power source and an output terminal occupy the ground around it.

Rigs remain in the world after a cycle, an assertion failure, stop, disconnect or server shutdown. Enabled state, IDs, counters and positions are saved in the world. On reload, existing devices and wires are reused and monitoring begins recovery by draining leftover batches through the real chutes, followed by a fresh cycle; no inventories are reset. Multiple rigs run concurrently. Only the main dimension is currently supported.

Active rig columns are loaded incrementally and kept loaded so assemblies continue working away from players. Singleplayer pause still pauses simulation. For compatibility with other mods' chunk pins, test columns stay loaded until the server session ends even after stop/clean; disabled/removed rigs do not pin them again on the next load. No chunks are forcibly unloaded or discarded.

### Upgrade existing rigs

Run **`/sltest upgrade`** once after installing the new build. This applies to **all registered rigs**, including current rigs: IDs and origins stay, inventories and result counters reset, all rebuilt rigs start. No separate stop/clean/add is necessary. Keep the 9 × 5 area clear up to height 7 and stay outside the footprint.

Before stopping or deleting anything, the command checks every replacement area, terrain, chunks, assets, entities and foreign connections. An obstruction aborts the entire preflight with no changes. Clear the reported obstruction and retry. If chunks are unloaded, the command queues their loading and asks you to repeat `/sltest upgrade` shortly; no blocks are changed during that first call. Old v2 rigs are stopped when loaded and marked `upgrade-required`; they are never rebuilt automatically. The save uses the existing manifest key and supports both layout versions. Unregistered disposable v1 blocks are outside the command's scope.

Construction errors after preflight leave registered rigs stopped for inspection/retry; upgrade is not a transactional world backup. Do not store personal items in rig chests: upgrade deliberately removes their contents. `/sltest clean id` remains available for removing one stopped rig without replacement.

## Continuous chute-basic cycle

Bottomless supply → filling chute → **ordinary input chest** → tested chute → **ordinary output chest** → draining chute → recycler. All three chutes are operated through real Signals knife switches; the harness observes inventories and network nodes and never invokes transfer callbacks to run the rig.

The tested paper is:

```text
game:firewood
amount 8

in target
game:firewood 8+
output 9
```

The filler uses `game:firewood / in target / game:firewood 0 / amount 8` (each slash is a newline), so a continuously enabled filler supplies only one batch into an empty input. The drain uses `game:firewood / amount 8`. Supply templates remain 4 and 12 firewood; ordinary test chests do not replenish or delete anything.

Each cycle fills exactly eight, switches filling off, observes input 0 and output 0 with source=8/target=0 for three stable seconds, enables the tested chute, requires source=0/target=8 and input 15, disables it, observes output 9 for three stable seconds, drains the output through the helper, then observes empty chests and output 0 for three stable seconds. A successful cycle additionally requires exactly eight supplied and eight received by the recycler. Both output states must actually be observed; a fixed delay alone cannot pass. The test does not prove within-tick ordering or transients between observations.

A phase that fails to complete within 60 active simulation seconds reports failure and enters recovery. A full recycler during recovery/draining instead reports waiting and resumes when space becomes available; its configuration does not alter the test chest until the drain is enabled. On reload/start, recovery drains existing complete batches through actual devices before establishing new counter baselines. No inventory clearing or reseeding occurs during cycles. Changed papers/templates stop the rig for inspection. A missing or partial batch can prevent recovery and is reported rather than silently discarded.

Status includes layout version and phase. No automatic chat messages are sent; successful cycles only update counters, failures/waiting/recovery are logged on state changes. This rig tests one eight-item batch and target-scope output, not all Paper Conditions, keep semantics or throughput bounds.

## Chests

**Bezedná truhla / Bottomless chest:** 16 independent item templates. Insert a stack to set its slot's item and replenishment count. Extraction restores that slot to its template count. Multiple different items can be supplied simultaneously. Replacing a stack with another item replaces its template; adding more of the same item increases its maintained count. Sneak + sprint + right-click with an empty hand clears all templates and contents, allowing a fresh configuration. Templates are stored with the chest. The source does not perform the usual periodic perish update.

**Likvidační truhla / Recycler chest:** vanilla inventory accepting normal items. No destruction until every slot reaches its allowed item stack size. Then it consumes one oldest arrival batch every five active seconds until empty. Consecutive additions to the same slot form one batch; additions separated by arrivals to other slots retain their order. External withdrawals and slot replacement update the queue. Sneak + right-click opens settings: interval Immediately / 1 / 2 / 5 / 10 seconds, and trigger full / half full. Half full is the sum of each slot's occupied fraction (eight full slots or sixteen half stacks in a 16-slot chest). Immediate mode drains the queue on reaching that threshold. Default remains full + 5 seconds. Queue order, settings, remaining delay and counters survive save/load; offline time does not delete contents. There is no per-item timer or background task.

Both reuse the vanilla typed chest window, orientation and animated shape. Shapes are copied from vanilla normal chest; texture references use vanilla ebony planks/gold sheet and blackened gray planks/black fittings.

## Logging and performance

UTF-8 reports are in the game data folder `signalslink-testing/`. Files rotate each UTC day and each server session. A new error or state change is logged once; a recovered cycle writes recovery. Successful cycles update counters only. Each minute writes per-rig summaries and harness callback average/maximum timing. Log write failure emits a server error once; it does not destroy or stop test assemblies.

Timing measures the test harness, not total server tick time. Use `/debug logticks 100` to toggle the game's slow server tick profiler when investigating production performance. Increasing the number of rigs stresses the actual devices, inventories, signals and chunk simulation.

## Verification

```powershell
dotnet test SignalsLink.Tests/SignalsLink.Tests.csproj -p:SkipPublishCopy=true
```

Tests cover real inventory transfer and refill, multiple templates, FIFO, save/load state, ground placement bounds, foreign block/wire preservation and persistent manifests. Actual GUI interaction, animation, placement, long-running multi-rig behavior and restart in the game require in-game validation; unit tests and build are not substitutes for that.
