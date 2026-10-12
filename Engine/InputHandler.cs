using System;
using System.Collections.Generic;
using Avalonia.Input;

namespace AVAWars.Engine;

public class InputHandler : IDisposable
{
    private readonly HashSet<Key> _pressedKeys = new();
    private readonly ControllerManager _controller = new();

    // Threshold for analog stick to register as direction
    private const float StickThreshold = 0.3f;
    // Threshold for trigger to register as pressed
    private const float TriggerThreshold = 0.3f;

    // Mouse state
    private bool _leftMousePressed;
    private bool _rightMousePressed;
    private bool _middleMousePressed;
    private double _mouseWheelDelta;

    public void OnKeyDown(Key key)
    {
        _pressedKeys.Add(key);
    }

    public void OnKeyUp(Key key)
    {
        _pressedKeys.Remove(key);
    }

    public bool IsKeyPressed(Key key)
    {
        return _pressedKeys.Contains(key);
    }

    public void OnMouseDown(MouseButton button)
    {
        if (button == MouseButton.Left)
            _leftMousePressed = true;
        else if (button == MouseButton.Right)
            _rightMousePressed = true;
        else if (button == MouseButton.Middle)
            _middleMousePressed = true;
    }

    public void OnMouseUp(MouseButton button)
    {
        if (button == MouseButton.Left)
            _leftMousePressed = false;
        else if (button == MouseButton.Right)
            _rightMousePressed = false;
        else if (button == MouseButton.Middle)
            _middleMousePressed = false;
    }

    public void OnMouseWheel(double delta)
    {
        _mouseWheelDelta += delta;
    }

    public void Update()
    {
        _controller.Update();
    }

    public void PostUpdate()
    {
        // Reset mouse wheel delta after each frame
        _mouseWheelDelta = 0;
    }

    public bool IsLeftMousePressed => _leftMousePressed;
    public bool IsRightMousePressed => _rightMousePressed;
    public double MouseWheelDelta => _mouseWheelDelta;

    public bool IsControllerConnected => _controller.IsConnected;

    // Controller-only menu inputs, for initials entry (the keyboard is handled key by key there).
    public bool PadUp => _controller.DPadUp || _controller.LeftStickY < -0.5f;
    public bool PadDown => _controller.DPadDown || _controller.LeftStickY > 0.5f;
    public bool PadLeft => _controller.DPadLeft || _controller.LeftStickX < -0.5f;
    public bool PadRight => _controller.DPadRight || _controller.LeftStickX > 0.5f;
    public bool PadAccept => _controller.ButtonX || _controller.ButtonB || _controller.ButtonA;
    public bool PadDone => _controller.ButtonStart;

    /// <summary>Any key, mouse button, wheel turn or controller input at all this frame.</summary>
    public bool IsAnyInput =>
        _pressedKeys.Count > 0 ||
        _leftMousePressed || _rightMousePressed || _middleMousePressed || _mouseWheelDelta != 0 ||
        Math.Abs(StickX) > 0.01 || Math.Abs(StickY) > 0.01 || IsFiring ||
        IsPausePressed || IsBackPressed;

    /// <summary>
    /// The flight stick from the keyboard or a gamepad, -1 to 1 on each axis, up positive. The mouse
    /// doesn't go through here: it places the crosshair directly.
    /// </summary>
    public double StickX
    {
        get
        {
            double x = 0;
            if (IsKeyPressed(Key.A) || IsKeyPressed(Key.Left) || _controller.DPadLeft) x -= 1;
            if (IsKeyPressed(Key.D) || IsKeyPressed(Key.Right) || _controller.DPadRight) x += 1;
            if (Math.Abs(_controller.LeftStickX) > StickThreshold * 0.5) x += _controller.LeftStickX;
            return Math.Clamp(x, -1, 1);
        }
    }

    public double StickY
    {
        get
        {
            double y = 0;
            if (IsKeyPressed(Key.S) || IsKeyPressed(Key.Down) || _controller.DPadDown) y -= 1;
            if (IsKeyPressed(Key.W) || IsKeyPressed(Key.Up) || _controller.DPadUp) y += 1;
            if (Math.Abs(_controller.LeftStickY) > StickThreshold * 0.5) y -= _controller.LeftStickY; // stick up is negative
            return Math.Clamp(y, -1, 1);
        }
    }

    /// <summary>Any of the yoke's fire buttons: the laser cannons, and the torpedoes at the exhaust port.</summary>
    public bool IsFiring =>
        IsKeyPressed(Key.Space) ||
        IsKeyPressed(Key.LeftCtrl) || IsKeyPressed(Key.RightCtrl) ||
        _controller.ButtonA || _controller.ButtonB || _controller.ButtonX ||
        _controller.RightBumper || _controller.LeftBumper ||
        _controller.RightTrigger > TriggerThreshold || _controller.LeftTrigger > TriggerThreshold ||
        _leftMousePressed || _rightMousePressed;

    // The mouse places the crosshair wherever it points.
    private bool _pointerMoved;
    public double PointerX { get; private set; }
    public double PointerY { get; private set; }

    public void OnPointer(double x, double y)
    {
        PointerX = x;
        PointerY = y;
        _pointerMoved = true;
    }

    /// <summary>True once after each mouse movement.</summary>
    public bool TakePointerMove()
    {
        bool moved = _pointerMoved;
        _pointerMoved = false;
        return moved;
    }

    public bool IsPausePressed =>
        _controller.ButtonStart;

    public bool IsBackPressed =>
        _controller.ButtonBack;

    public void Dispose()
    {
        _controller.Dispose();
    }
}
