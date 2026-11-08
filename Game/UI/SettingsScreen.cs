using System.Globalization;
using System.IO;
using Gum.Wireframe;
using Microsoft.Xna.Framework.Input;
using MonoGame_project.Configuration;
using MonoGame_project.Engine;
using MonoGame_project.Engine.Input;
using MonoGame_project.Engine.UI;

namespace MonoGame_project.Game.UI;

/// <summary>Full-screen settings modal with a saved copy and an editable draft.</summary>
internal sealed class SettingsScreen : UiScreen
{
    private readonly EngineContext _context;
    private readonly GraphicalUiElement _visual;
    private readonly GraphicalUiElement _graphicsGroup;
    private readonly GraphicalUiElement _controlsGroup;
    private readonly GraphicalUiElement _audioGroup;
    private readonly GraphicalUiElement _status;
    private readonly GumHudButton _graphicsTab;
    private readonly GumHudButton _controlsTab;
    private readonly GumHudButton _audioTab;
    private readonly GumHudButton _fullscreen;
    private readonly GumHudButton _vsync;
    private readonly GumHudButton _uiScale;
    private readonly GumHudButton _sensitivity;
    private readonly GumHudButton _reset;
    private readonly GumHudButton _cancel;
    private readonly GumHudButton _apply;
    private UserSettings _draft;
    private bool _openRequested;
    private bool _closeRequested;
    private bool _disposed;

    public SettingsScreen(EngineContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _draft = context.Settings.Current;
        _visual = GumHudProject.CreateScreen("Settings");
        _graphicsGroup = GumHudProject.Require(_visual, "GraphicsGroup");
        _controlsGroup = GumHudProject.Require(_visual, "ControlsGroup");
        _audioGroup = GumHudProject.Require(_visual, "AudioGroup");
        _status = GumHudProject.Require(_visual, "Status");
        _graphicsTab = new GumHudButton(_visual, "GraphicsTab", "GRAPHICS",
            () => SelectTab(SettingsTab.Graphics));
        _controlsTab = new GumHudButton(_visual, "ControlsTab", "CONTROLS",
            () => SelectTab(SettingsTab.Controls));
        _audioTab = new GumHudButton(_visual, "AudioTab", "AUDIO",
            () => SelectTab(SettingsTab.Audio));
        _fullscreen = new GumHudButton(_visual, "FullscreenButton", "", () =>
        {
            _draft = _draft with { Fullscreen = !_draft.Fullscreen };
            UpdateValues();
        });
        _vsync = new GumHudButton(_visual, "VSyncButton", "", () =>
        {
            _draft = _draft with { VSync = !_draft.VSync };
            UpdateValues();
        });
        _uiScale = new GumHudButton(_visual, "UiScaleButton", "", () =>
        {
            _draft = _draft with
            {
                UiScale = Next(UserSettings.UiScaleOptions, _draft.UiScale)
            };
            UpdateValues();
        });
        _sensitivity = new GumHudButton(_visual, "SensitivityButton", "", () =>
        {
            _draft = _draft with
            {
                InspectMouseSensitivity = Next(UserSettings.SensitivityOptions,
                    _draft.InspectMouseSensitivity)
            };
            UpdateValues();
        });
        _reset = new GumHudButton(_visual, "ResetButton", "RESET DEFAULTS", Reset);
        _cancel = new GumHudButton(_visual, "CancelButton", "BACK", Close);
        _apply = new GumHudButton(_visual, "ApplyButton", "APPLY", Apply);
        SelectTab(SettingsTab.Graphics);
        UpdateValues();
    }

    public override GraphicalUiElement Visual => _visual;
    public override bool SuspendsScene => true;

    /// <summary>Called by the pause menu; attachment happens before the next Gum update.</summary>
    public void Open()
    {
        if (_context.InputContexts.Current == InputContexts.PauseMenu)
        {
            _openRequested = true;
        }
    }

    public override void HandleInput()
    {
        if (_openRequested)
        {
            _openRequested = false;
            _closeRequested = false;
            _draft = _context.Settings.Current;
            GumHudProject.SetText(_status, "");
            SelectTab(SettingsTab.Graphics);
            UpdateValues();
            _context.InputContexts.Push(InputContexts.Settings);
            _context.Ui.Scene.SetModalVisible(this, true);
            return;
        }

        if (_context.InputContexts.Current != InputContexts.Settings)
        {
            return;
        }

        if (_closeRequested || _context.Input.WasKeyPressed(Keys.Escape))
        {
            _closeRequested = false;
            _context.Ui.Scene.SetModalVisible(this, false);
            _context.InputContexts.Pop(InputContexts.Settings);
        }
    }

    public override void Dispose()
    {
        if (_disposed) return;
        _graphicsTab.Dispose();
        _controlsTab.Dispose();
        _audioTab.Dispose();
        _fullscreen.Dispose();
        _vsync.Dispose();
        _uiScale.Dispose();
        _sensitivity.Dispose();
        _reset.Dispose();
        _cancel.Dispose();
        _apply.Dispose();
        _disposed = true;
    }

    private void SelectTab(SettingsTab tab)
    {
        _graphicsGroup.Visible = tab == SettingsTab.Graphics;
        _controlsGroup.Visible = tab == SettingsTab.Controls;
        _audioGroup.Visible = tab == SettingsTab.Audio;
        _graphicsTab.SetSelected(tab == SettingsTab.Graphics);
        _controlsTab.SetSelected(tab == SettingsTab.Controls);
        _audioTab.SetSelected(tab == SettingsTab.Audio);
    }

    private void UpdateValues()
    {
        _fullscreen.SetLabel(_draft.Fullscreen ? "FULLSCREEN" : "WINDOWED");
        _vsync.SetLabel(_draft.VSync ? "ON" : "OFF");
        _uiScale.SetLabel($"{_draft.UiScale * 100f:0}%");
        _sensitivity.SetLabel(_draft.InspectMouseSensitivity.ToString("0.00",
            CultureInfo.InvariantCulture) + "x");
    }

    private void Reset()
    {
        _draft = UserSettings.Default;
        GumHudProject.SetText(_status, "DEFAULTS SELECTED / APPLY TO SAVE");
        UpdateValues();
    }

    private void Close() => _closeRequested = true;

    private void Apply()
    {
        try
        {
            var uiScaleChanged = _draft.UiScale != _context.Settings.Current.UiScale;
            _context.Settings.Save(_draft);
            GumHudProject.SetText(_status, uiScaleChanged
                ? "SAVED / RESTART TO APPLY UI SCALE"
                : "SETTINGS SAVED");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"[settings] Save failed: {error.Message}");
            GumHudProject.SetText(_status, "COULD NOT SAVE SETTINGS / CHECK STORAGE");
        }
    }

    private static float Next(float[] options, float current)
    {
        var index = Array.IndexOf(options, current);
        return options[(index + 1) % options.Length];
    }

    private enum SettingsTab { Graphics, Controls, Audio }
}
