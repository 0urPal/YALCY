using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace YALCY.Views.Tabs;

public partial class DmxSacnTabView : UserControl
{
    // The value each number box had when editing started
    private readonly Dictionary<NumericUpDown, decimal?> _valuesBeforeEdit = new();

    public DmxSacnTabView()
    {
        InitializeComponent();
        AddHandler(GotFocusEvent, OnNumberBoxGotFocus, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(LostFocusEvent, OnNumberBoxLostFocus, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private void OnNumberBoxGotFocus(object? sender, GotFocusEventArgs e)
    {
        var box = FindNumberBox(e.Source);
        if (box != null)
            _valuesBeforeEdit.TryAdd(box, box.Value);
    }

    // NumericBoxConverter skips writing an empty box back to its setting. If a box is left empty,
    // put back the value it had before editing, which also undoes anything saved while typing.
    private void OnNumberBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        var box = FindNumberBox(e.Source);
        if (box == null || !_valuesBeforeEdit.Remove(box, out var previous) || previous == null) return;

        // Run after NumericUpDown has finished committing its text, which is what empties Value
        Dispatcher.UIThread.Post(() =>
        {
            if (box.Value == null)
                box.SetCurrentValue(NumericUpDown.ValueProperty, previous);
        });
    }

    private static NumericUpDown? FindNumberBox(object? source) =>
        source as NumericUpDown ?? (source as Visual)?.FindAncestorOfType<NumericUpDown>();
}
