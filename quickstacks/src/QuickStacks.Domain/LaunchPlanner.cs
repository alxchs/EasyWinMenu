using System.Diagnostics;

namespace QuickStacks.Domain;

/// <summary>
/// Construtor de planos de execução de itens (Fase 18).
/// Garante paridade total com o comportamento do EasyWinMenu:
/// - Expansão de variáveis de ambiente (%VAR%)
/// - Resolução de executáveis soltos via %PATH%
/// - Envoltório de comandos via cmd.exe /c
/// - Modos de execução (Normal, Minimizada, Maximizada, Administrador via 'runas')
/// - Descoberta automática de diretório de trabalho quando omitido.
/// </summary>
public static class LaunchPlanner
{
    private static readonly string[] ExecutableExtensions = ["", ".exe", ".cmd", ".bat", ".com"];

    /// <summary>Expande variáveis de ambiente (%VAR%) em uma string de forma segura.</summary>
    public static string Expand(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : Environment.ExpandEnvironmentVariables(value.Trim());
    }

    /// <summary>
    /// Resolve o caminho completo de um executável, inclusive pesquisando nos diretórios do %PATH%
    /// caso seja um nome de arquivo sem separadores de pasta.
    /// </summary>
    public static string ResolveExecutablePath(
        string target,
        string? pathEnvironment = null,
        Func<string, bool>? fileExists = null,
        Func<string, bool>? directoryExists = null)
    {
        var existsFile = fileExists ?? File.Exists;
        var existsDir = directoryExists ?? Directory.Exists;

        if (string.IsNullOrWhiteSpace(target))
        {
            return string.Empty;
        }

        var expanded = Expand(target);

        // Se já existe diretamente (caminho relativo ou absoluto)
        if (existsFile(expanded) || existsDir(expanded))
        {
            try
            {
                return Path.GetFullPath(expanded);
            }
            catch
            {
                return expanded;
            }
        }

        // Se contém separador de diretório, não busca no PATH
        if (expanded.Contains(Path.DirectorySeparatorChar) || expanded.Contains(Path.AltDirectorySeparatorChar))
        {
            return expanded;
        }

        // Busca no PATH
        var pathVar = pathEnvironment ?? Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var directories = pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var dir in directories)
        {
            foreach (var ext in ExecutableExtensions)
            {
                var candidateName = expanded.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? expanded : expanded + ext;
                var candidate = Path.Combine(dir, candidateName);
                if (existsFile(candidate))
                {
                    try
                    {
                        return Path.GetFullPath(candidate);
                    }
                    catch
                    {
                        return candidate;
                    }
                }
            }
        }

        return expanded;
    }

    /// <summary>
    /// Constrói o plano de execução a partir dos dados do item.
    /// </summary>
    public static LaunchPlan BuildPlan(
        MenuItem item,
        string? pathEnvironment = null,
        Func<string, bool>? fileExists = null,
        Func<string, bool>? directoryExists = null,
        ExecutionMode? modeOverride = null)
    {
        var existsFile = fileExists ?? File.Exists;
        var existsDir = directoryExists ?? Directory.Exists;

        var rawTarget = item.Path ?? string.Empty;
        var mode = modeOverride ?? item.ExecutionMode;

        string fileName;
        string arguments;

        if (item.Type == MenuItemType.Command)
        {
            fileName = "cmd.exe";
            var cmdTarget = Expand(rawTarget);
            var cmdArgs = Expand(item.Arguments);
            arguments = string.IsNullOrWhiteSpace(cmdArgs)
                ? $"/c \"{cmdTarget}\""
                : $"/c \"{cmdTarget}\" {cmdArgs}";
        }
        else
        {
            fileName = ResolveExecutablePath(rawTarget, pathEnvironment, existsFile, existsDir);
            arguments = Expand(item.Arguments);
        }

        // Diretório de trabalho
        string? workingDir = null;
        if (!string.IsNullOrWhiteSpace(item.WorkingDirectory))
        {
            workingDir = Expand(item.WorkingDirectory);
        }
        else if (item.Type != MenuItemType.Command && item.Type != MenuItemType.Url)
        {
            var targetPath = Expand(rawTarget);
            if (existsDir(targetPath))
            {
                workingDir = targetPath;
            }
            else if (existsFile(targetPath))
            {
                var dir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    workingDir = dir;
                }
            }
        }

        var verb = mode == ExecutionMode.Administrator ? "runas" : null;
        var windowStyle = mode switch
        {
            ExecutionMode.Minimized => ProcessWindowStyle.Minimized,
            ExecutionMode.Maximized => ProcessWindowStyle.Maximized,
            _ => ProcessWindowStyle.Normal,
        };

        return new LaunchPlan
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDir,
            UseShellExecute = true,
            Verb = verb,
            WindowStyle = windowStyle,
        };
    }
}

