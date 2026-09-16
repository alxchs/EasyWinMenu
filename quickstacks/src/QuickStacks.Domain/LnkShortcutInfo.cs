namespace QuickStacks.Domain;

/// <summary>O que foi extraído de um arquivo .lnk (RF08 - importação de atalhos existentes).</summary>
public sealed record LnkShortcutInfo(
    string DisplayName,
    string TargetPath,
    string? Arguments,
    string? WorkingDirectory,
    string? IconLocation);
