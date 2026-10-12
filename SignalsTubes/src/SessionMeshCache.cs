using System.Runtime.CompilerServices;
using Vintagestory.API.Client;

namespace SignalsTubes.src;

/// <summary>
/// Block meshes shared by all block entities of one kind. A mesh holds the atlas texture ids and UVs of one
/// client session, so the cache empties when the client joins a world again; a stale atlas id stops the
/// whole chunk from tesselating.
/// </summary>
public sealed class SessionMeshCache
{
    private readonly Dictionary<string, MeshData> meshes = new();
    private readonly ConditionalWeakTable<ICoreClientAPI, object> session = new();
    private readonly int max;

    public SessionMeshCache(int max) { this.max = max; }

    public MeshData Get(ICoreClientAPI capi, string key, Func<MeshData> build)
    {
        lock (meshes)
        {
            if (!session.TryGetValue(capi, out _))
            {
                meshes.Clear();
                session.Clear();
                session.Add(capi, null);
            }
            if (meshes.TryGetValue(key, out var cached)) return cached;
            if (meshes.Count >= max) meshes.Clear();
            return meshes[key] = build();
        }
    }
}
