using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Floe.App.Services;
using Floe.App.ViewModels;
using Floe.Core.Interop;
using Floe.Core.Models;
using Floe.Core.Services;

namespace Floe.App.Windows;

public partial class DockWindow
{
    private void UpdateClock()
    {
        var now = DateTime.Now;
        var time = now.ToString("HH:mm");
        if (_clockTime.Text != time)
        {
            // at startup the clock has no text yet; that first fill is not a minute turning
            var shown = _clockTime.Text.Length > 0;
            _clockTime.Text = time;
            if (shown && GlassSettings.Cyber && !_dozeHidden && _sleepRun is null && Root.OpacityMask is null) PlayClockSplit();
        }

        var date = now.ToString("ddd d MMM", Loc.Culture);
        if (_clockDate.Text != date)
        {
            // the date only changes once a day
            _clockDate.Text = date;
        }
    }

}
