namespace EasyWinMenu.Core.Models;

/// <summary>
/// How a group keeps its icons laid out. <see cref="None"/> is free placement — the
/// user drops each icon wherever they like and nothing moves it again. The other three
/// are "live" modes: picking one from the group menu re-lays the icons out once, and
/// then keeps doing it on its own whenever an icon is added, removed or renamed, so the
/// group never drifts back out of order the way a one-off "arrange now" command would.
/// </summary>
public enum IconArrangement
{
    None,
    Grid,
    ByName,
    ByType
}
