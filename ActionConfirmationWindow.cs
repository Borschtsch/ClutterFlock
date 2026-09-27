using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;

namespace ClutterFlock;

/// <summary>Explicit consent for one displayed filesystem action; Cancel is the default.</summary>
public sealed class ActionConfirmationWindow : Window
{
    public ActionConfirmationWindow(string title, string description, string confirmLabel, Style? dangerStyle = null)
    {
        Title = title; Width = 600; Height = 460; MinWidth = 480; MinHeight = 330;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        var panel = new DockPanel { Margin = new Thickness(18) };
        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = new Button { Name = "CancelAction", Content = "Cancel", IsCancel = true, IsDefault = true, Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 0, 10, 0) };
        var confirm = new Button { Name = "ConfirmAction", Content = confirmLabel, Padding = new Thickness(16, 8, 16, 8) };
        if (dangerStyle != null) confirm.Style = dangerStyle;
        confirm.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(cancel); buttons.Children.Add(confirm); DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons);
        panel.Children.Add(new TextBox { Text = description, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = panel;
    }
}
