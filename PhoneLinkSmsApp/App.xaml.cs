using System.Windows;
using System.Windows.Threading;

namespace PhoneLinkSmsApp;

public partial class App : Application
{
    void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show($"예상치 못한 오류가 발생했습니다.\n\n{e.Exception.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
