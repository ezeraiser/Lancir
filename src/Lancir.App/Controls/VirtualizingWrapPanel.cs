using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Lancir.App.Controls;

/// <summary>
/// A wrap panel that only builds the items that are on screen, plus a few rows above and below them (<see cref="Overscan"/>), and
/// builds or recycles the rest as the list is scrolled. WPF has no such panel for a grid of tiles: with a plain WrapPanel every tile of a
/// large library is built at once (its template, its cover picture), so opening the app got slower and used more memory with every game
/// added. All items are assumed to have the same size (the size of the first one built is used), which lets the position of any item be
/// computed without building the ones before it. Must sit in an ItemsControl whose template has a ScrollViewer with CanContentScroll on.
/// </summary>
public sealed class VirtualizingWrapPanel : VirtualizingPanel, IScrollInfo
{
    private const double LineHeight = 48;
    private const double WheelStep = 96;

    public static readonly DependencyProperty OverscanProperty = DependencyProperty.Register(
        nameof(Overscan), typeof(int), typeof(VirtualizingWrapPanel),
        new FrameworkPropertyMetadata(2, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>How many rows above and below the visible ones are kept built, so a scroll finds them ready.</summary>
    public int Overscan
    {
        get => (int)GetValue(OverscanProperty);
        set => SetValue(OverscanProperty, value);
    }

    private Size _itemSize = new(192, 330); // replaced by the real size of the first built item
    private Size _extent;
    private Size _viewport;
    private Point _offset;
    private int _columns = 1;

    // ---- layout ----

    protected override Size MeasureOverride(Size availableSize)
    {
        _ = InternalChildren; // makes sure the generator exists
        var itemsOwner = ItemsControl.GetItemsOwner(this);
        var count = itemsOwner?.Items.Count ?? 0;

        var width = double.IsInfinity(availableSize.Width) ? _itemSize.Width : availableSize.Width;
        var height = double.IsInfinity(availableSize.Height) ? double.PositiveInfinity : availableSize.Height;

        var firstIndex = 0;
        var lastIndex = -1;

        // the first pass may use a guessed item size; once a real item was measured the window is worked out again with its size
        for (var pass = 0; pass < 3; pass++)
        {
            _columns = Math.Max(1, (int)Math.Floor(width / Math.Max(1, _itemSize.Width)));
            var rows = (count + _columns - 1) / _columns;
            var extentHeight = rows * _itemSize.Height;
            var viewportHeight = double.IsInfinity(height) ? extentHeight : height;

            _offset.Y = Math.Clamp(_offset.Y, 0, Math.Max(0, extentHeight - viewportHeight));
            var firstRow = (int)Math.Floor(_offset.Y / _itemSize.Height);
            var lastRow = (int)Math.Ceiling((_offset.Y + viewportHeight) / _itemSize.Height) - 1;
            var from = Math.Max(0, firstRow - Overscan);
            var to = Math.Min(Math.Max(rows - 1, 0), lastRow + Overscan);
            firstIndex = from * _columns;
            lastIndex = Math.Min(count - 1, (to + 1) * _columns - 1);

            UpdateScrollInfo(new Size(width, viewportHeight), new Size(_columns * _itemSize.Width, extentHeight));

            if (count == 0)
            {
                RemoveAllChildren();
                return new Size(0, 0);
            }

            Realize(firstIndex, lastIndex);

            // the real size of an item, from the first one built: if it differs from what was assumed, lay out again with it
            if (InternalChildren.Count > 0 && InternalChildren[0].DesiredSize is { Width: > 0, Height: > 0 } real
                && (Math.Abs(real.Width - _itemSize.Width) > 0.5 || Math.Abs(real.Height - _itemSize.Height) > 0.5))
            {
                _itemSize = real;
                continue;
            }

            break;
        }

        return new Size(Math.Min(width, _columns * _itemSize.Width), double.IsInfinity(height) ? _extent.Height : height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var generator = ItemContainerGenerator;
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var index = generator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));
            if (index < 0)
            {
                continue;
            }

            var row = index / _columns;
            var column = index % _columns;
            InternalChildren[i].Arrange(new Rect(column * _itemSize.Width, row * _itemSize.Height - _offset.Y, _itemSize.Width, _itemSize.Height));
        }

        return finalSize;
    }

    /// <summary>Builds the items firstIndex..lastIndex that are not built yet and gives up the built ones outside that range.</summary>
    private void Realize(int firstIndex, int lastIndex)
    {
        var generator = (IItemContainerGenerator)ItemContainerGenerator;
        CleanUp(generator, firstIndex, lastIndex);

        var start = generator.GeneratorPositionFromIndex(firstIndex);
        var childIndex = start.Offset == 0 ? start.Index : start.Index + 1;
        using (generator.StartAt(start, GeneratorDirection.Forward, true))
        {
            for (var index = firstIndex; index <= lastIndex; index++, childIndex++)
            {
                var child = (UIElement)generator.GenerateNext(out var isNew);
                if (isNew)
                {
                    if (childIndex >= InternalChildren.Count)
                    {
                        AddInternalChild(child);
                    }
                    else
                    {
                        InsertInternalChild(childIndex, child);
                    }

                    generator.PrepareItemContainer(child);
                }

                child.Measure(new Size(_itemSize.Width, double.PositiveInfinity));
            }
        }
    }

    private void CleanUp(IItemContainerGenerator generator, int firstIndex, int lastIndex)
    {
        var recycler = VirtualizingPanel.GetVirtualizationMode(this) == VirtualizationMode.Recycling
            ? generator as IRecyclingItemContainerGenerator
            : null;
        for (var i = InternalChildren.Count - 1; i >= 0; i--)
        {
            var position = new GeneratorPosition(i, 0);
            var index = generator.IndexFromGeneratorPosition(position);
            if (index >= firstIndex && index <= lastIndex)
            {
                continue;
            }

            if (recycler is not null)
            {
                recycler.Recycle(position, 1); // the container (and its template) is reused for an item that scrolls in
            }
            else
            {
                generator.Remove(position, 1);
            }

            RemoveInternalChildRange(i, 1);
        }
    }

    private void RemoveAllChildren()
    {
        if (InternalChildren.Count > 0)
        {
            ((IItemContainerGenerator)ItemContainerGenerator).RemoveAll();
            InternalChildren.Clear();
        }
    }

    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        switch (args.Action)
        {
            case NotifyCollectionChangedAction.Remove:
            case NotifyCollectionChangedAction.Replace:
                RemoveInternalChildRange(args.Position.Index, args.ItemUICount);
                break;
            case NotifyCollectionChangedAction.Move:
                RemoveInternalChildRange(args.OldPosition.Index, args.ItemUICount);
                break;
        }

        base.OnItemsChanged(sender, args);
        InvalidateMeasure();
    }

    protected override void OnClearChildren()
    {
        base.OnClearChildren();
        _offset.Y = 0;
    }

    // ---- scrolling (IScrollInfo) ----

    private void UpdateScrollInfo(Size viewport, Size extent)
    {
        var changed = viewport != _viewport || extent != _extent;
        _viewport = viewport;
        _extent = extent;
        if (changed)
        {
            ScrollOwner?.InvalidateScrollInfo();
        }
    }

    public bool CanVerticallyScroll { get; set; } = true;

    public bool CanHorizontallyScroll { get; set; }

    public double ExtentWidth => _extent.Width;

    public double ExtentHeight => _extent.Height;

    public double ViewportWidth => _viewport.Width;

    public double ViewportHeight => _viewport.Height;

    public double HorizontalOffset => 0;

    public double VerticalOffset => _offset.Y;

    public ScrollViewer? ScrollOwner { get; set; }

    public void SetVerticalOffset(double offset)
    {
        offset = Math.Clamp(offset, 0, Math.Max(0, _extent.Height - _viewport.Height));
        if (Math.Abs(offset - _offset.Y) < 0.01)
        {
            return;
        }

        _offset.Y = offset;
        ScrollOwner?.InvalidateScrollInfo();
        InvalidateMeasure();
    }

    public void SetHorizontalOffset(double offset)
    {
    }

    public void LineUp() => SetVerticalOffset(_offset.Y - LineHeight);

    public void LineDown() => SetVerticalOffset(_offset.Y + LineHeight);

    public void PageUp() => SetVerticalOffset(_offset.Y - _viewport.Height);

    public void PageDown() => SetVerticalOffset(_offset.Y + _viewport.Height);

    public void MouseWheelUp() => SetVerticalOffset(_offset.Y - WheelStep);

    public void MouseWheelDown() => SetVerticalOffset(_offset.Y + WheelStep);

    public void LineLeft()
    {
    }

    public void LineRight()
    {
    }

    public void PageLeft()
    {
    }

    public void PageRight()
    {
    }

    public void MouseWheelLeft()
    {
    }

    public void MouseWheelRight()
    {
    }

    /// <summary>Scrolls so that a tile that took focus (or was asked to come into view) is fully visible.</summary>
    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        var child = visual as UIElement;
        while (child is not null && !InternalChildren.Contains(child))
        {
            child = VisualTreeHelper.GetParent(child) as UIElement;
        }

        if (child is null)
        {
            return rectangle;
        }

        var index = ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(InternalChildren.IndexOf(child), 0));
        if (index < 0)
        {
            return rectangle;
        }

        var top = index / _columns * _itemSize.Height;
        if (top < _offset.Y)
        {
            SetVerticalOffset(top);
        }
        else if (top + _itemSize.Height > _offset.Y + _viewport.Height)
        {
            SetVerticalOffset(top + _itemSize.Height - _viewport.Height);
        }

        return rectangle;
    }
}
