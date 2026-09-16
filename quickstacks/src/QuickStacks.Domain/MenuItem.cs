namespace QuickStacks.Domain;

/// <summary>
/// Um no da arvore de atalhos: pasta (container) ou uma folha executavel/arquivo/URL.
/// A hierarquia e' expressa por <see cref="ParentId"/> (self-join), sem limite de niveis.
/// </summary>
public sealed class MenuItem
{
    public required string Id { get; init; }

    public string? ParentId { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public required MenuItemType Type { get; set; }

    public string? Path { get; set; }

    public string? Arguments { get; set; }

    public string? WorkingDirectory { get; set; }

    /// <summary>Icone customizado; quando nulo, a UI extrai o icone do alvo (<see cref="Path"/>).</summary>
    public string? Icon { get; set; }

    public int SortOrder { get; set; }

    public bool IsFavorite { get; set; }

    /// <summary>Quantas vezes o item foi executado - base para "mais utilizados" (RF13).</summary>
    public int LaunchCount { get; set; }

    /// <summary>Ultima execucao - base para "recentes" (RF12). Nulo se nunca executado.</summary>
    public DateTimeOffset? LastUsedUtc { get; set; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }

    public bool IsFolder => Type == MenuItemType.Folder;

    public static MenuItem CreateFolder(string name, string? parentId, int sortOrder)
    {
        var now = DateTimeOffset.UtcNow;
        return new MenuItem
        {
            Id = Guid.NewGuid().ToString("N"),
            ParentId = parentId,
            Name = name,
            Type = MenuItemType.Folder,
            SortOrder = sortOrder,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public static MenuItem CreateShortcut(string name, string? parentId, MenuItemType type, string path, int sortOrder)
    {
        if (type == MenuItemType.Folder)
        {
            throw new ArgumentException("Use CreateFolder para itens do tipo Folder.", nameof(type));
        }

        var now = DateTimeOffset.UtcNow;
        return new MenuItem
        {
            Id = Guid.NewGuid().ToString("N"),
            ParentId = parentId,
            Name = name,
            Type = type,
            Path = path,
            SortOrder = sortOrder,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void RegisterLaunch()
    {
        LaunchCount++;
        LastUsedUtc = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
