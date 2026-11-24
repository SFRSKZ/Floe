using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using Floe.App.Services;

namespace Floe.App.Windows;

/// <summary>
/// The monitor going dark (and a DisplayPort screen dropping off the bus while it sleeps) leaves the dock laid out
/// for whatever Windows reported in between, with a stale frame and a hover that never ended. When the screen comes
/// back the dock lays itself out again, drops the hover and draws one full frame from scratch.
/// </summary>
public partial class DockWindow
{
    private const int WmPowerBroadcast = 0x0218;
    private const int PbtPowerSettingChange = 0x8013;
    private static Guid _consoleDisplayState = new("6FE69556-704A-47A0-8F24-C28D936FDA47");
    private IntPtr _displayStateNotify;
    private DispatcherTimer? _displayRecover;
    private int _displayState = 1;

    private void WatchDisplayPower()
    {
        try { _displayStateNotify = RegisterPowerSettingNotification(_hwnd, ref _consoleDisplayState, 0); }
        catch (Exception ex) { Log($"display notify failed {ex.Message}"); }
        Closed += (_, _) =>
        {
            if (_displayStateNotify != IntPtr.Zero) UnregisterPowerSettingNotification(_displayStateNotify);
            _displayStateNotify = IntPtr.Zero;
        };
    }

    private void HandleDisplayPower(int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == 0x007E) // WM_DISPLAYCHANGE: the size Windows gives right now may be a passing one
        {
            Log($"display change {lParam.ToInt64() & 0xFFFF}x{(lParam.ToInt64() >> 16) & 0xFFFF}");
            ScheduleDisplayRecover(TimeSpan.FromSeconds(1.5));
            return;
        }

        if (msg != WmPowerBroadcast || wParam.ToInt64() != PbtPowerSettingChange || lParam == IntPtr.Zero) return;
        // POWERBROADCAST_SETTING: GUID (16) + DataLength (4) + Data
        if (Marshal.PtrToStructure<Guid>(lParam) != _consoleDisplayState) return;
        var state = Marshal.ReadInt32(lParam, 20); // 0 off, 1 on, 2 dimmed
        if (state == _displayState) return;
        _displayState = state;
        Log($"display state={(state == 0 ? "off" : state == 1 ? "on" : "dimmed")}");
        // a dark screen stalls the vsync: those frames say nothing about the machine
        Performance.Hold(state == 0);
        if (state == 1) ScheduleDisplayRecover(TimeSpan.FromSeconds(1.5));
    }

    private void ScheduleDisplayRecover(TimeSpan delay)
    {
        _displayRecover ??= new DispatcherTimer(DispatcherPriority.Background, Dispatcher);
        _displayRecover.Stop();
        _displayRecover.Interval = delay;
        _displayRecover.Tick -= OnDisplayRecover;
        _displayRecover.Tick += OnDisplayRecover;
        _displayRecover.Start();
    }

    private void OnDisplayRecover(object? sender, EventArgs e)
    {
        _displayRecover?.Stop();
        if (_displayState == 0) return;
        Log("display recover");
        try
        {
            Performance.RestoreStartLevel();
            if (DwmDock) DwmTransparency.Apply(this);
            Reposition();
            LeaveHover();
            // a visualizer that lost its GPU while the screen was away gets another go
            ApplyVisualizerState();
            // one software frame makes WPF throw away what it kept and draw every pixel again
            if (_source?.CompositionTarget is { } target)
            {
                target.RenderMode = RenderMode.SoftwareOnly;
                Dispatcher.BeginInvoke(new Action(() => target.RenderMode = RenderMode.Default), DispatcherPriority.Background);
            }

            InvalidateVisual();
        }
        catch (Exception ex)
        {
            Log($"display recover failed {ex.Message}");
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr RegisterPowerSettingNotification(IntPtr recipient, ref Guid powerSetting, int flags);

    [DllImport("user32.dll")]
    private static extern bool UnregisterPowerSettingNotification(IntPtr handle);
}
