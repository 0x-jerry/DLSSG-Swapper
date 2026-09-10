using Wpf.Ui.Abstractions;

namespace DlssgSwapper.App.Navigation;

public sealed class AppPageProvider(Func<Type, object?> factory) : INavigationViewPageProvider
{
    public object? GetPage(Type pageType) => factory(pageType);
}
