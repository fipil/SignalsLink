# Link rendering and synchronization (2026-09-17)

Hoses and sleeves retain their saved graph; no rebuilding in the world is necessary. Deploy matching SignalsLink versions on server and clients because the network channel now includes delta messages.

## Rendering

- Snapshots are reconciled and ordinary changes only queue affected connections. Dirty chunks queue their nearby connections; unchanged endpoint block IDs reuse existing geometry.
- Link construction is limited to 8 connections / approximately 2 ms per client tick. Batch uploads are limited to 8 batches / approximately 2 ms per rendered frame. Budgets are checked between operations, not hard preemption of an individual operation.
- Up to 16 connections of the same material and origin chunk share a GPU mesh. A change rebuilds its small batch, not all connections.
- Distance candidates refresh every 250 ms or after camera movement; frustum culling is applied each frame. Bounds include the segment and motion, not just one endpoint.
- Endpoint chunk residency is checked in bounded slices. GPU meshes are released incrementally after chunk unload and on renderer disposal/texture reload.
- Cached vertex weights deform only positions. UVs, indices and rest geometry are reused; lighting normals remain those of the resting mesh during the small sway. Invisible animations are settled and new animations are restricted to visible segments.
- A pulse chooses one eligible segment along the selected flowing branch through couplings. It stops at the far endpoint, never follows other branches on that endpoint, and does not restart already active segments. At most 8 segments animate simultaneously per client. Clients may choose different segments; animation is cosmetic.
- Routes are cached and invalidated on topology changes and relevant chunk loading. Initial traversal and pulse candidate selection are proportional to that route, not the world graph.
- Placement preview is rebuilt at most 30 times per second after a meaningful endpoint movement. Existing GPU buffers are updated while vertex/index counts fit; stationary previews reuse their mesh.

## Server and network

- Mutations are coalesced for 50 ms into an added/removed delta, with the final operation for each pair winning.
- Full snapshots are used on joining and on revision mismatch. Original protobuf connection field 1 and world save key are preserved.
- The obstruction monitor maintains a conservative chunk index from coordinates, independent of chunk loading. No periodic whole-world collision-path indexing remains.
- Every 250 ms it checks up to 4 queued placement candidates plus 2 rotating background candidates. Unloaded paths remain intact. Large queues take multiple ticks; background sweep latency grows with sleeve count. Building through a sleeve may therefore cut it on a later tick rather than synchronously during placement.
- Graph replacement initializes the coordinate index once. Path collision work remains bounded during normal ticks.

## Verification

LinkPerformanceTests covers old save serialization, delta application/recovery/batching, spatial coverage, cached coupling routes and loops, middle-segment animation and branch isolation, vertex weights, reuse of unchanged meshes, batch count, position-only updates, animation cap, unloaded mesh disposal, and bounded server path reads.

These tests use real geometry and mocked rendering/network APIs; they do not measure OpenGL frame time or visually verify the game. In-game checks should include joining/rejoining multiplayer, editing/cutting several connections, loading/unloading chunks, looking away from active lines, a long hose/sleeve through several couplings, texture reload, and placing a block through a sleeve.
