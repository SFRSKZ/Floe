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
using MacDock.App.Services;
using MacDock.App.ViewModels;
using MacDock.Core.Interop;
using MacDock.Core.Models;
using MacDock.Core.Services;

namespace MacDock.App.Windows;

public partial class DockWindow
{
    private void UpdateClock()
    {
        var now = DateTime.Now;
        var time = now.ToString("HH:mm");
        if (_clockTime.Text != time)
        {
            _clockTime.Text = time;
        }

        var date = now.ToString("ddd d MMM");
        if (_clockDate.Text != date)
        {
            // the date only changes once a day
            _clockDate.Text = date;
        }
    }

}
