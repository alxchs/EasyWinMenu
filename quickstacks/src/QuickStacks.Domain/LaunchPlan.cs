using System.Diagnostics;

namespace QuickStacks.Domain;

/// <summary>
/// Descreve os parâmetros precisos de inicialização de um processo pelo sistema operacional (Fase 18).
/// Desacoplado da camada de UI e testável em testes unitários puros.
/// </summary>
public sealed class LaunchPlan
{
    public required string FileName { get; init; }

    public required string Arguments { get; init; }

    public string? WorkingDirectory { get; init; }

    public bool UseShellExecute { get; init; } = true;

    public string? Verb { get; init; }

    public ProcessWindowStyle WindowStyle { get; init; } = ProcessWindowStyle.Normal;
}

