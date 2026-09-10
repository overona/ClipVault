using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ClipVault.Models;
using ClipVault.Native;
using ClipVault.Services;

namespace ClipVault.Views;

public partial class MainWindow : Window
{
    private const int HotkeyId = 0xC11B;

    private readonly HistoryStore _store;
    private readonly Settings _settings;
    private readonly ListCollectionView _view;
    private readonly DispatcherTimer _captureTimer;
    private readonly DispatcherTimer _clockTimer;

    private HwndSource? _source;
    private IntPtr _hwnd;
    private IntPtr _lastForeground;
    private string? _expectedHash;
    private bool _hotkeyRegistered;
    private bool _suppressDeactivateHide;
    private Vector _slideFrom;   // offset (DIPs) toward the taskbar edge the popup slides in from

    public MainWindow(HistoryStore store, Settings settings)
    {
        _store = store;
        _settings = settings;
        InitializeComponent();

        _view = new ListCollectionView(_store.Items) { Filter = FilterItem };
        ItemList.ItemsSource = _view;
        _store.Items.CollectionChanged += (_, _) => UpdateCount();

        // Coalesce bursts of WM_CLIPBOARDUPDATE (Office apps fire several per copy).
        _captureTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _captureTimer.Tick += (_, _) => { _captureTimer.Stop(); CaptureClipboard(); };

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _clockTimer.Tick += (_, _) => { foreach (var i in _store.Items) i.RefreshTimeAgo(); };
        _clockTimer.Start();

        UpdateCount();
        UpdatePreview(Selected);
    }

    /// <summary>Creates the native window (without showing it) so we can listen for clipboard changes and the hotkey.</summary>
    public void InitializeHidden()
    {
        _hwnd = new WindowInteropHelper(this).EnsureHandle();
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);
        if (!NativeMethods.AddClipboardFormatListener(_hwnd))
            App.Log("AddClipboardFormatListener failed");
        RegisterHotkey();
    }

    public void Shutdown()
    {
        UnregisterHotkey();
        if (_hwnd != IntPtr.Zero) NativeMethods.RemoveClipboardFormatListener(_hwnd);
        _source?.RemoveHook(WndProc);
    }

    // ---------------- Hotkey ----------------

    public bool RegisterHotkey()
    {
        UnregisterHotkey();
        _hotkeyRegistered = NativeMethods.RegisterHotKey(_hwnd, HotkeyId, _settings.NativeModifiers, _settings.NativeKey);
        if (!_hotkeyRegistered)
            App.Log($"RegisterHotKey failed for {_settings.HotkeyText} (already taken by another app?)");
        return _hotkeyRegistered;
    }

    private void UnregisterHotkey()
    {
        if (_hotkeyRegistered) { NativeMethods.UnregisterHotKey(_hwnd, HotkeyId); _hotkeyRegistered = false; }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case NativeMethods.WM_CLIPBOARDUPDATE:
                _captureTimer.Stop();
                _captureTimer.Start();
                handled = true;
                break;
            case NativeMethods.WM_HOTKEY when wParam.ToInt32() == HotkeyId:
                TogglePopup();
                handled = true;
                break;
        }
        return IntPtr.Zero;
    }

    // ---------------- Clipboard capture ----------------

    private void CaptureClipboard()
    {
        try
        {
            if (_expectedHash is not null)
            {
                // This update was caused by us re-copying an item; just make sure it's on top.
                var ours = _store.FindByHash(_expectedHash);
                _expectedHash = null;
                if (ours is not null) return;
            }

            var sourceApp = NativeMethods.GetForegroundProcessName();
            if (_settings.IsExcludedApp(sourceApp)) return;
            var item = ClipboardService.Capture(_settings, _store.ImagesDir);
            if (item is null) return;
            item.SourceApp = sourceApp;
            _store.AddOrTouch(item);

            if (IsVisible && string.IsNullOrEmpty(SearchBox.Text))
                SelectIndex(0);
        }
        catch (Exception ex)
        {
            App.Log("Capture failed: " + ex);
        }
    }

    // ---------------- Show / hide ----------------

    public void TogglePopup()
    {
        if (IsVisible && IsActive) Hide();
        else ShowPopup();
    }

    public void ShowPopup()
    {
        if (!IsVisible)
        {
            _lastForeground = NativeMethods.GetForegroundWindow();
            if (_lastForeground == _hwnd) _lastForeground = IntPtr.Zero;
            PositionOnCursorScreen();
        }

        SearchBox.Text = "";
        _view.Refresh();
        bool animate = !IsVisible;
        if (animate) Opacity = 0;
        Show();
        Activate();
        NativeMethods.SetForegroundWindow(_hwnd);
        SearchBox.Focus();
        SelectIndex(0);
        if (animate) SlideIn();
    }

    /// <summary>Short slide + fade from the taskbar edge, the way Windows flyouts appear.</summary>
    private void SlideIn()
    {
        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);
        BeginAnimation(OpacityProperty, null);

        double left = Left, top = Top;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var slide = TimeSpan.FromMilliseconds(220);

        if (_slideFrom.X != 0)
            BeginAnimation(LeftProperty, new DoubleAnimation(left + _slideFrom.X, left, slide) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        if (_slideFrom.Y != 0)
            BeginAnimation(TopProperty, new DoubleAnimation(top + _slideFrom.Y, top, slide) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });

        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)) { FillBehavior = FillBehavior.Stop };
        fade.Completed += (_, _) => Opacity = 1;
        Opacity = 1; // base value the Stop fill returns to
        BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>
    /// Docks the popup against the taskbar of the screen the cursor is on (centered along it, like the
    /// Windows 11 clipboard flyout) and records which direction to slide in from.
    /// </summary>
    private void PositionOnCursorScreen()
    {
        try
        {
            const double margin = 12, slideDistance = 56;
            var cursor = System.Windows.Forms.Cursor.Position;
            var screen = System.Windows.Forms.Screen.FromPoint(cursor);
            var m = _source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            var wa = ToDips(screen.WorkingArea, m);
            var bounds = ToDips(screen.Bounds, m);

            double w = Math.Min(Width, wa.Width - 2 * margin);
            double h = Math.Min(Height, wa.Height - 2 * margin);
            Width = w; Height = h;

            // The taskbar lives on whichever side the working area is inset from the screen bounds.
            if (wa.Bottom < bounds.Bottom - 0.5 || (wa == bounds)) // bottom, or auto-hidden/unknown: assume bottom
            {
                Left = wa.Left + (wa.Width - w) / 2; Top = wa.Bottom - h - margin; _slideFrom = new Vector(0, slideDistance);
            }
            else if (wa.Top > bounds.Top + 0.5)
            {
                Left = wa.Left + (wa.Width - w) / 2; Top = wa.Top + margin; _slideFrom = new Vector(0, -slideDistance);
            }
            else if (wa.Left > bounds.Left + 0.5)
            {
                Left = wa.Left + margin; Top = wa.Top + (wa.Height - h) / 2; _slideFrom = new Vector(-slideDistance, 0);
            }
            else
            {
                Left = wa.Right - w - margin; Top = wa.Top + (wa.Height - h) / 2; _slideFrom = new Vector(slideDistance, 0);
            }
        }
        catch (Exception ex) { App.Log("Positioning failed: " + ex.Message); }
    }

    private static Rect ToDips(System.Drawing.Rectangle r, Matrix fromDevice)
    {
        var tl = fromDevice.Transform(new Point(r.Left, r.Top));
        var br = fromDevice.Transform(new Point(r.Right, r.Bottom));
        return new Rect(tl, br);
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (_suppressDeactivateHide) return;
        Hide();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Hide();

    protected override void OnClosing(CancelEventArgs e)
    {
        // The window lives for the whole app lifetime; closing just hides it.
        e.Cancel = true;
        Hide();
    }

    // ---------------- Using items ----------------

    private ClipItem? Selected => ItemList.SelectedItem as ClipItem;

    private void UseItem(ClipItem? item, bool paste, bool plainText = false)
    {
        if (item is null) return;

        _expectedHash = item.Hash;
        if (!ClipboardService.Apply(item, plainText))
        {
            _expectedHash = null;
            System.Windows.MessageBox.Show(this, "That item could not be put on the clipboard. The original files may have been moved or deleted.",
                "ClipVault", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _store.Touch(item, countAsUse: true);

        var target = _lastForeground;
        Hide();

        if (paste && _settings.AutoPaste && target != IntPtr.Zero)
        {
            _ = Task.Run(async () =>
            {
                NativeMethods.SetForegroundWindow(target);
                await Task.Delay(150);
                NativeMethods.SendCtrlV();
            });
        }
    }

    private void Paste_Click(object sender, RoutedEventArgs e) => UseItem(Selected, paste: true);
    private void PastePlain_Click(object sender, RoutedEventArgs e) => UseItem(Selected, paste: true, plainText: true);
    private void Copy_Click(object sender, RoutedEventArgs e) => UseItem(Selected, paste: false);

    private void Pin_Click(object sender, RoutedEventArgs e) => TogglePin();

    private void TogglePin()
    {
        var item = Selected;
        if (item is null) return;
        _store.SetPinned(item, !item.Pinned);
        _view.Refresh();
        ItemList.SelectedItem = item;
        ItemList.ScrollIntoView(item);
        UpdatePreview(item);
    }

    private void Delete_Click(object sender, RoutedEventArgs e) => DeleteSelected();

    private void DeleteSelected()
    {
        var item = Selected;
        if (item is null) return;
        int index = _view.IndexOf(item);
        _store.Remove(item);
        SelectIndex(Math.Min(index, _view.Count - 1));
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _suppressDeactivateHide = true;
        try
        {
            bool hasPinned = _store.Items.Any(i => i.Pinned);
            var result = System.Windows.MessageBox.Show(this,
                hasPinned ? "Delete all unpinned items from the history?" : "Delete everything from the history?",
                "Clear history", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                _store.Clear(keepPinned: true);
                UpdatePreview(null);
            }
        }
        finally { _suppressDeactivateHide = false; Activate(); }
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    public void OpenSettings()
    {
        _suppressDeactivateHide = true;
        try
        {
            var dlg = new SettingsWindow(_settings) { Owner = IsVisible ? this : null };
            if (dlg.ShowDialog() == true)
            {
                _settings.Save();
                _store.MaxItems = _settings.MaxItems;
                _store.Trim();
                App.Current.UpdateTrayText();
                ThemeManager.Apply(_settings.Theme);
                if (!RegisterHotkey())
                    System.Windows.MessageBox.Show($"Could not register {_settings.HotkeyText}. Another program is probably using it. Pick a different combination in Settings.",
                        "ClipVault", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        finally
        {
            _suppressDeactivateHide = false;
            if (IsVisible) Activate();
        }
    }

    // ---------------- Selection / search ----------------

    private bool FilterItem(object o) => o is ClipItem item && item.Matches(SearchBox.Text);

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        _view.Refresh();
        SelectIndex(0);
        UpdateCount();
    }

    private void SelectIndex(int index)
    {
        if (_view.Count == 0) { ItemList.SelectedItem = null; UpdatePreview(null); return; }
        index = Math.Clamp(index, 0, _view.Count - 1);
        ItemList.SelectedIndex = index;
        if (ItemList.SelectedItem is not null) ItemList.ScrollIntoView(ItemList.SelectedItem);
        // SelectionChanged does not fire when the index was already selected, so sync the preview here too.
        UpdatePreview(Selected);
    }

    private void MoveSelection(int delta) => SelectIndex(ItemList.SelectedIndex < 0 ? 0 : ItemList.SelectedIndex + delta);

    private void ItemList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdatePreview(Selected);

    private void ItemList_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Rows are Focusable=False so typing always lands in the search box, but WPF only selects a
        // ListBoxItem on click when it can take focus. Select the clicked row here instead.
        if (e.ChangedButton is not (MouseButton.Left or MouseButton.Right)) return;
        if (e.OriginalSource is DependencyObject d && FindAncestor<ListBoxItem>(d) is { DataContext: ClipItem item })
        {
            ItemList.SelectedItem = item;
            UpdatePreview(item); // no SelectionChanged when the row was already selected
        }
    }

    private void ItemList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject d || FindAncestor<System.Windows.Controls.Primitives.ButtonBase>(d) is not null) return;
        if (FindAncestor<ListBoxItem>(d) is not null)
            UseItem(Selected, paste: true);
    }

    /// <summary>The small copy icon on each row: copy that row without pasting.</summary>
    private void RowCopy_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ClipItem item) UseItem(item, paste: false);
        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject d) where T : DependencyObject
    {
        while (d is not null && d is not T) d = VisualTreeHelper.GetParent(d);
        return d as T;
    }

    private void UpdatePreview(ClipItem? item)
    {
        PreviewContent.Content = item;
        if (item is null)
        {
            PreviewTitle.Text = "Nothing selected";
            PreviewMeta.Text = "";
            PinButton.SetResourceReference(ForegroundProperty, "Muted");
            PastePlainMenu.IsEnabled = false;
            return;
        }
        PreviewTitle.Text = item.KindLabel + (item.SourceApp is null ? "" : "  from " + item.SourceApp);
        PreviewMeta.Text = $"{item.Details}   ·   copied {item.CopiedAtLocal}   ·   {item.UsageText}";
        PinButton.SetResourceReference(ForegroundProperty, item.Pinned ? "Accent" : "Muted");
        PinMenu.Header = item.Pinned ? "Unpin" : "Pin";
        PastePlainMenu.IsEnabled = item.Kind == ClipKind.Text;
    }

    private void UpdateCount()
    {
        int total = _store.Items.Count;
        int shown = _view.Count;
        CountText.Text = shown == total ? $"{total} item{(total == 1 ? "" : "s")}" : $"{shown} of {total} items";
        EmptyText.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = total == 0 ? "Nothing here yet. Copy something and it will show up." : "No matches.";
        if (total > 0 && shown == 0) EmptyText.Visibility = Visibility.Visible;
    }

    // ---------------- Keyboard ----------------

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (ctrl && key >= Key.D1 && key <= Key.D9)
        {
            int n = key - Key.D1;
            if (n < _view.Count) UseItem(_view.GetItemAt(n) as ClipItem, paste: true);
            e.Handled = true;
            return;
        }

        switch (key)
        {
            case Key.Escape:
                if (!string.IsNullOrEmpty(SearchBox.Text)) SearchBox.Text = "";
                else Hide();
                e.Handled = true;
                break;
            case Key.Down: MoveSelection(1); e.Handled = true; break;
            case Key.Up: MoveSelection(-1); e.Handled = true; break;
            case Key.PageDown: MoveSelection(8); e.Handled = true; break;
            case Key.PageUp: MoveSelection(-8); e.Handled = true; break;
            case Key.Home when ctrl || !SearchBox.IsKeyboardFocused: SelectIndex(0); e.Handled = true; break;
            case Key.End when ctrl || !SearchBox.IsKeyboardFocused: SelectIndex(_view.Count - 1); e.Handled = true; break;
            case Key.Enter:
                // Enter pastes, Ctrl+Enter only copies; Shift on either strips RTF/HTML so only plain text is pasted.
                UseItem(Selected, paste: !ctrl, plainText: shift);
                e.Handled = true;
                break;
            case Key.Delete when !SearchBox.IsKeyboardFocused || string.IsNullOrEmpty(SearchBox.Text) || ctrl:
                DeleteSelected();
                e.Handled = true;
                break;
            case Key.P when ctrl:
                TogglePin();
                e.Handled = true;
                break;
            case Key.F when ctrl:
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;
                break;
        }
    }

    private void Window_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        // Typing anywhere in the window goes to the search box.
        if (SearchBox.IsKeyboardFocused || string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0])) return;
        SearchBox.Focus();
        SearchBox.Text += e.Text;
        SearchBox.CaretIndex = SearchBox.Text.Length;
        e.Handled = true;
    }
}
