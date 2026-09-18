namespace QuickStacks.Domain;

/// <summary>
/// Contrato para exibição do menu de contexto nativo da Shell do Windows (Fase 19).
/// </summary>
public interface IShellContextMenuService
{
    /// <summary>
    /// Exibe o menu de contexto genuíno do Explorer na coordenada de tela especificada.
    /// Retorna true se o menu foi exibido com sucesso; false caso contrário (permitindo fallback para menu interno).
    /// </summary>
    bool TryShow(IntPtr windowHandle, string path, int screenX, int screenY, bool extendedVerbs = false);
}
