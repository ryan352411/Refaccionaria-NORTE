using System.Windows;
using RefaccionariaPOS.Security;
using RefaccionariaPOS.Views;

namespace RefaccionariaPOS
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnMainWindowClose;

            if (!ActivationService.Validate(out string activationMessage))
            {
                MessageBox.Show(activationMessage, "Licencia", MessageBoxButton.OK, MessageBoxImage.Warning);
                Shutdown();
                return;
            }

            LoginView pantallaLogin = new LoginView();
            MainWindow = pantallaLogin;
            pantallaLogin.Show();
        }
    }
}
