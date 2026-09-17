namespace QuickStacks.Domain;

/// <summary>Posição livre (X/Y) de um item dentro do canvas do grupo solto que o contém (Fase 9, modo Panel).</summary>
public sealed record DesktopIconPosition(string ItemId, double X, double Y);
