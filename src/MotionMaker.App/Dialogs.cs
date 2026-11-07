using Avalonia.Controls;
using Avalonia.Layout;
using MotionMaker.Core;

namespace MotionMaker.App;

internal sealed record GearRequest(GearKind Kind, double Module, int Teeth, double Pressure, double FaceWidth, double Bore, double Pitch);

internal enum SaveChoice
{
    Save,
    Discard,
    Cancel
}

internal static class Dialogs
{
    public static async Task<SaveChoice> AskSaveAsync(Window owner, string? message = null)
    {
        var choice = SaveChoice.Cancel;
        var dialog = Create("Save changes", 460, 180);
        var save = new Button { Content = "Save", IsDefault = true, MinWidth = 88 };
        var discard = new Button { Content = "Don't save", MinWidth = 88 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 88 };
        save.Click += (_, _) => { choice = SaveChoice.Save; dialog.Close(); };
        discard.Click += (_, _) => { choice = SaveChoice.Discard; dialog.Close(); };
        cancel.Click += (_, _) => { choice = SaveChoice.Cancel; dialog.Close(); };
        dialog.Content = Layout(message ?? "Save changes to this project?", save, discard, cancel);
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

    public static async Task<GearRequest?> AskGearAsync(Window owner)
    {
        GearRequest? request = null;
        var dialog = Create("Gear", 420, 460);
        var spur = new RadioButton { Content = "Spur", IsChecked = true, Margin = new Avalonia.Thickness(16, 12, 16, 0) };
        var bevel = new RadioButton { Content = "Bevel", Margin = new Avalonia.Thickness(16, 4, 16, 0) };
        var module = NumberBox("Module (mm)", 1);
        var teeth = NumberBox("Teeth", 20);
        var pressure = NumberBox("Pressure angle (°)", 20);
        var face = NumberBox("Face width (mm)", 10);
        var bore = NumberBox("Bore (mm)", 6);
        var pitch = NumberBox("Pitch angle (°)", 45);
        pitch.Label.IsVisible = false;
        pitch.Box.IsVisible = false;
        bevel.IsCheckedChanged += (_, _) =>
        {
            var show = bevel.IsChecked == true;
            pitch.Label.IsVisible = show;
            pitch.Box.IsVisible = show;
        };
        var create = new Button { Content = "Create", IsDefault = true, MinWidth = 88 };
        var defaults = new Button { Content = "Use defaults", MinWidth = 110 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 88 };
        GearRequest BuildRequest(bool useDefaults)
        {
            var kind = bevel.IsChecked == true ? GearKind.Bevel : GearKind.Spur;
            if (useDefaults)
            {
                return new GearRequest(kind, 1, 20, 20, 10, 6, 45);
            }

            return new GearRequest(
                kind,
                Read(module.Box, 1),
                Math.Max(6, (int)Math.Round(Read(teeth.Box, 20))),
                Read(pressure.Box, 20),
                Read(face.Box, 10),
                Read(bore.Box, 6),
                kind == GearKind.Bevel ? Read(pitch.Box, 45) : 45);
        }

        create.Click += (_, _) => { request = BuildRequest(false); dialog.Close(); };
        defaults.Click += (_, _) => { request = BuildRequest(true); dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();
        var stack = new StackPanel();
        stack.Children.Add(spur);
        stack.Children.Add(bevel);
        stack.Children.Add(module.Label);
        stack.Children.Add(module.Box);
        stack.Children.Add(teeth.Label);
        stack.Children.Add(teeth.Box);
        stack.Children.Add(pressure.Label);
        stack.Children.Add(pressure.Box);
        stack.Children.Add(face.Label);
        stack.Children.Add(face.Box);
        stack.Children.Add(bore.Label);
        stack.Children.Add(bore.Box);
        stack.Children.Add(pitch.Label);
        stack.Children.Add(pitch.Box);
        var panel = new DockPanel();
        var buttons = Buttons(create, defaults, cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);
        panel.Children.Add(stack);
        dialog.Content = panel;
        await dialog.ShowDialog(owner);
        return request;
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
