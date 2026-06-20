using System;
using System.Windows;
using RefaccionariaPOS.Data;
using RefaccionariaPOS.Security;
using RefaccionariaPOS.Views;

namespace RefaccionariaPOS
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            if (!ActivationService.Validate(out string activationMessage))
            {
                MessageBox.Show(activationMessage, "Licencia", MessageBoxButton.OK, MessageBoxImage.Warning);
                Shutdown();
                return;
            }

            try
            {
                DatabaseMigrator.EjecutarUnaVez();
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo preparar la base de datos: " + ex.Message, "Error de base de datos", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }

            LoginView pantallaLogin = new LoginView();
            MainWindow = pantallaLogin;
            pantallaLogin.Show();
        }
    }
}
