using System.Windows.Controls;
using System.Windows.Input;
using DlssgSwapper.App.ViewModels;

namespace DlssgSwapper.App.Views.Pages;

public partial class GamesPage : Page
{
    public GamesPage() => InitializeComponent();

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.SelectedProfile != null)
            vm.ConfigureGameCommand.Execute(vm.SelectedProfile);
    }
}
