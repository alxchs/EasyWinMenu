namespace QuickStacks.Domain;

/// <summary>
/// Representa um item em estado de recorte pendente (Ctrl+X).
/// O item so' e' de fato removido da pasta de origem quando colado em outro lugar
/// ou quando o arquivo deixa de existir no disco.
/// </summary>
public sealed record PendingCutItem(string ItemId, string Path, string? SourceFolderId);

/// <summary>
/// Coordenador desacoplado de UI e de plataforma que gerencia recortes pendentes do clipboard (Fase 16).
/// Segue o modelo PendingCut do EasyWinMenu, permitindo teste unitario puro de estado.
/// </summary>
public sealed class PendingCutCoordinator
{
    private readonly List<PendingCutItem> _pendingCuts = [];
    private readonly object _lock = new();

    public event EventHandler? StateChanged;

    public IReadOnlyList<PendingCutItem> GetActiveCuts()
    {
        lock (_lock)
        {
            return _pendingCuts.ToList();
        }
    }

    public bool IsCutPending(string itemId)
    {
        lock (_lock)
        {
            return _pendingCuts.Any(c => c.ItemId == itemId);
        }
    }

    public void RegisterCut(string itemId, string path, string? sourceFolderId)
    {
        lock (_lock)
        {
            _pendingCuts.Add(new PendingCutItem(itemId, path, sourceFolderId));
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        lock (_lock)
        {
            if (_pendingCuts.Count == 0)
            {
                return;
            }

            _pendingCuts.Clear();
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<PendingCutItem> ConsumeMatching(IEnumerable<string> paths)
    {
        var pathSet = paths.Where(p => !string.IsNullOrWhiteSpace(p)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<PendingCutItem> matched;

        lock (_lock)
        {
            matched = _pendingCuts.Where(p => pathSet.Contains(p.Path)).ToList();
            foreach (var item in matched)
            {
                _pendingCuts.Remove(item);
            }
        }

        if (matched.Count > 0)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        return matched;
    }

    public IReadOnlyList<PendingCutItem> DetectMissingFromDisk(Func<string, bool> pathExists)
    {
        List<PendingCutItem> missing;

        lock (_lock)
        {
            missing = _pendingCuts.Where(p => !pathExists(p.Path)).ToList();
            foreach (var item in missing)
            {
                _pendingCuts.Remove(item);
            }
        }

        if (missing.Count > 0)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        return missing;
    }
}
