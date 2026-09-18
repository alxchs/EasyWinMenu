using Microsoft.UI.Dispatching;
using QuickStacks.Domain;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace QuickStacks.UI;

/// <summary>
/// Contrato para janelas/controles que exibem itens sujeitos a efeito visual de recorte (Fase 16).
/// </summary>
public interface ICutVisualOwner
{
    string OwnerId { get; }
    void RefreshCutVisuals();
    Task ReloadAsync();
}

/// <summary>
/// Servico centralizado de clipboard com suporte a Ctrl+C, Ctrl+X e Ctrl+V (Fase 16).
/// Conecta o <see cref="PendingCutCoordinator"/> com a API nativa da Windows Clipboard e WinUI 3.
/// </summary>
public static class ClipboardService
{
    private static readonly List<WeakReference<ICutVisualOwner>> _owners = [];
    private static DispatcherQueueTimer? _diskWatcher;

    public static PendingCutCoordinator Coordinator { get; } = new();

    static ClipboardService()
    {
        Coordinator.StateChanged += (_, _) => NotifyOwnersVisualChanged();
    }

    public static void RegisterOwner(ICutVisualOwner owner)
    {
        _owners.RemoveAll(wr => !wr.TryGetTarget(out _));
        if (!_owners.Any(wr => wr.TryGetTarget(out var target) && ReferenceEquals(target, owner)))
        {
            _owners.Add(new WeakReference<ICutVisualOwner>(owner));
        }
    }

    public static void UnregisterOwner(ICutVisualOwner owner)
    {
        _owners.RemoveAll(wr => !wr.TryGetTarget(out var target) || ReferenceEquals(target, owner));
    }

    public static void Copy(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path)))
        {
            return;
        }

        SetClipboardStorageItem(path, cut: false);
        Coordinator.Clear();
        StopDiskWatcherIfIdle();
    }

    public static void Cut(string itemId, string path, string? ownerId, DispatcherQueue dispatcherQueue)
    {
        if (string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path)))
        {
            return;
        }

        SetClipboardStorageItem(path, cut: true);
        Coordinator.Clear();
        Coordinator.RegisterCut(itemId, path, ownerId);

        StartDiskWatcher(dispatcherQueue);
    }

    public static async Task PasteAsync(IMenuRepository repository, string? targetFolderId, Func<Task> onReload)
    {
        var content = Clipboard.GetContent();
        if (!content.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        var items = await content.GetStorageItemsAsync();
        if (items.Count == 0)
        {
            return;
        }

        var siblingCount = (await repository.GetChildrenAsync(targetFolderId)).Count;
        var pastedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var storageItem in items)
        {
            var type = storageItem is StorageFolder ? MenuItemType.Executable : MenuItemType.Executable;
            var launchItem = MenuItem.CreateShortcut(storageItem.Name, targetFolderId, type, storageItem.Path, siblingCount++);
            await repository.AddAsync(launchItem);
            pastedPaths.Add(storageItem.Path);
        }

        var consumed = Coordinator.ConsumeMatching(pastedPaths);
        foreach (var cut in consumed)
        {
            await repository.DeleteAsync(cut.ItemId);
        }

        await onReload();

        var affectedOwnerIds = consumed
            .Select(c => c.SourceFolderId ?? "root")
            .Distinct()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var wr in _owners)
        {
            if (wr.TryGetTarget(out var owner) && affectedOwnerIds.Contains(owner.OwnerId))
            {
                await owner.ReloadAsync();
            }
        }

        StopDiskWatcherIfIdle();
    }

    private static void SetClipboardStorageItem(string path, bool cut)
    {
        var package = new DataPackage
        {
            RequestedOperation = cut ? DataPackageOperation.Move : DataPackageOperation.Copy
        };

        if (File.Exists(path))
        {
            _ = StorageFile.GetFileFromPathAsync(path).AsTask().ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully)
                {
                    package.SetStorageItems([t.Result]);
                    Clipboard.SetContent(package);
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
        else if (Directory.Exists(path))
        {
            _ = StorageFolder.GetFolderFromPathAsync(path).AsTask().ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully)
                {
                    package.SetStorageItems([t.Result]);
                    Clipboard.SetContent(package);
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
    }

    private static void StartDiskWatcher(DispatcherQueue dispatcherQueue)
    {
        if (_diskWatcher is not null)
        {
            return;
        }

        _diskWatcher = dispatcherQueue.CreateTimer();
        _diskWatcher.Interval = TimeSpan.FromSeconds(2);
        _diskWatcher.Tick += async (_, _) => await CheckMissingCutsAgainstDiskAsync();
        _diskWatcher.Start();
    }

    private static async Task CheckMissingCutsAgainstDiskAsync()
    {
        var missing = Coordinator.DetectMissingFromDisk(p => File.Exists(p) || Directory.Exists(p));
        if (missing.Count == 0)
        {
            StopDiskWatcherIfIdle();
            return;
        }

        var affectedOwnerIds = missing
            .Select(c => c.SourceFolderId ?? "root")
            .Distinct()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var wr in _owners)
        {
            if (wr.TryGetTarget(out var owner) && affectedOwnerIds.Contains(owner.OwnerId))
            {
                await owner.ReloadAsync();
            }
        }

        StopDiskWatcherIfIdle();
    }

    private static void StopDiskWatcherIfIdle()
    {
        if (Coordinator.GetActiveCuts().Count == 0 && _diskWatcher is not null)
        {
            _diskWatcher.Stop();
            _diskWatcher = null;
        }
    }

    private static void NotifyOwnersVisualChanged()
    {
        foreach (var wr in _owners)
        {
            if (wr.TryGetTarget(out var owner))
            {
                owner.RefreshCutVisuals();
            }
        }
    }
}
