using System.Text.Json;
using QuickStacks.Domain;

namespace QuickStacks.Infrastructure;

/// <summary>
/// Migra a configuração do EasyWinMenu legado (%APPDATA%\EasyWinMenu\config.json)
/// para o banco de dados do QuickStacks (quickstacks.db).
/// Importa grupos de atalhos, posições na área de trabalho e atalhos configurados.
/// </summary>
public static class EasyWinMenuMigrationService
{
    public static string DefaultConfigPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EasyWinMenu", "config.json");

    public static bool HasLegacyConfig(string? path = null)
    {
        var target = path ?? DefaultConfigPath;
        return File.Exists(target);
    }

    public static async Task<int> MigrateAsync(IMenuRepository repository, string? configPath = null, CancellationToken ct = default)
    {
        var target = configPath ?? DefaultConfigPath;
        if (!File.Exists(target))
        {
            return 0;
        }

        var json = await File.ReadAllTextAsync(target, ct);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("Categories", out var categoriesEl) || categoriesEl.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        var importedCount = 0;
        var existingFolders = (await repository.GetAllAsync(ct)).Where(i => i.IsFolder).ToList();
        var folderSort = existingFolders.Count;

        foreach (var catEl in categoriesEl.EnumerateArray())
        {
            var name = catEl.TryGetProperty("Name", out var n) ? n.GetString() ?? "Sem Nome" : "Sem Nome";
            var isDesktopGroup = catEl.TryGetProperty("IsDesktopGroup", out var dg) && dg.GetBoolean();
            
            var x = catEl.TryGetProperty("DesktopX", out var xProp) ? xProp.GetDouble() : 80.0;
            var y = catEl.TryGetProperty("DesktopY", out var yProp) ? yProp.GetDouble() : 80.0;
            var w = catEl.TryGetProperty("DesktopWidth", out var wProp) ? wProp.GetDouble() : 300.0;
            var h = catEl.TryGetProperty("DesktopHeight", out var hProp) ? hProp.GetDouble() : 200.0;
            var displayModeStr = catEl.TryGetProperty("DisplayMode", out var dmProp) ? dmProp.GetString() : "Panel";
            var displayMode = string.Equals(displayModeStr, "AppFolder", StringComparison.OrdinalIgnoreCase)
                ? DesktopGroupDisplayMode.AppFolder
                : DesktopGroupDisplayMode.Panel;

            // Se a pasta já existir (ex.: 'Dev Apps', 'Taskbar'), reutiliza o ID ou atualiza
            var existing = existingFolders.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            MenuItem folder;
            if (existing != null)
            {
                folder = existing;
                folder.IsDesktopGroup = isDesktopGroup;
                await repository.UpdateAsync(folder, ct);
            }
            else
            {
                folder = MenuItem.CreateFolder(name, null, folderSort++);
                folder.IsDesktopGroup = isDesktopGroup;
                await repository.AddAsync(folder, ct);
                existingFolders.Add(folder);
            }

            if (isDesktopGroup)
            {
                var placement = new DesktopGroupPlacement(
                    folder.Id,
                    Math.Max(0, x),
                    Math.Max(0, y),
                    Math.Max(120, w),
                    Math.Max(80, h),
                    displayMode,
                    1.0,
                    false
                );
                await repository.SetDesktopGroupPlacementAsync(placement, ct);
            }

            // Itens da categoria
            if (catEl.TryGetProperty("Items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
            {
                var existingChildren = await repository.GetChildrenAsync(folder.Id, ct);
                var itemSort = existingChildren.Count;

                foreach (var itemEl in itemsEl.EnumerateArray())
                {
                    var itemName = itemEl.TryGetProperty("Name", out var inProp) ? inProp.GetString() ?? "" : "";
                    var itemTypeStr = itemEl.TryGetProperty("Type", out var itProp) ? itProp.GetString() : "Application";
                    var targetPath = itemEl.TryGetProperty("Target", out var tProp) ? tProp.GetString() ?? "" : "";
                    var args = itemEl.TryGetProperty("Arguments", out var aProp) ? aProp.GetString() : null;
                    var workDir = itemEl.TryGetProperty("WorkingDirectory", out var wdProp) ? wdProp.GetString() : null;

                    // Não duplicar se já existir com mesmo nome e mesmo pai
                    if (existingChildren.Any(c => string.Equals(c.Name, itemName, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    var itemType = itemTypeStr switch
                    {
                        "WebUrl" => MenuItemType.Url,
                        "Application" when targetPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) => MenuItemType.Shortcut,
                        "Application" => MenuItemType.Executable,
                        _ => MenuItemType.Shortcut,
                    };

                    var shortcut = MenuItem.CreateShortcut(itemName, folder.Id, itemType, targetPath, itemSort++);
                    shortcut.Arguments = args;
                    shortcut.WorkingDirectory = workDir;
                    await repository.AddAsync(shortcut, ct);
                    importedCount++;
                }
            }
        }

        return importedCount;
    }
}

