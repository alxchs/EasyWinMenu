namespace QuickStacks.Application;

/// <summary>Um nivel da trilha exibida na BreadcrumbBar (null Id = raiz).</summary>
public sealed record BreadcrumbNodeViewModel(string? Id, string Name);
