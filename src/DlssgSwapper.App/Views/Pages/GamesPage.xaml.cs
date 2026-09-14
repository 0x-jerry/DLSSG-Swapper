using DlssgSwapper.App.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace DlssgSwapper.App.Views.Pages;

public sealed partial class GamesPage : Page
{
    public GamesPage() => InitializeComponent();

    private void OnRowDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.SelectedProfile != null)
            vm.SelectedProfile.ConfigureCommand.Execute(null);
    }
}
