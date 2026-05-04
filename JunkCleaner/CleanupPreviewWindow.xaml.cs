using System.Windows;
using JunkCleaner.Ui;

namespace JunkCleaner;

public partial class CleanupPreviewWindow : Window
{
    public CleanupPreviewWindow()
    {
        InitializeComponent();
        MysticTitleBar.ApplyWhenReady(this);
    }

    public void SetBody(string content) => PreviewBody.Text = content;

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
