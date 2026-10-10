namespace FullscreenStart.Layout;

/// <summary>
/// Stacks the groups of one column: each group sits right below the previous one, or further down at
/// its own position (<see cref="Models.TileGroup.Top"/>) if that is lower. Groups never overlap.
/// </summary>
public static class GroupStack
{
    /// <summary>One tile row: a 60 px cell plus the 4 px gap.</summary>
    public const double RowHeight = 64;

    /// <summary>Y offset of every group, given each group's wanted row and its height (including its margin).</summary>
    public static double[] Arrange(IReadOnlyList<(int Top, double Height)> groups)
    {
        var result = new double[groups.Count];
        double y = 0;
        for (int i = 0; i < groups.Count; i++)
        {
            y = Math.Max(y, groups[i].Top * RowHeight);
            result[i] = y;
            y += groups[i].Height;
        }
        return result;
    }

    /// <summary>Row closest to <paramref name="y"/>, or 0 when that is no further down than <paramref name="flowY"/> (where the group would land anyway).</summary>
    public static int RowAt(double y, double flowY)
    {
        int row = (int)Math.Round(Math.Max(0, y) / RowHeight);
        return row * RowHeight <= flowY + RowHeight / 2 ? 0 : row;
    }
}
