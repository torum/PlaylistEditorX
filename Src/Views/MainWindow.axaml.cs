using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PlaylistEditorX.Models;
using PlaylistEditorX.ViewModels;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace PlaylistEditorX.Views;

public partial class MainWindow : Window
{
    private readonly DataFormat<PlaylistItem> _nodeFormat = DataFormat.CreateInProcessFormat<PlaylistItem>("PlaylistItemFormat");
    private Point _pointerPressedPoint;
    private bool _isPressed;
    private bool _isPressedAndMoved;

    private bool _isDropAbove;

    private InsertionLineAdorner? _insertionLine;
    private AdornerLayer? _adornerLayer;

    public MainWindow()
    {
        InitializeComponent();

        this.PlaylistListBox.AddHandler(InputElement.PointerPressedEvent, OnItemPointerPressed, RoutingStrategies.Tunnel);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        _adornerLayer = AdornerLayer.GetAdornerLayer(PlaylistListBox);
    }

    private async void OnItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        if (e.Source is not Control control || control.DataContext is not PlaylistItem item)
        {
            Debug.WriteLine($"Not DataContext is PlaylistItem: {e.Source}");
            return;
        }

        //Debug.WriteLine($"PointerPressed on {item.Text}");
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        _pointerPressedPoint = e.GetPosition(this);
        _isPressed = true;
        _isPressedAndMoved = false;

        while (_isPressed)
        {
            await Task.Delay(100);
            if (_isPressedAndMoved)
                break;
        }

        if (!_isPressedAndMoved)
        {
            Debug.WriteLine("Pointer did not move");
            return;
        }

        var transferItem = DataTransferItem.Create(_nodeFormat, item);

        // 2. Prepare the v12 DataTransfer container payload
        var dragData = new DataTransfer();

        dragData.Add(transferItem);

        // Initiate the asynchronous native drag frame
        // This will block synchronously until the user releases the cursor
        var result = await DragDrop.DoDragDropAsync(e, dragData, DragDropEffects.Move);

        // Logic to remove node from its old parent if necessary

        if (result == DragDropEffects.Move)
        {
            e.Handled = true;
        }

        Debug.WriteLine(result == DragDropEffects.Move
            ? "DoDragDropAsync result = Succuses (Move)"
            : "DoDragDropAsync result = Fail (None)");

        RemoveInsertionLine();
    }

    private async void ListBox_DragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Formats.Contains(DataFormat.File))
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
            return;
        }

        var draggedNode = e.DataTransfer.TryGetValue(_nodeFormat);

        if (draggedNode is PlaylistItem item)
        {
            var pos = e.GetPosition(PlaylistListBox);
            AutoScroll(PlaylistListBox, pos);

            var currentPosition = e.GetPosition(PlaylistListBox);
            var visualTarget = PlaylistListBox.GetVisualAt(currentPosition);
            var targetListBoxItem = visualTarget?.FindAncestorOfType<ListBoxItem>();

            if (targetListBoxItem != null)
            {
                e.DragEffects = DragDropEffects.Move;
                e.Handled = true;

                // Determine whether to draw the line above or below the item
                var positionInItem = e.GetPosition(targetListBoxItem);
                double itemHeight = targetListBoxItem.Bounds.Height;
                double itemCenterY = itemHeight / 2;
                double insertionY;
                if (positionInItem.Y < itemCenterY)
                {
                    // Cursor is on the top half -> Intent is ABOVE
                    // Map local 0 back to the ListBox coordinate space
                    insertionY = targetListBoxItem.Bounds.Y;
                    _isDropAbove = true;
                }
                else
                {
                    // Cursor is on the bottom half -> Intent is BELOW
                    // Map local bottom back to the ListBox coordinate space
                    insertionY = targetListBoxItem.Bounds.Y + itemHeight;
                    _isDropAbove = false;
                }
                /*
                
                // Get the bounding box of the item being hovered over
                var itemBounds = targetListBoxItem.Bounds;
                double itemCenterY = itemBounds.Y + (itemBounds.Height / 2);

                double insertionY;
                if (currentPosition.Y < itemCenterY)
                {
                    // Cursor is on the top half -> Draw line at the top edge of this item
                    insertionY = itemBounds.Y;
                }
                else
                {
                    // Cursor is on the bottom half -> Draw line at the bottom edge of this item
                    insertionY = itemBounds.Y + itemBounds.Height;
                }
                */

                // Trigger your adorner update method
                ShowInsertionLine(insertionY);

                return;
            }
        }

        // Fallback: If they hover over empty space or an invalid format, reject the drop
        e.DragEffects = DragDropEffects.None;

        RemoveInsertionLine();
    }

    private async void ListBox_Drop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        if (e.DataTransfer.Formats.Contains(_nodeFormat))
        {
            var draggedNode = e.DataTransfer.TryGetValue(_nodeFormat);

            if (draggedNode is not null)
            {
                if ((e.Source as Control)?.DataContext is not PlaylistItem targetNode)
                {
                    e.DragEffects = DragDropEffects.None;
                    e.Handled = true;
                    return;
                }

                if (!ReferenceEquals(draggedNode, targetNode))
                {
                    // Success.

                    //Debug.WriteLine($"DraggedNode = {draggedNode.Text}, TargetNode = {targetNode?.Text}");

                    vm.ReorderItems(draggedNode, targetNode, _isDropAbove);

                    // Report success.
                    e.DragEffects = DragDropEffects.Move;
                    e.Handled = true;

                    RemoveInsertionLine();
                }
                else
                {
                    e.DragEffects = DragDropEffects.None;
                    e.Handled = true;

                    RemoveInsertionLine();
                }
            }

            e.Handled = true;
            return;
        }


        if (e.DataTransfer.Formats.Contains(DataFormat.File))
        {
            var files = e.DataTransfer.TryGetFiles();

            if (files != null)
            {
                // Convert IStorageItem to local paths (for Desktop use cases)
                var localPaths = files
                    .Select(file => file.TryGetLocalPath())
                    .Where(path => path != null)
                    .ToArray();

                if (localPaths.Length > 0)
                {
                    vm.AddFilesToPlaylist(localPaths!);
                }
            }

            e.Handled = true;
            return;
        }


    }

    private void ShowInsertionLine(double y)
    {
        if (_adornerLayer == null)
            return;

        if (_insertionLine == null)
        {
            _insertionLine = new InsertionLineAdorner();
            AdornerLayer.SetAdornedElement(_insertionLine, this.PlaylistListBox);
            _adornerLayer.Children.Add(_insertionLine);
        }

        _insertionLine.Y = y;
        _insertionLine.InvalidateVisual();
    }

    private void RemoveInsertionLine()
    {
        if (_adornerLayer == null)
            return;

        if (_insertionLine != null)
        {
            _adornerLayer.Children.Remove(_insertionLine);
            _insertionLine = null;
        }
    }

    private async void ListBox_FileDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        if (e.DataTransfer.Formats.Contains(DataFormat.File))
        {
            var files = e.DataTransfer.TryGetFiles();

            if (files != null)
            {
                // 3. Convert IStorageItem to local paths (for Desktop use cases)
                var localPaths = files
                    .Select(file => file.TryGetLocalPath())
                    .Where(path => path != null)
                    .ToArray();

                if (localPaths.Length > 0)
                {
                    vm.AddFilesToPlaylist(localPaths!);
                }
            }

            e.Handled = true;
        }
    }

    private static void AutoScroll(ListBox listBox, Avalonia.Point pointerPos)
    {
        const double scrollMargin = 30;

        if (listBox.Scroll != null)
        {
            if (pointerPos.Y < scrollMargin)
                listBox.Scroll.Offset = new Avalonia.Vector(listBox.Scroll.Offset.X, listBox.Scroll.Offset.Y - 10);

            else if (pointerPos.Y > listBox.Bounds.Height - scrollMargin)
                listBox.Scroll.Offset = new Avalonia.Vector(listBox.Scroll.Offset.X, listBox.Scroll.Offset.Y + 10);
        }
    }

    private void PlaylistListBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        if (vm.SelectedItem == null)
            return;

        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        if (!ctrl)
            return;

        if (e.Key == Key.Up)
        {
            vm.MoveItemUp(vm.SelectedItem);
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            vm.MoveItemDown(vm.SelectedItem);
            e.Handled = true;
        }
    }

    private void PlaylistListBox_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isPressed)
        {
            return;
        }

        var currentPoint = e.GetPosition(this);

        // Calculate the distance moved
        var deltaX = Math.Abs(currentPoint.X - _pointerPressedPoint.X);
        var deltaY = Math.Abs(currentPoint.Y - _pointerPressedPoint.Y);

        if (deltaX > 4 || deltaY > 4)
        {
            // The pointer HAS moved beyond a tiny jitter threshold
            Debug.WriteLine("Pointer is moved");
            _isPressedAndMoved = true;
            _isPressed = false;
        }
    }

    private void PlaylistListBox_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _isPressedAndMoved = false;
        _isPressed = false;
    }

    private void PlaylistListBox_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isPressedAndMoved = false;
        _isPressed = false;
    }
}