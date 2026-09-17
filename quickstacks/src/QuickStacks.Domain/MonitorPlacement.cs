namespace QuickStacks.Domain;

/// <summary>Retangulo de area de trabalho em pixels independentes de DPI - sem dependencia de WinUI/WPF para poder ser testado sem hardware real (Fase 12).</summary>
public readonly record struct MonitorRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double CenterX => X + (Width / 2);
    public double CenterY => Y + (Height / 2);

    public MonitorRect IntersectWith(MonitorRect other)
    {
        var x1 = Math.Max(X, other.X);
        var y1 = Math.Max(Y, other.Y);
        var x2 = Math.Min(Right, other.Right);
        var y2 = Math.Min(Bottom, other.Bottom);
        return x2 > x1 && y2 > y1 ? new MonitorRect(x1, y1, x2 - x1, y2 - y1) : default;
    }

    public bool IsEmpty => Width <= 0 || Height <= 0;
}

/// <summary>
/// Onde um grupo deve ficar quando o conjunto de monitores nao e' mais o mesmo em que ele foi
/// posicionado (Fase 12, modelo MonitorPlacement.cs do EasyWinMenu). Geometria pura sobre
/// areas de trabalho, sem nenhuma dependencia de janela - testavel sem um segundo monitor.
/// </summary>
public static class MonitorPlacement
{
    private const double MinVisibleWidth = 48;
    private const double MinVisibleHeight = 16;
    private const double TitleStripHeight = 32;

    /// <summary>Verdadeiro quando o grupo ainda pode ser alcancado com o mouse: sua faixa de titulo se sobrepoe a alguma area de trabalho o suficiente pra ser agarrada.</summary>
    public static bool IsReachable(MonitorRect group, IReadOnlyList<MonitorRect> workAreas)
    {
        var strip = new MonitorRect(group.X, group.Y, group.Width, Math.Min(TitleStripHeight, group.Height));
        foreach (var area in workAreas)
        {
            var overlap = strip.IntersectWith(area);
            if (!overlap.IsEmpty
                && overlap.Width >= Math.Min(MinVisibleWidth, group.Width)
                && overlap.Height >= Math.Min(MinVisibleHeight, strip.Height))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A area de trabalho a que um grupo pertence: a que ele mais sobrepoe, ou senao a mais proxima do centro. -1 so' quando nao ha nenhuma area.</summary>
    public static int IndexOfOwner(MonitorRect group, IReadOnlyList<MonitorRect> workAreas)
    {
        var best = -1;
        var bestOverlap = 0.0;
        for (var i = 0; i < workAreas.Count; i++)
        {
            var overlap = group.IntersectWith(workAreas[i]);
            var area = overlap.IsEmpty ? 0 : overlap.Width * overlap.Height;
            if (area > bestOverlap)
            {
                bestOverlap = area;
                best = i;
            }
        }

        if (best >= 0)
        {
            return best;
        }

        var nearest = double.MaxValue;
        for (var i = 0; i < workAreas.Count; i++)
        {
            var distance = DistanceSquared(group.CenterX, group.CenterY, workAreas[i]);
            if (distance < nearest)
            {
                nearest = distance;
                best = i;
            }
        }

        return best;
    }

    /// <summary>O monitor vizinho de <paramref name="fromIndex"/> numa direcao (-1 esquerda, +1 direita), ordenado pelo centro horizontal e dando a volta nas pontas - igual ao Win+Shift+seta do proprio Windows. Nulo quando ha so' um monitor.</summary>
    public static int? AdjacentIndex(int fromIndex, IReadOnlyList<MonitorRect> workAreas, int direction)
    {
        if (workAreas.Count < 2 || fromIndex < 0 || fromIndex >= workAreas.Count)
        {
            return null;
        }

        var ordered = Enumerable.Range(0, workAreas.Count)
            .OrderBy(i => workAreas[i].CenterX)
            .ThenBy(i => workAreas[i].Y)
            .ToList();

        var position = ordered.IndexOf(fromIndex);
        var next = ((position + Math.Sign(direction)) % ordered.Count + ordered.Count) % ordered.Count;
        return ordered[next];
    }

    /// <summary>Carrega um grupo de uma area de trabalho pra outra mantendo o lugar relativo - um grupo no canto superior direito de um monitor cai no canto superior direito do outro - e depois o mantem inteiro dentro do destino.</summary>
    public static (double X, double Y) MapBetween(MonitorRect group, MonitorRect from, MonitorRect to)
    {
        var relativeX = Relative(group.X, from.X, from.Width, group.Width);
        var relativeY = Relative(group.Y, from.Y, from.Height, group.Height);

        var targetX = to.X + (relativeX * Math.Max(0, to.Width - group.Width));
        var targetY = to.Y + (relativeY * Math.Max(0, to.Height - group.Height));

        return ClampInto(targetX, targetY, group.Width, group.Height, to);
    }

    /// <summary>Mantem um retangulo de <paramref name="width"/>x<paramref name="height"/> dentro de <paramref name="area"/>.</summary>
    public static (double X, double Y) ClampInto(double x, double y, double width, double height, MonitorRect area)
    {
        var maxX = Math.Max(area.X, area.Right - width);
        var maxY = Math.Max(area.Y, area.Bottom - height);
        return (Math.Clamp(x, area.X, maxX), Math.Clamp(y, area.Y, maxY));
    }

    /// <summary>Empurra uma posicao na diagonal ate nao ficar mais em cima de uma ja' ocupada, pra grupos resgatados do mesmo canto nao ficarem empilhados exatamente um sobre o outro.</summary>
    public static (double X, double Y) AvoidStacking(double x, double y, double width, double height, MonitorRect area, ICollection<(double X, double Y)> taken, double step = 28)
    {
        var candidate = (X: x, Y: y);
        for (var attempt = 0; attempt < 12 && taken.Any(p => Math.Abs(p.X - candidate.X) < 4 && Math.Abs(p.Y - candidate.Y) < 4); attempt++)
        {
            candidate = ClampInto(candidate.X + step, candidate.Y + step, width, height, area);
        }

        taken.Add(candidate);
        return candidate;
    }

    private static double Relative(double position, double start, double span, double size)
    {
        var travel = span - size;
        return travel <= 0 ? 0 : Math.Clamp((position - start) / travel, 0, 1);
    }

    private static double DistanceSquared(double pointX, double pointY, MonitorRect area)
    {
        var dx = Math.Max(Math.Max(area.X - pointX, 0), pointX - area.Right);
        var dy = Math.Max(Math.Max(area.Y - pointY, 0), pointY - area.Bottom);
        return (dx * dx) + (dy * dy);
    }
}
