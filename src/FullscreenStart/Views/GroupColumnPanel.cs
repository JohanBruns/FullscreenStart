using System.Windows;
using System.Windows.Controls;
using FullscreenStart.Layout;
using FullscreenStart.ViewModels;

namespace FullscreenStart.Views;

/// <summary>Arranges the groups of one column, honouring each group's free position (see <see cref="GroupStack"/>).</summary>
public sealed class GroupColumnPanel : Panel
{
    private double[] _tops = Array.Empty<double>();

    protected override Size MeasureOverride(Size availableSize)
    {
        var groups = new List<(int Top, double Height)>();
        double width = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            int top = (child as FrameworkElement)?.DataContext is GroupViewModel g ? g.Model.Top : 0;
            groups.Add((top, child.DesiredSize.Height));
            width = Math.Max(width, child.DesiredSize.Width);
        }
        _tops = GroupStack.Arrange(groups);
        double height = groups.Count == 0 ? 0 : _tops[^1] + groups[^1].Height;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            double top = i < _tops.Length ? _tops[i] : 0;
            child.Arrange(new Rect(0, top, finalSize.Width, child.DesiredSize.Height));
        }
        return finalSize;
    }
}
