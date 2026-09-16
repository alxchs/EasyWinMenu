using System.Text.RegularExpressions;

namespace QuickStacks.Domain;

/// <summary>Validacao de "#RRGGBB" sem depender de nenhum tipo de UI - usada tanto pela UI (antes de gravar) quanto pelo repositorio (defesa em profundidade).</summary>
public static partial class HexColor
{
    public static bool IsValid(string? value) => value is not null && HexPattern().IsMatch(value);

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexPattern();
}
