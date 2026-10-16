using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace MonoGame_project.Engine.Input;

/// <summary>
/// Captures one input snapshot per frame and owns relative-mouse capture.
/// Gameplay systems remain responsible for interpreting the input.
/// </summary>
public sealed class InputService : IDisposable
{
    private readonly Microsoft.Xna.Framework.Game _game;
    private MouseMode _mouseMode;
    private bool _mouseCaptureActive;
    private bool _mouseVisibilityBeforeCapture;
    private MouseState _previousHardwareMouse;
    private bool _hasHardwareMouseSnapshot;
    private bool _isFocused;
    private bool _isInputReady;
    private bool _disposed;

    internal InputService(Microsoft.Xna.Framework.Game game)
    {
        ArgumentNullException.ThrowIfNull(game);
        _game = game;
        _game.Activated += OnGameActivated;
        _game.Deactivated += OnGameDeactivated;
    }

    public KeyboardState CurrentKeyboard { get; private set; }
    public GamePadState CurrentGamePad { get; private set; }
    public MouseState PreviousMouse { get; private set; }
    public MouseState CurrentMouse { get; private set; }
    public Point MouseDelta { get; private set; }
    internal bool IsRelativeMouseCaptured =>
        _mouseMode == MouseMode.Relative && _mouseCaptureActive;

    /// <summary>Captures input once before scene update systems run.</summary>
    internal void BeginFrame()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var currentHardwareKeyboard = Keyboard.GetState();
        var currentHardwareGamePad = GamePad.GetState(PlayerIndex.One);
        var currentHardwareMouse = Mouse.GetState(_game.Window);
        var previousHardwareMouse = _previousHardwareMouse;
        var hadHardwareMouseSnapshot = _hasHardwareMouseSnapshot;
        _previousHardwareMouse = currentHardwareMouse;
        _hasHardwareMouseSnapshot = true;

        if (_isFocused && !_game.IsActive)
        {
            DeactivateInput();
        }

        if (!_isFocused)
        {
            ClearPublishedInput();
            return;
        }

        if (!_isInputReady)
        {
            ClearPublishedInput();
            _isInputReady = true;

            if (_mouseMode == MouseMode.Relative &&
                currentHardwareMouse.LeftButton == ButtonState.Pressed &&
                IsInsideWindow(currentHardwareMouse))
            {
                ActivateMouseCapture();
            }

            return;
        }

        PreviousMouse = CurrentMouse;
        CurrentKeyboard = currentHardwareKeyboard;
        CurrentGamePad = currentHardwareGamePad;
        CurrentMouse = currentHardwareMouse;
        MouseDelta = Point.Zero;

        if (_mouseMode == MouseMode.Free)
        {
            DeactivateMouseCapture();
            return;
        }

        if (!_mouseCaptureActive)
        {
            if (hadHardwareMouseSnapshot &&
                WasLeftMousePressedInsideWindow(
                    previousHardwareMouse,
                    currentHardwareMouse))
            {
                ActivateMouseCapture();
            }

            return;
        }

        MouseDelta = SdlRelativeMouse.ReadDelta();
    }

    internal void SetMouseMode(MouseMode mouseMode)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_mouseMode == mouseMode)
        {
            return;
        }

        _mouseMode = mouseMode;
        MouseDelta = Point.Zero;
        if (mouseMode == MouseMode.Free)
        {
            DeactivateMouseCapture();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _game.Activated -= OnGameActivated;
        _game.Deactivated -= OnGameDeactivated;
        _mouseMode = MouseMode.Free;
        DeactivateInput();
        _disposed = true;
    }

    private void OnGameActivated(object? sender, EventArgs eventArgs)
    {
        _isFocused = true;
        _isInputReady = false;
        _hasHardwareMouseSnapshot = false;
        ClearPublishedInput();
    }

    private void OnGameDeactivated(object? sender, EventArgs eventArgs)
    {
        DeactivateInput();
    }

    private void DeactivateInput()
    {
        _isFocused = false;
        _isInputReady = false;
        _hasHardwareMouseSnapshot = false;
        ClearPublishedInput();
        DeactivateMouseCapture();
    }

    private void ClearPublishedInput()
    {
        CurrentKeyboard = default;
        CurrentGamePad = default;
        PreviousMouse = default;
        CurrentMouse = default;
        MouseDelta = Point.Zero;
    }

    private void ActivateMouseCapture()
    {
        _mouseVisibilityBeforeCapture = _game.IsMouseVisible;
        _game.IsMouseVisible = false;
        try
        {
            SdlRelativeMouse.Enable();
        }
        catch
        {
            _game.IsMouseVisible = _mouseVisibilityBeforeCapture;
            throw;
        }

        _mouseCaptureActive = true;
    }

    private void DeactivateMouseCapture()
    {
        if (!_mouseCaptureActive)
        {
            return;
        }

        try
        {
            SdlRelativeMouse.Disable();
        }
        finally
        {
            _mouseCaptureActive = false;
            _game.IsMouseVisible = _mouseVisibilityBeforeCapture;
            MouseDelta = Point.Zero;
        }
    }

    private bool WasLeftMousePressedInsideWindow(
        MouseState previousMouse,
        MouseState currentMouse)
    {
        if (previousMouse.LeftButton != ButtonState.Released ||
            currentMouse.LeftButton != ButtonState.Pressed)
        {
            return false;
        }

        return IsInsideWindow(currentMouse);
    }

    private bool IsInsideWindow(MouseState mouse)
    {
        var bounds = _game.Window.ClientBounds;
        return mouse.X >= 0 &&
               mouse.X < bounds.Width &&
               mouse.Y >= 0 &&
               mouse.Y < bounds.Height;
    }
}
