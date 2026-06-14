using System.Windows;
using RefaccionariaPOS.Views;

namespace RefaccionariaPOS
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            LoginView pantallaLogin = new LoginView();
            MainWindow = pantallaLogin;
            pantallaLogin.Show();
        }
    }
}
