using System.Windows;
using JunkCleaner.Ui;

namespace JunkCleaner;

public partial class ReportWindow : Window
{
    public ReportWindow()
    {
        InitializeComponent();
        MysticTitleBar.ApplyWhenReady(this);
    }

    public void SetBody(string content) => ReportBody.Text = content;

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
