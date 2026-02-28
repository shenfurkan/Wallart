using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using WallArt.ViewModels;
using System.Drawing;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace WallArt;

public partial class MainWindow : Window
{
    public bool IsExplicitClose { get; set; } = false;

    private NotifyIcon? _notifyIcon;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // Initialize Native Tray Icon
        _notifyIcon = new NotifyIcon
        {
            Icon = new Icon(Application.GetResourceStream(new Uri("pack://application:,,,/Wallart.ico"))!.Stream),
            Text = "WallArt",
            Visible = true
        };
        
        _notifyIcon.DoubleClick += (s, e) => RestoreWindow();
        
        var contextMenu = new ContextMenuStrip();
        var exitItem = new ToolStripMenuItem("Exit WallArt");
        exitItem.Click += (s, e) => 
        {
            IsExplicitClose = true;
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            Application.Current.Shutdown();
        };
        contextMenu.Items.Add(exitItem);
        _notifyIcon.ContextMenuStrip = contextMenu;
    }

    private void RestoreWindow()
    {
        this.Show();
        this.WindowState = WindowState.Normal;
        this.Activate();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            this.DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        ShrinkToTray();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        ShrinkToTray();
    }
    
    private void ShrinkToTray()
    {
        this.Hide();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!IsExplicitClose)
        {
            e.Cancel = true;
            ShrinkToTray();
        }
        else
        {
            _notifyIcon!.Visible = false;
            _notifyIcon.Dispose();
        }
        base.OnClosing(e);
    }
}