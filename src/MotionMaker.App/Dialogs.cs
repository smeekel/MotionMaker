using Avalonia.Controls;
using Avalonia.Layout;

namespace MotionMaker.App;

internal enum SaveChoice
{
    Save,
    Discard,
    Cancel
}

internal static class Dialogs
{
    public static async Task<SaveChoice> AskSaveAsync(Window owner)
    {
        var choice = SaveChoice.Cancel;
        var dialog = Create("Save changes", 420, 170);
        var save = new Button { Content = "Save", IsDefault = true, MinWidth = 88 };
        var discard = new Button { Content = "Don't save", MinWidth = 88 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 88 };
        save.Click += (_, _) => { choice = SaveChoice.Save; dialog.Close(); };
        discard.Click += (_, _) => { choice = SaveChoice.Discard; dialog.Close(); };
        cancel.Click += (_, _) => { choice = SaveChoice.Cancel; dialog.Close(); };
        dialog.Content = Layout("Save changes to this project?", save, discard, cancel);
        await dialog.ShowDialog(owner);
        return choice;
    }

    public static async Task<string?> PromptAsync(Window owner, string title, string message, string initial)
    {
        string? value = null;
        var dialog = Create(title, 420, 190);
        var box = new TextBox { Text = initial, Margin = new Avalonia.Thickness(16, 0, 16, 8) };
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 88 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 88 };
        ok.Click += (_, _) => { value = box.Text ?? ""; dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();
        var panel = new DockPanel();
        var buttons = Buttons(ok, cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        DockPanel.SetDock(box, Dock.Bottom);
        panel.Children.Add(buttons);
        panel.Children.Add(box);
        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(16)
        });
        dialog.Content = panel;
        await dialog.ShowDialog(owner);
        return value;
    }

    public static async Task ShowMessageAsync(Window owner, string title, string message)
    {
        var dialog = Create(title, 460, 200);
        var close = new Button { Content = "Close", IsDefault = true, IsCancel = true, MinWidth = 88 };
        close.Click += (_, _) => dialog.Close();
        dialog.Content = Layout(message, close);
        await dialog.ShowDialog(owner);
    }

    public static async Task<bool> EditPreferencesAsync(Window owner, MotionMaker.Core.UserSettings settings)
    {
        var dialog = Create("Preferences", 420, 280);
        var invert = new CheckBox { Content = "Invert mouse wheel", IsChecked = settings.InvertWheel, Margin = new Avalonia.Thickness(16, 12, 16, 4) };
        var orbit = NumberBox("Orbit sensitivity", settings.OrbitSensitivity);
        var pan = NumberBox("Pan sensitivity", settings.PanSensitivity);
        var snap = NumberBox("Snap spacing (mm)", settings.SnapSpacingMm);
        var saved = false;
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 88 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 88 };
        ok.Click += (_, _) => { saved = true; dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();
        var stack = new StackPanel();
        stack.Children.Add(invert);
        stack.Children.Add(orbit.Label);
        stack.Children.Add(orbit.Box);
        stack.Children.Add(pan.Label);
        stack.Children.Add(pan.Box);
        stack.Children.Add(snap.Label);
        stack.Children.Add(snap.Box);
        var panel = new DockPanel();
        var buttons = Buttons(ok, cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);
        panel.Children.Add(stack);
        dialog.Content = panel;
        await dialog.ShowDialog(owner);
        if (!saved)
        {
            return false;
        }

        settings.InvertWheel = invert.IsChecked == true;
        settings.OrbitSensitivity = Read(orbit.Box, settings.OrbitSensitivity);
        settings.PanSensitivity = Read(pan.Box, settings.PanSensitivity);
        settings.SnapSpacingMm = Read(snap.Box, settings.SnapSpacingMm);
        if (settings.OrbitSensitivity <= 0)
        {
            settings.OrbitSensitivity = 1;
        }

        if (settings.PanSensitivity <= 0)
        {
            settings.PanSensitivity = 1;
        }

        if (settings.SnapSpacingMm <= 0)
        {
            settings.SnapSpacingMm = 1;
        }

        return true;
    }

    private static Window Create(string title, double width, double height) => new()
    {
        Title = title,
        Width = width,
        Height = height,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        CanResize = false
    };

    private static Control Layout(string message, params Button[] buttons)
    {
        var panel = new DockPanel();
        var row = Buttons(buttons);
        DockPanel.SetDock(row, Dock.Bottom);
        panel.Children.Add(row);
        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(16)
        });
        return panel;
    }

    private static StackPanel Buttons(params Button[] buttons)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Avalonia.Thickness(16)
        };
        foreach (var button in buttons)
        {
            row.Children.Add(button);
        }

        return row;
    }

    private static (TextBlock Label, TextBox Box) NumberBox(string label, double value)
    {
        return (
            new TextBlock { Text = label, Margin = new Avalonia.Thickness(16, 8, 16, 2) },
            new TextBox { Text = value.ToString("0.###"), Margin = new Avalonia.Thickness(16, 0, 16, 0) });
    }

    private static double Read(TextBox box, double fallback)
        => double.TryParse(box.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)
            || double.TryParse(box.Text, out value)
            ? value
            : fallback;
}
