namespace QuickStacks.Application;

/// <summary>
/// Barramento simples de notificação para sincronização em tempo real entre EditorWindow
/// e DesktopGroupWindow(s). Disparado quando qualquer operação de escrita (adição, edição,
/// exclusão, reparentamento, ordenação ou drop) ocorre no banco.
/// </summary>
public static class DataChangeNotifier
{
    public static event Action? Changed;

    public static void NotifyChanged()
    {
        Changed?.Invoke();
    }
}

