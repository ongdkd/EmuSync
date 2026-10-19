using Microsoft.Extensions.DependencyInjection;

namespace EmuSync.App;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
		UserAppTheme = AppTheme.Dark;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var page = new LibraryPage();
		var window = new Window(page) { Title = "EmuSync", Width = 1000, Height = 740, MinimumWidth = 360, MinimumHeight = 560 };
		window.Deactivated += (_, _) => page.Deactivated();
		window.Activated += async (_, _) => await page.Returned();
		return window;
	}
}
