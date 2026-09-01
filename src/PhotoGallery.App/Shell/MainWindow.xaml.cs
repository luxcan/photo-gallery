using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using PhotoGallery.App.Albums;
using PhotoGallery.App.Duplicates;
using PhotoGallery.App.Gallery;
using PhotoGallery.App.People;
using PhotoGallery.App.ViewModels;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Albums;
using PhotoGallery.Application.UseCases.Gallery;

namespace PhotoGallery.App.Shell;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    /// <summary>
    /// One confirmation at a time. <see cref="MessageBox"/> runs a nested message
    /// loop, so anything that reaches the click handler while one is open - a
    /// second row's button, an accessibility tool, a test - would stack another
    /// dialog inside the first rather than being blocked by the disabled window.
    /// </summary>
    private bool _confirming;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        // The title bar is Windows', not this app's, so it has to be told the
        // theme rather than inheriting it.
        TitleBarPainter.Follow(this);

        _seekTimer.Tick += OnSeekTick;

        // Both viewers can turn a photograph, and both are refused the same way
        // when its folder is away. The dialog belongs here, so they say so and
        // this answers - and the user gets the same message deleting gives.
        viewModel.Gallery.TurnRefusedOutOfReach += OnTurnRefusedOutOfReach;
        viewModel.People.TurnRefusedOutOfReach += OnTurnRefusedOutOfReach;

        // The viewer only receives Escape and the arrow keys once it holds
        // focus, and it cannot take focus until it is actually visible.
        viewModel.Gallery.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GalleryViewModel.IsViewerOpen)
                && viewModel.Gallery.IsViewerOpen)
            {
                Dispatcher.BeginInvoke(() => PhotoViewer.Focus());
            }
        };

        // The same for the face inspector, which walks the review queue on the
        // arrow keys and closes on Escape.
        viewModel.People.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PeopleViewModel.IsInspecting)
                && viewModel.People.IsInspecting)
            {
                Dispatcher.BeginInvoke(() => FaceInspector.Focus());
            }
        };

        // And for one copy of a duplicated picture, which steps through the
        // group on the arrow keys. Without this the keys did nothing until an
        // on-screen button had been clicked, because only then was the focus
        // anywhere inside the overlay for the key to bubble out of.
        viewModel.Duplicates.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DuplicatesViewModel.IsInspecting)
                && viewModel.Duplicates.IsInspecting)
            {
                Dispatcher.BeginInvoke(() => DuplicateInspector.Focus());
            }
        };

        // Choosing in any of these lists puts the focus inside the list, and
        // closing it takes whatever held the focus out of the visual tree -
        // which leaves the focus nowhere at all. The picture is still open and
        // looks ready, but the arrow keys have nothing to bubble out of until
        // something inside is clicked again. Every list hands it back.
        WhenPickingEnds(
            viewModel.Gallery.Picker,
            () => viewModel.Gallery.Picker.IsOpen,
            () => viewModel.Gallery.IsViewerOpen,
            PhotoViewer);

        WhenPickingEnds(
            viewModel.People.Reassign,
            () => viewModel.People.Reassign.IsOpen,
            () => viewModel.People.IsInspecting,
            FaceInspector);

        // The album list was the one that did not, and it is the one people
        // reach for most: putting a photograph in an album left the arrows dead
        // where naming a face in the same viewer did not.
        WhenPickingEnds(
            viewModel.Gallery.Albums,
            () => viewModel.Gallery.Albums.IsOpen,
            () => viewModel.Gallery.IsViewerOpen,
            PhotoViewer);
    }

    /// <summary>
    /// Returns the focus to the picture behind a list once the list closes.
    /// </summary>
    /// <remarks>
    /// The name picker and the album picker share no type - they answer about
    /// different things, and the pair is deliberately not one class - so this
    /// takes anything that raises changes and is told how to read it. Both spell
    /// the property <c>IsOpen</c>, which is what the name below is naming.
    /// </remarks>
    private void WhenPickingEnds(
        INotifyPropertyChanged picker,
        Func<bool> isOpen,
        Func<bool> stillOpen,
        IInputElement behind) =>
        picker.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PersonPicker.IsOpen) && !isOpen() && stillOpen())
            {
                Dispatcher.BeginInvoke(() => behind.Focus());
            }
        };

    /// <summary>
    /// Whether the side nav's names and counts are in the layout.
    /// </summary>
    /// <remarks>
    /// Separate from the view model's fold, because the two change at different
    /// moments. Folding takes them out at once, so the star column absorbs the
    /// whole shrink and nothing inside the pane reflows. Unfolding puts them back
    /// only once the pane has finished opening: reinstating them at the start
    /// would have every name re-trim its own ellipsis for 180ms and drag its
    /// count a hundred pixels across the item.
    /// </remarks>
    public static readonly DependencyProperty NavLabelsVisibleProperty =
        DependencyProperty.Register(
            nameof(NavLabelsVisible),
            typeof(bool),
            typeof(MainWindow),
            new PropertyMetadata(true));

    public bool NavLabelsVisible
    {
        get => (bool)GetValue(NavLabelsVisibleProperty);
        set => SetValue(NavLabelsVisibleProperty, value);
    }

    /// <summary>
    /// Puts the nav at the width the current state calls for.
    /// </summary>
    /// <remarks>
    /// The stored fold goes back on without animating: a slide on every launch
    /// is noise rather than feedback, and the library is already open before
    /// this window is built, so the value is right from the first frame.
    ///
    /// <para>A held animation outranks a local value for good, so both are
    /// cleared before Width is set outright. Nothing else in this file may
    /// assign <c>NavigationBar.Width</c> - once an animation has run, an
    /// assignment is silently ignored, and the symptom is a nav that restores at
    /// the wrong width with nothing to point at.</para>
    /// </remarks>
    private void ApplyNavigationWidth(bool collapsed, bool animate)
    {
        var collapse = (Storyboard)Resources["NavCollapse"];
        var expand = (Storyboard)Resources["NavExpand"];

        if (animate)
        {
            if (collapsed)
            {
                NavLabelsVisible = false;
                collapse.Begin(this, HandoffBehavior.SnapshotAndReplace, isControllable: true);
                return;
            }

            // Completed is raised when this animation is displaced as well as
            // when it finishes, so a fold that interrupts the unfold arrives
            // here with the pane already on its way back to 52. Without the
            // check it would put the names back inside the rail, where the star
            // column is nothing and only the clipped count survives. The fold
            // has the last word.
            void OnExpanded(object? sender, EventArgs e)
            {
                expand.Completed -= OnExpanded;

                if (!_viewModel.IsNavCollapsed)
                {
                    NavLabelsVisible = true;
                }
            }

            expand.Completed += OnExpanded;
            expand.Begin(this, HandoffBehavior.SnapshotAndReplace, isControllable: true);
            return;
        }

        collapse.Remove(this);
        expand.Remove(this);
        NavigationBar.BeginAnimation(WidthProperty, null);
        NavigationBar.Width = collapsed ? NavLayout.CollapsedWidth : NavLayout.ExpandedWidth;
        NavLabelsVisible = !collapsed;
    }

    /// <summary>
    /// Settles the nav at the width the window opens at.
    /// </summary>
    /// <remarks>
    /// Here rather than in the constructor, and the view model is listened to
    /// only afterwards, so that whichever way the size arrived the first width
    /// is put on without a slide - <c>SizeChanged</c> fires during the first
    /// layout pass, before this.
    /// </remarks>
    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        _viewModel.AdaptNavigationToWidth(ActualWidth);
        ApplyNavigationWidth(_viewModel.IsNavCollapsed, animate: false);

        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.IsNavCollapsed))
            {
                ApplyNavigationWidth(_viewModel.IsNavCollapsed, animate: true);
            }
        };
    }

    /// <remarks>
    /// <c>Window.SizeChanged</c> is a direct event, so this cannot be reached by
    /// the four descendants that raise it too. The width guard is for the drag
    /// that only changes the height.
    /// </remarks>
    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged)
        {
            _viewModel.AdaptNavigationToWidth(e.NewSize.Width);
        }
    }

    /// <summary>
    /// Re-flows the grid when the window changes width, keeping the picture the
    /// user was looking at under their eye.
    /// </summary>
    /// <remarks>
    /// Rows are re-chunked only when the whole number of columns changes, so a
    /// live drag crosses a boundary a handful of times rather than once per
    /// mouse move.
    /// </remarks>
    private void OnGallerySizeChanged(object sender, SizeChangedEventArgs e)
    {
        // How many pictures are on screen is what the decoder works in, and the
        // window's height is the only thing that says. Set before the width
        // check, because making the window taller changes it on its own.
        _viewModel.Gallery.SetVisibleRows(
            GalleryLayout.RowsOnScreen(e.NewSize.Height, _viewModel.Gallery.CellSize));

        if (!e.WidthChanged)
        {
            return;
        }

        double cellSize = _viewModel.Gallery.CellSize;
        int columns = GalleryLayout.ColumnsFor(e.NewSize.Width, cellSize);
        if (columns == _viewModel.Gallery.Columns)
        {
            return;
        }

        ScrollViewer? scroller = FindScrollViewer(GalleryGrid);
        int firstItem = scroller is null
            ? 0
            : GalleryLayout.FirstItemAt(scroller.VerticalOffset, _viewModel.Gallery.Columns, cellSize);

        _viewModel.Gallery.SetColumns(columns);

        scroller?.ScrollToVerticalOffset(GalleryLayout.OffsetOf(firstItem, columns, cellSize));
    }

    /// <summary>
    /// Ctrl and the wheel zoom the grid. The wheel on its own is left untouched,
    /// so it still scrolls the list.
    /// </summary>
    /// <remarks>
    /// Marked handled only when the modifier is down. Without that the same notch
    /// would both zoom and scroll, and the picture the zoom had just anchored
    /// would slide out from under the pointer.
    /// </remarks>
    private void OnGalleryMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control)
        {
            return;
        }

        e.Handled = true;

        GalleryViewModel gallery = _viewModel.Gallery;
        double cellSize = GalleryLayout.Zoomed(gallery.CellSize, e.Delta);
        if (cellSize == gallery.CellSize)
        {
            return;
        }

        ScrollViewer? scroller = FindScrollViewer(GalleryGrid);
        int firstItem = scroller is null
            ? 0
            : GalleryLayout.FirstItemAt(scroller.VerticalOffset, gallery.Columns, gallery.CellSize);

        gallery.SetCellSize(cellSize);
        int columns = GalleryLayout.ColumnsFor(GalleryGrid.ActualWidth, cellSize);
        gallery.SetColumns(columns);

        scroller?.ScrollToVerticalOffset(GalleryLayout.OffsetOf(firstItem, columns, cellSize));

        // The viewport now holds a different number of tiles, and a zoom does not
        // always move the scroll offset, so the decode is asked for by name rather
        // than waiting for a scroll event that may never arrive.
        _ = gallery.ShowRangeAsync(firstItem);
        _ = _viewModel.RememberCellSizeAsync();
    }

    /// <summary>
    /// Keeps the decoded pictures following the viewport as the user scrolls.
    /// </summary>
    /// <remarks>
    /// A change in the extent alone is not a scroll. WPF estimates a virtualised
    /// list's height from the rows realised so far and refines the estimate as
    /// more of them realise, raising a scroll event each time at a complete
    /// standstill - and asking for a range on those restarted the wait for the
    /// viewport to settle over and over. Dragging the bar to a position away from
    /// the top left the grid grey for exactly that reason: the estimate was still
    /// being refined there, so the wait never finished.
    /// </remarks>
    private void OnGalleryScrolled(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange == 0)
        {
            return;
        }

        GalleryViewModel gallery = _viewModel.Gallery;
        gallery.SetVisibleRows(GalleryLayout.RowsOnScreen(e.ViewportHeight, gallery.CellSize));

        // The raw numbers, because everything about which pictures get decoded
        // is derived from them and an estimated extent is the one input here
        // that cannot be trusted.
        int first = FirstVisibleItem(GalleryGrid, gallery.TotalCount);

        DiagnosticLog.Write(
            $"scrolled to {e.VerticalOffset:F0} of {e.ExtentHeight:F0} "
            + $"(viewport {e.ViewportHeight:F0}, {gallery.Columns} across, showing {first})");

        // Unknown means the rows could not be asked - mid-layout, or a container
        // detached while it was being walked. Leaving the window where it is
        // costs one scroll event; guessing the top would send the decoder to the
        // wrong end of the library.
        if (first >= 0)
        {
            _ = gallery.ShowRangeAsync(first);
        }
    }

    /// <summary>
    /// The same re-flow for one person's pictures, without the zoom.
    /// </summary>
    /// <remarks>
    /// No Ctrl and wheel here. The pixel-to-index helpers the zoom anchors with
    /// assume every row is a full one, and a heading starts a short row - so the
    /// grid is re-anchored by scrolling a row into view rather than by
    /// arithmetic on offsets.
    /// </remarks>
    private void OnPersonPhotosSizeChanged(object sender, SizeChangedEventArgs e)
    {
        PeopleViewModel people = _viewModel.People;
        double cellSize = _viewModel.Gallery.CellSize;

        people.SetVisibleRows(GalleryLayout.RowsOnScreen(e.NewSize.Height, cellSize));

        if (!e.WidthChanged)
        {
            return;
        }

        int columns = GalleryLayout.ColumnsFor(e.NewSize.Width, cellSize);
        if (columns == people.Columns)
        {
            return;
        }

        // Nothing to keep in view on the grid's first layout, when it grows from
        // nothing to full size - and asking a half-realised list where it is
        // answers with whatever row happens to exist. Re-anchoring on that
        // opened every person two thirds of the way down their pictures.
        bool firstLayout = e.PreviousSize.Width == 0 || e.PreviousSize.Height == 0;
        int firstItem = firstLayout
            ? 0
            : Math.Max(0, FirstVisibleItem(PersonPhotoGrid, people.PhotoCount));

        people.SetColumns(columns);

        GalleryRow? landing = people.PhotoRows
            .LastOrDefault(row => row.FirstIndex <= firstItem);
        if (landing is not null)
        {
            PersonPhotoGrid.ScrollIntoView(landing);
        }
    }

    /// <summary>
    /// Keeps the open album's grid in step with its own size, exactly as
    /// the People grid does.
    /// </summary>
    private void OnAlbumPhotosSizeChanged(object sender, SizeChangedEventArgs e)
    {
        AlbumsViewModel albums = _viewModel.Albums;
        double cellSize = _viewModel.Gallery.CellSize;

        albums.SetVisibleRows(GalleryLayout.RowsOnScreen(e.NewSize.Height, cellSize));

        if (!e.WidthChanged)
        {
            return;
        }

        int columns = GalleryLayout.ColumnsFor(e.NewSize.Width, cellSize);
        if (columns == albums.Columns)
        {
            return;
        }

        bool firstLayout = e.PreviousSize.Width == 0 || e.PreviousSize.Height == 0;
        int firstItem = firstLayout
            ? 0
            : Math.Max(0, FirstVisibleItem(AlbumPhotoGrid, albums.PhotoCount));

        albums.SetColumns(columns);

        GalleryRow? landing = albums.PhotoRows
            .LastOrDefault(row => row.FirstIndex <= firstItem);
        if (landing is not null)
        {
            AlbumPhotoGrid.ScrollIntoView(landing);
        }
    }

    private void OnAlbumPhotosScrolled(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange == 0)
        {
            return;
        }

        AlbumsViewModel albums = _viewModel.Albums;
        albums.SetVisibleRows(
            GalleryLayout.RowsOnScreen(e.ViewportHeight, _viewModel.Gallery.CellSize));

        _ = albums.ShowRangeAsync(
            FirstVisibleItem(AlbumPhotoGrid, albums.PhotoCount));
    }

    private void OnPersonPhotosScrolled(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange == 0)
        {
            return;
        }

        PeopleViewModel people = _viewModel.People;
        people.SetVisibleRows(
            GalleryLayout.RowsOnScreen(e.ViewportHeight, _viewModel.Gallery.CellSize));

        int first = FirstVisibleItem(PersonPhotoGrid, people.PhotoCount);
        if (first >= 0)
        {
            _ = people.ShowRangeAsync(first);
        }
    }

    /// <summary>
    /// Commits the year of birth when the box is left, so a year typed and then
    /// clicked away from is not quietly discarded.
    /// </summary>
    private void OnBirthYearCommitted(object sender, RoutedEventArgs e)
    {
        if (_viewModel.People.SetBirthYearCommand.CanExecute(null))
        {
            _viewModel.People.SetBirthYearCommand.Execute(null);
        }
    }

    /// <summary>
    /// Which picture the top of the viewport is showing.
    /// </summary>
    /// <remarks>
    /// Asked of the rows the list has actually realised, and not worked out from
    /// the scroll offset. A virtualised list does not know how tall it is: WPF
    /// estimates the extent from the rows realised so far and revises the
    /// estimate constantly. Measured on this library while scrolling, it swung
    /// between 44,575 and 46,543 pixels within the same second, and the reported
    /// offset swung a thousand pixels with it - for the same pictures on screen.
    /// A position derived from a proportion of that jumped by seventy pictures
    /// at a standstill, so each decode was abandoned by the next estimate and
    /// the grid never filled at all.
    ///
    /// <para>The realised rows cannot be wrong in that way. They are the ones
    /// being drawn.</para>
    /// </remarks>
    /// <returns>The index, or -1 when the rows cannot be asked.</returns>
    private static int FirstVisibleItem(ListBox grid, int totalCount)
    {
        if (totalCount <= 0)
        {
            return 0;
        }

        if (FindItemsPanel(grid) is not Panel panel)
        {
            return -1;
        }

        try
        {
            foreach (UIElement child in panel.Children)
            {
                if (child.Visibility != Visibility.Visible)
                {
                    continue;
                }

                Rect box = child.TransformToAncestor(grid)
                    .TransformBounds(new Rect(child.RenderSize));

                // The first row with any part of itself below the top edge. A
                // recycled container that has not been positioned yet reports an
                // empty box and is skipped by the same test.
                if (box.Height > 0 && box.Bottom > 1)
                {
                    // The row says where it starts. Multiplying the row number by
                    // the number across assumed every row was full, which stopped
                    // being true when a heading began a new one - and a window
                    // pointed at the wrong pictures is a screen of grey.
                    return grid.ItemContainerGenerator.ItemFromContainer(child)
                        is Gallery.GalleryRow row
                        ? row.FirstIndex
                        : -1;
                }
            }
        }
        catch (InvalidOperationException)
        {
            // A container detached mid-walk, which a recycling list does. The
            // next scroll event asks again.
            return -1;
        }

        return -1;
    }

    private static Panel? FindItemsPanel(DependencyObject root)
    {
        if (root is VirtualizingStackPanel found)
        {
            return found;
        }

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            if (FindItemsPanel(VisualTreeHelper.GetChild(root, i)) is Panel child)
            {
                return child;
            }
        }

        return null;
    }

    private void OnFolderSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not FolderNode folder)
        {
            return;
        }

        _viewModel.Gallery.SelectedFolder = folder;
        _ = _viewModel.Gallery.LoadAsync();
    }

    /// <summary>
    /// Arrow keys and Escape over the open picture.
    /// </summary>
    /// <remarks>
    /// Left and right walk the library and Escape goes back to the grid. Bound
    /// to the picture rather than to the window so they only apply while one is
    /// actually open.
    ///
    /// <para>While either list is up the arrows belong to it instead - they are
    /// moving a caret through what is being typed - so this stands down
    /// entirely. Taking them here would have typing a name quietly walk the
    /// library. The album list was left out of that when it was added, so typing
    /// an album name walked the photographs underneath it, and each step closed
    /// the list, because opening another picture closes it.</para>
    ///
    /// <para>Escape while a list is up is not decided here at all: the window
    /// puts the list down before this runs. See <see cref="OnWindowKeyDown"/>,
    /// which is the one place that knows what Escape closes.</para>
    /// </remarks>
    private void OnViewerKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel.Gallery.Picker.IsOpen || _viewModel.Gallery.Albums.IsOpen)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Left:
                _viewModel.Gallery.PreviousPhotoCommand.Execute(null);
                break;
            case Key.Right:
                _viewModel.Gallery.NextPhotoCommand.Execute(null);
                break;
            case Key.Escape:
                _viewModel.Gallery.ClosePhotoCommand.Execute(null);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer found)
        {
            return found;
        }

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            if (FindScrollViewer(VisualTreeHelper.GetChild(root, i)) is ScrollViewer child)
            {
                return child;
            }
        }

        return null;
    }

    /// <summary>
    /// Browse only fills the box; adding is a separate, explicit step. That way
    /// a network path the picker mishandles can still be corrected by hand
    /// before anything is committed.
    /// </summary>
    private void OnBrowseSourceClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose a folder that holds photos",
        };

        string current = _viewModel.NewSourcePath.Trim();
        if (current.Length > 0 && Directory.Exists(current))
        {
            dialog.InitialDirectory = current;
        }

        if (dialog.ShowDialog(this) == true)
        {
            // Setting the path clears the last failure, so a stale red line
            // cannot sit beside the folder that has just been picked.
            _viewModel.NewSourcePath = dialog.FolderName;
        }
    }

    /// <summary>
    /// Forgets the remembered library and restarts, so the next launch asks
    /// which folder to open.
    /// </summary>
    /// <remarks>
    /// The app goes straight back to the last library rather than asking every
    /// time, so this is the way to change your mind. It restarts rather than
    /// rebuilding the container in place: every service, the database connection
    /// and the whole view model are bound to one working folder, and swapping
    /// that underneath a running window is a great deal of machinery for
    /// something done once in a blue moon.
    /// </remarks>
    /// <summary>
    /// Forgets a person, after saying what that costs.
    /// </summary>
    /// <remarks>
    /// Confirmed first because the number can be large - one person here carries
    /// 595 confirmations - and the work of building that up is not obvious from
    /// a row that says "Remove". The faces themselves are never touched; only
    /// the name is, so the pictures are all still there and simply unnamed.
    /// </remarks>
    private async void OnRemovePersonClicked(object sender, RoutedEventArgs e)
    {
        if (_confirming || sender is not FrameworkElement { DataContext: PersonItem person })
        {
            return;
        }

        _confirming = true;
        try
        {
            string counted = person.Summary.ConfirmedFaces == 0
                ? "No faces have been named as them yet."
                : $"{person.Summary.ConfirmedFaces:N0} faces are named as them. "
                  + "Those faces stay in your pictures and become unnamed again.";

            // Caution, and the same tone as removing a folder: both leave every
            // file alone and undo work this app did, which is what that tone is
            // for. They read as one kind of act because they are one.
            bool answer = AppDialog.Confirm(
                this,
                $"Remove {person.DisplayName}?",
                $"{counted}\n\nNothing is deleted from your photos.",
                confirm: "Remove",
                tone: DialogTone.Caution);

            if (answer)
            {
                await _viewModel.People.ForgetPersonCommand.ExecuteAsync(person);
            }
        }
        finally
        {
            _confirming = false;
        }
    }

    /// <summary>
    /// Keeps the face boxes over the faces when the picture is redrawn.
    /// </summary>
    /// <remarks>
    /// The detector worked in the cached preview's pixels; the picture on screen
    /// is whatever the window allows, and it changes with every resize and every
    /// switch between windowed and maximised.
    /// </remarks>
    private void OnPhotoAreaSizeChanged(object sender, SizeChangedEventArgs e) =>
        _viewModel.Gallery.LayoutFaces(e.NewSize.Width, e.NewSize.Height);

    /// <summary>The same, for the one face being asked about on the People screen.</summary>
    private void OnInspectAreaSizeChanged(object sender, SizeChangedEventArgs e) =>
        _viewModel.People.LayoutInspected(e.NewSize.Width, e.NewSize.Height);

    /// <summary>
    /// Left and right walk the review queue; Escape goes back to the faces.
    /// </summary>
    /// <remarks>
    /// The same keys the photo viewer uses, because it is the same gesture -
    /// looking at one picture and moving to the next.
    /// </remarks>
    /// <summary>
    /// Arrow keys and Escape over one copy of a duplicated picture, so two of
    /// them can be flicked between rather than clicked between.
    /// </summary>
    /// <summary>
    /// Escape puts down whatever is floating over the app.
    /// </summary>
    /// <remarks>
    /// One handler for every panel rather than an Escape branch inside each
    /// screen's key handler. The album panels had no such branch and could only
    /// be left by finding their Cancel button - which on a short window is below
    /// the fold of a panel the user cannot see the bottom of.
    ///
    /// <para>Tunnelling on the window, so it runs before whatever holds the
    /// focus inside the panel. A half-typed name in a text box must not swallow
    /// the key that dismisses the panel the box is in.</para>
    ///
    /// <para>Nothing is handled when no panel is open, so the key still reaches
    /// the viewer and the two inspectors, where Escape closes the picture
    /// itself.</para>
    /// </remarks>
    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        foreach ((Func<bool> isOpen, ICommand close) in Dismissible())
        {
            if (isOpen())
            {
                close.Execute(null);
                e.Handled = true;
                return;
            }
        }
    }

    /// <summary>
    /// Everything Escape may put down, nearest the front first.
    /// </summary>
    /// <remarks>
    /// The lists drawn over a picture come before the panels behind them, so a
    /// name list open over the album panel closes the list and leaves the panel.
    ///
    /// <para>The long pass is deliberately absent. It is work already running
    /// rather than a question waiting to be answered, and it carries a Stop
    /// button that says what stopping it means - one of those passes deletes
    /// photographs, and abandoning it halfway should cost more than a keystroke
    /// somebody meant for something else.</para>
    /// </remarks>
    private IEnumerable<(Func<bool> IsOpen, ICommand Close)> Dismissible()
    {
        GalleryViewModel gallery = _viewModel.Gallery;
        PeopleViewModel people = _viewModel.People;
        AlbumsViewModel albums = _viewModel.Albums;

        yield return (() => gallery.Picker.IsOpen, gallery.Picker.CancelCommand);
        yield return (() => gallery.Albums.IsOpen, gallery.Albums.CancelCommand);
        yield return (() => people.Reassign.IsOpen, people.Reassign.CancelCommand);
        yield return (() => albums.IsEditing, albums.CancelEditCommand);
        yield return (() => albums.IsCreating, albums.CancelCreateCommand);
        yield return (
            () => albums.Collections.IsPicking, albums.Collections.CancelPickingCommand);
        yield return (
            () => albums.Collections.IsNaming, albums.Collections.CancelNamingCommand);
    }

    private void OnDuplicateInspectorKeyDown(object sender, KeyEventArgs e)
    {
        DuplicatesViewModel duplicates = _viewModel.Duplicates;

        switch (e.Key)
        {
            case Key.Left:
                duplicates.InspectPreviousCommand.Execute(null);
                break;
            case Key.Right:
                duplicates.InspectNextCommand.Execute(null);
                break;
            case Key.Escape:
                duplicates.CloseInspectCommand.Execute(null);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void OnInspectorKeyDown(object sender, KeyEventArgs e)
    {
        PeopleViewModel people = _viewModel.People;

        // The name list has first claim on these while it is up - see the photo
        // viewer's handler for why. Escape is not among them: the window put the
        // list down before this ran.
        if (people.Reassign.IsOpen)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Left:
                people.InspectPreviousCommand.Execute(null);
                break;
            case Key.Right:
                people.InspectNextCommand.Execute(null);
                break;
            case Key.Escape:
                people.CloseInspectCommand.Execute(null);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    /// <summary>
    /// Offers the names available the moment the search box is clicked into, so
    /// an empty box answers "who can I look for?" without anything being typed.
    /// </summary>
    private void OnSearchFocused(object sender, KeyboardFocusChangedEventArgs e) =>
        _viewModel.Gallery.OpenSearchCommand.Execute(null);

    /// <summary>
    /// Points the app at face model files the user already has.
    /// </summary>
    /// <remarks>
    /// A folder rather than two file pickers: both weights are always kept
    /// together, and asking twice for one installation is a worse question. The
    /// files are checked against their known digests before either is kept, so a
    /// wrong folder is reported rather than half-installed.
    /// </remarks>
    /// <summary>
    /// Follows a link written inside a sentence.
    /// </summary>
    /// <remarks>
    /// The same shell launch every other address in this app uses: the address
    /// is handed to Windows and opened in whichever browser the user already
    /// chose, so this app still makes no request of its own.
    /// </remarks>
    private void OnLinkRequested(object sender, RequestNavigateEventArgs e) =>
        PageInBrowser.Open(e.Uri.ToString());

    /// <summary>
    /// Keeps the model files somewhere else from now on.
    /// </summary>
    /// <remarks>
    /// Nothing is moved. Whatever is in the old folder stays there, and if the
    /// new one already holds the files the features come back on immediately -
    /// which is the point, for a second library that should not mean a second
    /// 1.9 GB.
    /// </remarks>
    private async void OnChooseModelsFolderClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose where to keep the model files",

            // A folder that is not there yet makes the picker refuse to open,
            // and it refuses silently - which is exactly what "the button does
            // nothing" was. The models folder inside a library is only made
            // when something is put in it, so on a library that has never had
            // a model this was every time.
            InitialDirectory = Directory.Exists(_viewModel.Models.Folder)
                ? _viewModel.Models.Folder
                : string.Empty,
        };

        // Traced, because the failure this had was invisible from the outside:
        // no dialog, no message, no record of having been asked.
        DiagnosticLog.Write($"models folder: picker opening at '{dialog.InitialDirectory}'");

        bool chosen = dialog.ShowDialog(this) == true;
        DiagnosticLog.Write(
            chosen ? $"models folder: chose '{dialog.FolderName}'" : "models folder: cancelled");

        if (chosen)
        {
            await _viewModel.Models.ChooseFolderAsync(dialog.FolderName);
        }
    }

    /// <summary>
    /// Chooses the folder this library shares answers through.
    /// </summary>
    /// <remarks>
    /// The view model refuses a folder that overlaps a photo source, in either
    /// direction, and says why. Nothing is validated here: a picker that decided
    /// for itself which folders were allowed would be a second copy of a rule
    /// that already exists, and the two would drift.
    /// </remarks>
    private async void OnChooseSharedFolderClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose a folder every computer in the house can reach",

            // A folder that is not there any more makes the picker refuse to
            // open, and it refuses silently - which looks exactly like a button
            // that does nothing.
            InitialDirectory = Directory.Exists(_viewModel.Sharing.Folder)
                ? _viewModel.Sharing.Folder
                : string.Empty,
        };

        if (dialog.ShowDialog(this) == true)
        {
            await _viewModel.Sharing.ChooseFolderAsync(dialog.FolderName);
        }
    }

    /// <summary>
    /// Re-reads the model folder when the window comes back to the front.
    /// </summary>
    /// <remarks>
    /// Which is exactly when a download has finished: the user has been in a
    /// browser or in Explorer and has just come back. Doing it here is why there
    /// is no "check again" button - that button only ever asked for something
    /// that could be noticed.
    /// </remarks>
    private void OnWindowActivated(object? sender, EventArgs e)
    {
        if (_viewModel.ShowSettings && _viewModel.ShowsSearchSettings)
        {
            _ = _viewModel.Models.RefreshAsync();
        }
    }

    /// <summary>
    /// Shows the original file in Explorer, with it already selected.
    /// </summary>
    /// <remarks>
    /// The app never opens or alters the original - it works from the small
    /// copies in the working folder - so the way to look at the real thing is to
    /// hand it to Explorer and stop there. Selecting rather than opening leaves
    /// what to do with it entirely to the user.
    ///
    /// <para>The path comes from the button's own tag, so the viewer and the
    /// face inspector share this without either knowing about the other.</para>
    /// </remarks>
    private void OnShowInExplorerClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string path })
        {
            OriginalInExplorer.Show(path);
        }
    }

    /// <summary>
    /// Plays the open video, or says why it cannot be played.
    /// </summary>
    /// <remarks>
    /// The picture on screen is a still Photo Gallery keeps on this machine, so
    /// a video whose folder is disconnected looks entirely present until
    /// somebody asks to watch it. That is the moment worth being plain about.
    /// </remarks>
    private void OnPlayVideoClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string path })
        {
            OriginalVideo.Play(path);
        }
        else
        {
            // The binding gave nothing, which is itself an answer worth saying
            // out loud rather than a click that does nothing.
            OriginalVideo.Play(null);
        }
    }

    /// <summary>
    /// Names the clip, then tells the element to play it.
    /// </summary>
    /// <remarks>
    /// Both halves, in that order, and the second is not optional.
    /// <c>LoadedBehavior="Manual"</c> stops a video starting merely because
    /// somebody looked at it - but it also means giving the element a source
    /// does not begin playback, and relying on <see cref="OnVideoOpened"/> alone
    /// left it rendering the first frame and sitting there. That looks exactly
    /// like a clip playing something very still, which is the worst kind of
    /// broken: it does not look broken.
    ///
    /// <para>The command sets the source through the binding, which applies
    /// before this returns, so <c>Play</c> has something to act on.</para>
    /// </remarks>
    private void OnStartVideoClicked(object sender, RoutedEventArgs e)
    {
        _viewModel.Gallery.PlayVideoCommand.Execute(null);

        DiagnosticLog.Write(
            $"play asked: path={_viewModel.Gallery.PlayingPath ?? "<none>"} "
            + $"playing={_viewModel.Gallery.IsPlayingVideo} "
            + $"error={_viewModel.Gallery.PlaybackError ?? "<none>"}");

        // Back to the start. A clip that has been watched through is sitting at
        // its end, and playing from there is a video that does nothing - which
        // is what "watch it again" would have looked like now that stopping no
        // longer throws the source away.
        if (VideoPlayer.Source is not null)
        {
            VideoPlayer.Position = TimeSpan.Zero;
            _viewModel.Gallery.PlaybackSeconds = 0d;
            _seekTimer.Start();
        }

        VideoPlayer.Play();
    }

    /// <summary>
    /// Starts the clip again once the element reports the source open.
    /// </summary>
    /// <remarks>
    /// The other half of <see cref="OnStartVideoClicked"/>, for the case where
    /// the source had not finished opening when the click landed - a file on a
    /// share, which is all of them here. Calling Play twice is harmless.
    /// </remarks>
    private void OnVideoOpened(object sender, RoutedEventArgs e)
    {
        DiagnosticLog.Write(
            $"video opened: natural={VideoPlayer.NaturalVideoWidth}x"
            + $"{VideoPlayer.NaturalVideoHeight} length="
            + $"{(VideoPlayer.NaturalDuration.HasTimeSpan ? VideoPlayer.NaturalDuration.TimeSpan.ToString() : "<unknown>")}");

        // Only if somebody actually asked. This fires for a clip that was merely
        // warmed when its picture was opened, and a video that starts itself
        // because you looked at it is the thing Manual exists to prevent.
        if (_viewModel.Gallery.IsPlayingVideo)
        {
            // Play first, and work out which way up afterwards. The other order
            // cost an evening: anything that throws while measuring would leave
            // the clip opened and never started, which looks exactly like a play
            // button that does nothing.
            VideoPlayer.Play();
            _viewModel.Gallery.PlaybackSeconds = 0d;
            _seekTimer.Start();
        }

        _viewModel.Gallery.PlaybackLengthSeconds = VideoPlayer.NaturalDuration.HasTimeSpan
            ? VideoPlayer.NaturalDuration.TimeSpan.TotalSeconds
            : 0d;

        try
        {
            // The stream's own shape, which is the only place the rotation shows
            // up - MediaElement will not report what the container's flag says,
            // so the still is the reference for which way up this clip goes.
            if (VideoPlayer.NaturalVideoWidth > 0 && VideoPlayer.NaturalVideoHeight > 0
                && _viewModel.Gallery.OpenPicture is ImageSource still
                && still.Width > 0 && still.Height > 0)
            {
                _viewModel.Gallery.AlignPlaybackTo(
                    streamIsPortrait:
                        VideoPlayer.NaturalVideoHeight > VideoPlayer.NaturalVideoWidth,
                    stillIsPortrait: still.Height > still.Width);
            }
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException)
        {
            // A picture that will not say how big it is leaves the clip playing
            // the way it was recorded, which is the answer for most of them.
            DiagnosticLog.Write("could not measure the still to orient the video", ex);
        }
    }

    /// <summary>
    /// Stops the clip and puts its still back.
    /// </summary>
    /// <remarks>
    /// The element is stopped here as well as having its source cleared. Clearing
    /// alone does stop it, but it leaves the last position behind, so playing the
    /// same clip again would carry on from where it was rather than starting.
    /// </remarks>
    /// <summary>
    /// Moves the bar along while the clip runs.
    /// </summary>
    /// <remarks>
    /// A timer because <c>MediaElement</c> raises nothing as it plays - it will
    /// say when it opened and when it ended and nothing in between, so the only
    /// way to know where it is is to ask. Four times a second: often enough that
    /// the bar looks continuous, rarely enough to cost nothing.
    /// </remarks>
    private readonly System.Windows.Threading.DispatcherTimer _seekTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(250),
    };

    private void OnSeekTick(object? sender, EventArgs e)
    {
        // Not while the user has hold of it, or the thumb springs back out from
        // under them every quarter second.
        if (_viewModel.Gallery.IsScrubbing)
        {
            return;
        }

        _viewModel.Gallery.PlaybackSeconds = VideoPlayer.Position.TotalSeconds;
    }

    private void OnSeekStarted(object sender, RoutedEventArgs e) =>
        _viewModel.Gallery.IsScrubbing = true;

    private void OnSeekFinished(object sender, RoutedEventArgs e)
    {
        _viewModel.Gallery.IsScrubbing = false;
        VideoPlayer.Position = TimeSpan.FromSeconds(VideoSeek.Value);
    }

    /// <summary>
    /// Jumps to a point clicked on the bar rather than dragged to.
    /// </summary>
    /// <remarks>
    /// <c>IsMoveToPointEnabled</c> moves the thumb on a click but raises no drag,
    /// so without this the bar would move and the picture would not.
    /// </remarks>
    private void OnSeekClicked(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.Gallery.IsScrubbing)
        {
            VideoPlayer.Position = TimeSpan.FromSeconds(VideoSeek.Value);
        }
    }

    private void OnStopVideoClicked(object sender, RoutedEventArgs e)
    {
        _seekTimer.Stop();

        // Pause rather than Stop, and leave the source where it is. Stop resets
        // the element and the next play would fetch the clip over the share
        // again; the view model keeps the path for exactly that reason.
        VideoPlayer.Pause();
        _viewModel.Gallery.StopVideo();
    }

    /// <summary>
    /// Puts the still back when the clip finishes.
    /// </summary>
    /// <remarks>
    /// Better than a frozen last frame, which looks like the app has hung. The
    /// poster returns with its play badge, so watching it again is one click.
    /// </remarks>
    private void OnVideoEnded(object sender, RoutedEventArgs e)
    {
        DiagnosticLog.Write(
            $"video ended: pos={VideoPlayer.Position} natural="
            + $"{VideoPlayer.NaturalVideoWidth}x{VideoPlayer.NaturalVideoHeight}");

        _seekTimer.Stop();
        VideoPlayer.Pause();
        _viewModel.Gallery.StopVideo();
    }

    /// <summary>
    /// Says so plainly when this machine cannot decode the container.
    /// </summary>
    /// <remarks>
    /// The honest failure for a feature built on whatever codecs Windows has:
    /// the same reason a handful of clips get no poster. The viewer swaps the
    /// badge for a line saying it cannot be played here and a button that hands
    /// the file to whatever the machine does use, so the answer is never a black
    /// rectangle.
    /// </remarks>
    private void OnVideoFailed(object sender, ExceptionRoutedEventArgs e)
    {
        DiagnosticLog.Write("video failed in the viewer", e.ErrorException);
        _seekTimer.Stop();
        _viewModel.Gallery.ReportPlaybackFailed();
    }

    /// <summary>
    /// Deletes the open photograph, after saying exactly what that costs.
    /// </summary>
    /// <remarks>
    /// The only thing in this app that destroys something the user cannot get
    /// back, so the question names the file, its full path, and how many
    /// confirmed names go with it - and says outright whether the Recycle Bin
    /// will catch it. On a network share it will not, and a dialog implying an
    /// undo that does not exist would be the worst possible wording.
    ///
    /// <para>Cancel is the default button, so a stray Return or Space at the
    /// wrong moment can never delete a photograph.</para>
    /// </remarks>
    private async void OnDeletePhotoClicked(object sender, RoutedEventArgs e)
    {
        async Task<IReadOnlyList<PhotoToRemove>> Open() =>
            await _viewModel.Gallery.DescribeDeletionAsync() is PhotoToRemove photo
                ? [photo]
                : [];

        await ConfirmAndDeleteAsync(Open, _viewModel.Gallery.AfterOpenPhotoDeletedAsync);
    }

    /// <summary>
    /// Deletes the photograph a proposal was found in, from the review screen.
    /// </summary>
    /// <remarks>
    /// The same question, word for word, as the photo viewer asks. Two wordings
    /// for one irreversible act would be two chances to get it wrong.
    /// </remarks>
    private async void OnDeleteInspectedClicked(object sender, RoutedEventArgs e)
    {
        async Task<IReadOnlyList<PhotoToRemove>> Inspected() =>
            await _viewModel.People.DescribeInspectedDeletionAsync() is PhotoToRemove photo
                ? [photo]
                : [];

        await ConfirmAndDeleteAsync(Inspected, _viewModel.People.AfterInspectedDeletedAsync);
    }

    /// <summary>
    /// Asks before the one thing in this app that cannot be undone, naming the
    /// photographs and what goes with them.
    /// </summary>
    /// <remarks>
    /// One wording for every way a picture can leave this library. Cancel is the
    /// default button, and a share says outright that there is no Recycle Bin to
    /// fall back on - Windows does not recycle a deletion on a network or
    /// removable location, and a dialog implying an undo that does not exist is
    /// the worst thing this could do.
    /// </remarks>
    private bool AgreedToDelete(IReadOnlyList<PhotoToRemove> photos)
    {
        if (photos.Count == 0)
        {
            return false;
        }

        // Every copy in a duplicate group sits on the same share, so one answer
        // covers them all.
        bool recoverable = photos.All(photo => photo.Recoverable);
        int faces = photos.Sum(photo => photo.Faces);
        int names = photos.Sum(photo => photo.Names);

        // The full path only, as the many-photo branch below has always done:
        // it ends in the file name, so naming the file first said it twice.
        string what = photos.Count == 1
            ? photos[0].FullPath
            : string.Join("\n", photos.Take(6).Select(photo => photo.FullPath))
              + (photos.Count > 6 ? $"\n...and {photos.Count - 6:N0} more" : string.Empty);

        string subject = photos.Count == 1 ? "this photo" : $"these {photos.Count:N0} photos";
        string it = photos.Count == 1 ? "it" : "them";

        string forgets = faces == 0
            ? $"Photo Gallery also forgets {it}."
            : names == 0
                ? $"Photo Gallery also forgets {it}, including {faces:N0} faces found in {it}."
                : $"Photo Gallery also forgets {it}, including {faces:N0} faces "
                  + $"and the {names:N0} names confirmed on them.";

        string question = recoverable
            ? $"{what}\n\n"
              + $"The {(photos.Count == 1 ? "file goes" : "files go")} to the Recycle Bin. {forgets}"
            : $"{what}\n\n"
              + $"{(photos.Count == 1 ? "This file is" : "These files are")} on a network or "
              + "removable location, so Windows cannot put "
              + $"{(photos.Count == 1 ? "it" : "them")} in the Recycle Bin. "
              + $"{(photos.Count == 1 ? "It" : "They")} will be deleted permanently and cannot "
              + $"be recovered.\n\n{forgets}";

        return AppDialog.Confirm(
            this,
            recoverable
                ? $"Delete {subject}?"
                : $"Delete {subject} permanently?",
            question,

            // Named, and named differently for the two cases. "Delete
            // permanently" on the button is the last chance to notice that the
            // Recycle Bin is not going to catch this one.
            confirm: recoverable ? "Delete" : "Delete permanently",
            tone: recoverable ? DialogTone.Question : DialogTone.Danger);
    }

    /// <summary>
    /// Deletes every copy in one duplicate group that was not ticked.
    /// </summary>
    /// <remarks>
    /// The same question, word for word, as the photo viewer asks. Several ways
    /// to destroy a photograph would be several chances to word the warning
    /// badly.
    /// </remarks>
    private async void OnDeleteDuplicatesClicked(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is DuplicateSetItem set)
        {
            await ConfirmAndDeleteDuplicatesAsync([set]);
        }
    }

    /// <summary>
    /// Deletes the unchosen copies across every group the user has decided.
    /// </summary>
    /// <remarks>
    /// Groups nobody has touched are not part of it. That is the whole safety of
    /// a button acting on the screen rather than on one group: it can only reach
    /// what somebody has already looked at and made a choice in.
    /// </remarks>
    private async void OnDeleteChosenDuplicatesClicked(object sender, RoutedEventArgs e) =>
        await ConfirmAndDeleteDuplicatesAsync(_viewModel.Duplicates.Chosen);

    private Task ConfirmAndDeleteDuplicatesAsync(IReadOnlyList<DuplicateSetItem> sets) =>
        ConfirmAndDeleteAsync(
            () => _viewModel.Duplicates.DescribeDeletionAsync(sets),
            result => _viewModel.Duplicates.AfterDeletedAsync(sets, result));

    /// <summary>
    /// The one way a photograph leaves this library: work out what it would
    /// cost, ask, delete under the overlay, then let the screen catch up.
    /// </summary>
    /// <remarks>
    /// Four gestures delete in this app - the photo viewer, the face review and
    /// both of the duplicates screen's buttons - and they all come through here,
    /// so the question is worded once and the progress is shown once. What
    /// differs between them is only which pictures they name and what their own
    /// screen has to do afterwards, which is what the two callbacks are.
    ///
    /// <para><c>_confirming</c> covers the whole of it rather than the dialog
    /// alone: working out what four hundred groups would cost takes a moment,
    /// and a second click during it would put the same question twice.</para>
    ///
    /// <para>The source is checked before the question is asked. Deleting is the
    /// one thing here that cannot be undone, and a share that is away makes every
    /// file on it indistinguishable from one that has already gone - so the
    /// honest move is to say so up front rather than take a "yes" for work that
    /// will not happen.</para>
    /// </remarks>
    private async Task ConfirmAndDeleteAsync(
        Func<Task<IReadOnlyList<PhotoToRemove>>> describe,
        Func<PhotoRemovalResult, Task> then)
    {
        if (_confirming)
        {
            return;
        }

        _confirming = true;
        try
        {
            IReadOnlyList<PhotoToRemove> photos = await describe();
            if (photos.Count == 0)
            {
                return;
            }

            IReadOnlyList<string> unreachable = await _viewModel.UnreachableSourcesAsync(photos);
            if (unreachable.Count > 0)
            {
                SaidSourceIsAway(
                    unreachable,
                    photos.Count == 1
                        ? "This photo is stored there, so it could not be deleted."
                        : $"These {photos.Count:N0} photos are stored there, so they could "
                          + "not be deleted.");

                return;
            }

            if (!AgreedToDelete(photos))
            {
                return;
            }

            // Handed in rather than awaited after, so the overlay stays up while
            // the screen catches up. Settling four hundred duplicate groups and
            // re-reading them is ten to twenty seconds, and doing it with the
            // overlay already down left the deleted rows on screen looking as
            // though nothing had happened.
            await _viewModel.DeletePhotosAsync(photos, then);
        }
        finally
        {
            _confirming = false;
        }
    }

    /// <summary>
    /// Answers a turn that was refused because the photograph's folder is away.
    /// </summary>
    private void OnTurnRefusedOutOfReach(object? sender, IReadOnlyList<string> sources) =>
        SaidSourceIsAway(
            sources,
            "This photo is stored there, so its own file could not be told which way up it "
            + "goes. Turning only Photo Gallery's copy would leave the two disagreeing about "
            + "the same picture, so nothing was turned.");

    /// <summary>
    /// Tells the user nothing was touched, and why.
    /// </summary>
    /// <remarks>
    /// One wording for deleting and for turning, because to the user they are
    /// the same event: a thing they asked for did not happen, and the reason is
    /// the folder rather than the photograph.
    ///
    /// <para>Worded to make clear that the pictures are fine. The failure is
    /// this app's view of them, not the photographs themselves, and a message
    /// that only said "could not delete" would leave somebody wondering what
    /// state their library is now in.</para>
    /// </remarks>
    private void SaidSourceIsAway(IReadOnlyList<string> sources, string consequence)
    {
        AppDialog.Tell(
            this,
            sources.Count == 1 ? "That folder cannot be reached" : "Those folders cannot be reached",
            $"{string.Join("\n", sources)}\n\n"
            + $"{consequence}\n\n"
            + $"Photo Gallery cannot see the {(sources.Count == 1 ? "folder" : "folders")} "
            + "right now - a drive that is disconnected, a network share that is switched "
            + "off, or a computer that is asleep.\n\n"
            + "Nothing has been changed. Your photos are untouched, and so is everything "
            + "Photo Gallery knows about them. Reconnect and try again.",
            DialogTone.Caution);
    }

    private void OnSwitchLibraryClicked(object sender, RoutedEventArgs e)
    {
        bool answer = AppDialog.Confirm(
            this,
            "Close this library and choose another?",
            "Photo Gallery will restart and ask which folder to open. Nothing "
            + "in this library is changed or removed.",
            confirm: "Switch library");

        if (!answer)
        {
            return;
        }

        _viewModel.ForgetLibrary();

        Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true });
        System.Windows.Application.Current.Shutdown();
    }

    /// <summary>
    /// Detaching cannot be undone without re-adding and re-reading the folder,
    /// so it asks first, naming the folder. What is about to happen, and that it
    /// leaves the user's own files alone, is said on the overlay that follows.
    /// </summary>
    private void OnDetachSourceClicked(object sender, RoutedEventArgs e)
    {
        if (_confirming || sender is not FrameworkElement { DataContext: PhotoSourceItem item })
        {
            return;
        }

        _confirming = true;
        try
        {
            bool answer = AppDialog.Confirm(
                this,
                "Remove this folder from your library?",
                $"{item.Path}\n\nThe folder and the pictures in it are left exactly "
                + "as they are; only this app stops looking at them.",
                confirm: "Remove folder",
                tone: DialogTone.Caution);

            if (answer)
            {
                _viewModel.RemoveSourceCommand.Execute(item);
            }
        }
        finally
        {
            _confirming = false;
        }
    }

    /// <summary>
    /// Removes one of the user's own albums, after saying what goes with it.
    /// </summary>
    /// <remarks>
    /// Asked for the same reason removing a person is, and in the same tone: no
    /// file is touched, but the name and the rule are somebody's own writing and
    /// there is no undo - they would have to be typed again. The button also
    /// sits directly under Save the rule, which is a very small distance between
    /// keeping an album and losing it.
    ///
    /// <para>What it says first is the thing a person is actually afraid of,
    /// which is that the photographs go with it. The count comes off the album's
    /// own summary rather than the tiles on screen, which are loaded a window at
    /// a time and would say the wrong number for a long album.</para>
    /// </remarks>
    private async void OnRemoveAlbumClicked(object sender, RoutedEventArgs e)
    {
        if (_confirming || _viewModel.Albums.Selected is not AlbumItem album)
        {
            return;
        }

        _confirming = true;
        try
        {
            string counted = album.Summary.PhotoCount switch
            {
                0 => "Nothing is in it yet.",
                1 => "The photograph in it stays in your library, and belongs to no album "
                     + "afterwards.",
                int photos => $"The {photos:N0} photographs in it stay in your library, and "
                              + "belong to no album afterwards.",
            };

            bool answer = AppDialog.Confirm(
                this,
                $"Remove \"{album.Name}\"?",
                $"{counted}\n\nThe album's name and what it looks for go, and cannot be "
                + "brought back.",
                confirm: "Remove album",
                tone: DialogTone.Caution);

            if (answer)
            {
                await _viewModel.Albums.DeleteCommand.ExecuteAsync(null);
            }
        }
        finally
        {
            _confirming = false;
        }
    }

    /// <summary>Asks before taking a collection away.</summary>
    /// <remarks>
    /// The same shape as removing an album or a person, and for the same reason:
    /// no file is touched by any of the three, and all three undo work that
    /// would have to be done again. Less is lost here than with an album - the
    /// albums themselves come back on to the wall - so the question says that
    /// first, and the declining button is still the default one.
    /// </remarks>
    private async void OnRemoveCollectionClicked(object sender, RoutedEventArgs e)
    {
        if (_confirming || _viewModel.Albums.Collections.Open is not CollectionItem collection)
        {
            return;
        }

        _confirming = true;
        try
        {
            string counted = collection.Summary.AlbumCount switch
            {
                0 => "Nothing is on it yet.",
                1 => "The album on it stays in your library, and is on no collection "
                     + "afterwards.",
                int albums => $"The {albums:N0} albums on it stay in your library, and are on "
                              + "no collection afterwards.",
            };

            bool answer = AppDialog.Confirm(
                this,
                $"Remove \"{collection.Name}\"?",
                $"{counted}\n\nOnly the collection's name goes, and it cannot be brought back.",
                confirm: "Remove collection",
                tone: DialogTone.Caution);

            if (answer)
            {
                await _viewModel.Albums.Collections.DeleteCommand.ExecuteAsync(null);
            }
        }
        finally
        {
            _confirming = false;
        }
    }

    /// <summary>Chooses, previews and confirms the physical move for one album.</summary>
    private async void OnMoveAlbumClicked(object sender, RoutedEventArgs e)
    {
        if (_confirming
            || !_viewModel.IsIdle
            || _viewModel.Albums.Selected is not AlbumItem album)
        {
            return;
        }

        _confirming = true;
        try
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Choose where to move this album's originals",
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            AlbumMovePlan plan = await _viewModel
                .PlanAlbumMoveAsync(album.Id, dialog.FolderName);

            if (plan.Moving == 0)
            {
                AppDialog.Tell(
                    this,
                    "Everything is already there",
                    $"All {plan.AlreadyThere:N0} originals in \"{album.Name}\" are already in:\n\n"
                    + plan.DestinationFolder,
                    DialogTone.Information);
                return;
            }

            string counted = plan.Moving == 1
                ? "Move 1 original"
                : $"Move {plan.Moving:N0} originals";
            string conflicts = plan.Renamed == 0
                ? "No destination names conflict."
                : plan.Renamed == 1
                    ? "1 file will receive a numbered name because that name already exists."
                    : $"{plan.Renamed:N0} files will receive numbered names because those names already exist.";
            string already = plan.AlreadyThere == 0
                ? string.Empty
                : $"\n\n{plan.AlreadyThere:N0} originals already in that folder will stay there.";

            bool answer = AppDialog.Confirm(
                this,
                $"Move originals from \"{album.Name}\"?",
                $"{counted} ({FileSize.Rounded(plan.TotalBytes)}) into:\n\n"
                + $"{plan.DestinationFolder}\n\n{conflicts}{already}\n\n"
                + "Existing files are never overwritten. Each moved photo keeps its album, "
                + "faces, metadata, and other library information. This cannot be undone as "
                + "one action.",
                confirm: "Move originals",
                tone: DialogTone.Caution);

            if (!answer)
            {
                return;
            }

            AlbumMoveResult result = await _viewModel.MoveAlbumAsync(plan);
            if (result.Failed > 0 || result.WasCancelled)
            {
                string firstErrors = result.Errors.Count == 0
                    ? string.Empty
                    : "\n\n" + string.Join("\n", result.Errors.Take(5));
                AppDialog.Tell(
                    this,
                    result.WasCancelled ? "The move was stopped" : "Some originals stayed put",
                    result.Summary + firstErrors,
                    DialogTone.Caution);
            }
        }
        catch (OperationCanceledException)
        {
            // The overlay already said it was stopping and no unstarted file was changed.
        }
        catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException
                                       or InvalidOperationException
                                       or ArgumentException
                                       or NotSupportedException)
        {
            AppDialog.Tell(
                this,
                "The originals were not moved",
                ex.Message,
                DialogTone.Caution);
        }
        finally
        {
            _confirming = false;
        }
    }
}
