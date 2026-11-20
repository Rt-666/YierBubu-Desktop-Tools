using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace YierBubu.Windows;

public sealed record AnimationEntry(string Id, string Name, string Description, string File, string? SourceUrl = null, string? Sha256 = null,
    int FrameWidth=0,int FrameHeight=0,int SpriteRow=0,int FrameCount=0,double[]? Delays=null);
public sealed record AnimationLoadFailure(string Id, string Message);
public sealed class AnimationLoadException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Loads local GIFs away from the UI thread and keeps a bounded LRU cache.</summary>
public sealed class AnimationLibrary
{
    private readonly string directory;
    private readonly Dictionary<string, AnimationEntry> entries;
    private readonly Dictionary<string, AnimatedClip> cache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task<AnimatedClip>> inFlight = new(StringComparer.Ordinal);
    private readonly LinkedList<string> recentlyUsed = new();
    private readonly object sync = new();
    private readonly SemaphoreSlim decodeGate = new(1, 1);
    private readonly long maximumCacheBytes;
    private long cachedBytes;
    public IReadOnlyList<AnimationEntry> Entries { get; }
    public long CachedBytes { get { lock (sync) return cachedBytes; } }
    public int CachedCount { get { lock (sync) return cache.Count; } }
    public const long MaximumClipBytes = 32L * 1024 * 1024;

    /// <param name="assetsRoot">The application's Assets directory.</param>
    public AnimationLibrary(string assetsRoot, long maximumCacheBytes = 64L * 1024 * 1024,string collection="Animations")
    {
        this.maximumCacheBytes = Math.Max(MaximumClipBytes, maximumCacheBytes);
        if(collection is not ("Animations" or "Keyboards"))throw new ArgumentException("Unknown animation collection",nameof(collection));
        directory = Path.GetFullPath(Path.Combine(assetsRoot, collection));
        string catalog = Path.Combine(directory, "catalog.json");
        try
        {
            if (!System.IO.File.Exists(catalog))
            {
                entries = new(StringComparer.Ordinal); Entries = Array.Empty<AnimationEntry>(); return;
            }
            using var document = JsonDocument.Parse(System.IO.File.ReadAllText(catalog));
            var array = document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement : document.RootElement.GetProperty("animations");
            var items = JsonSerializer.Deserialize<AnimationEntry[]>(array.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
            if (items.Length > 24) throw new InvalidDataException("动画目录最多支持 24 款动画。");
            entries = new(StringComparer.Ordinal);
            foreach (var entry in items)
            {
                if (string.IsNullOrWhiteSpace(entry.Id) || entry.Id.Length > 64 || entry.Id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_') ||
                    string.IsNullOrWhiteSpace(entry.Name) || string.IsNullOrWhiteSpace(entry.File))
                    throw new InvalidDataException("动画目录缺少有效的名称或文件信息。");
                ResolveFile(entry); // Validate traversal and format before accepting the catalog.
                if (!entries.TryAdd(entry.Id, entry)) throw new InvalidDataException("动画目录存在重复的标识。");
            }
            Entries = new ReadOnlyCollection<AnimationEntry>(items);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or ArgumentException or InvalidOperationException)
        {
            throw new AnimationLoadException("无法读取动画目录，请重新解压完整程序。", e);
        }
    }

    private string ResolveFile(AnimationEntry entry)
    {
        string path = Path.GetFullPath(Path.Combine(directory, entry.File));
        if (!path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !(string.Equals(Path.GetExtension(path), ".gif", StringComparison.OrdinalIgnoreCase)||string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("动画文件必须是动画目录内的 GIF 或 PNG 图集。");
        return path;
    }
    public bool Contains(string id) => entries.ContainsKey(id);
    public bool TryGet(string id, [NotNullWhen(true)] out AnimatedClip? clip)
    {
        lock (sync)
        {
            if (!cache.TryGetValue(id, out clip)) return false;
            Touch(id); return true;
        }
    }
    public AnimatedClip Get(string id) => TryGet(id, out var clip) ? clip! : throw new AnimationLoadException("动画尚未加载，请稍后再试。");
    public Task<AnimatedClip> LoadAsync(string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task<AnimatedClip> task;
        lock (sync)
        {
            if (cache.TryGetValue(id, out var clip)) { Touch(id); return Task.FromResult(clip); }
            if (!entries.TryGetValue(id, out var entry)) throw new AnimationLoadException("找不到这款动画，请选择其他动画。");
            if (!inFlight.TryGetValue(id, out task!)) { task = LoadCoreAsync(entry); inFlight.Add(id, task); }
        }
        // A caller changing its selection must not cancel a shared preload.
        return cancellationToken.CanBeCanceled ? task.WaitAsync(cancellationToken) : task;
    }
    private async Task<AnimatedClip> LoadCoreAsync(AnimationEntry entry)
    {
        await Task.Yield();
        await decodeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var clip = await Task.Run(() => AnimatedClip.Decode(entry, ResolveFile(entry), MaximumClipBytes)).ConfigureAwait(false);
            lock (sync)
            {
                while (cache.Count >= 6 || cachedBytes + clip.EstimatedBytes > maximumCacheBytes)
                {
                    string oldest = recentlyUsed.First!.Value;
                    cachedBytes -= cache[oldest].EstimatedBytes; cache.Remove(oldest); recentlyUsed.RemoveFirst();
                }
                cache.Add(entry.Id, clip); cachedBytes += clip.EstimatedBytes; Touch(entry.Id);
            }
            return clip;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or ExternalException or OutOfMemoryException)
        {
            throw new AnimationLoadException($"“{entry.Name}”暂时无法播放，请检查动画文件是否完整。", e);
        }
        finally { lock (sync) inFlight.Remove(entry.Id); decodeGate.Release(); }
    }
    private void Touch(string id) { recentlyUsed.Remove(id); recentlyUsed.AddLast(id); }
    public async Task<IReadOnlyList<AnimationLoadFailure>> PreloadAsync(CancellationToken cancellationToken = default)
    {
        var failures = new List<AnimationLoadFailure>();
        foreach (var entry in Entries.Take(6))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { await LoadAsync(entry.Id, cancellationToken).ConfigureAwait(false); }
            catch (AnimationLoadException e) { failures.Add(new(entry.Id, e.Message)); }
        }
        return failures.AsReadOnly();
    }
}
