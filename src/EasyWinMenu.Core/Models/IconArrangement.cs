namespace EasyWinMenu.Core.Models;

/// <summary>
/// How a group keeps its icons laid out. <see cref="None"/> is free placement — the
/// user drops each icon wherever they like and nothing moves it again. <see cref="ByName"/>
/// is the single "live" mode: it re-lays the icons out once and then keeps doing it on
/// its own whenever an icon is added, removed or renamed. A group only holds shortcuts,
/// so the order is always by shortcut name. <see cref="Grid"/> and <see cref="ByType"/>
/// only remain so that configs saved by older versions still deserialize; both are read
/// as <see cref="ByName"/> and rewritten as such on the next change.
/// </summary>
public enum IconArrangement
{
    None,
    Grid,
    ByName,
    ByType
}
