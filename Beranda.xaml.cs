namespace MyPosAccurate2026;

public partial class Beranda : ContentPage
{
	public Beranda()
	{
		InitializeComponent();
	}

    private async void MenuFaktur_Tapped(object sender, TappedEventArgs e)
    {
        if (sender is Border border)
        {
            await border.ScaleTo(0.9, 100);
            await border.ScaleTo(1.0, 100);
        }

        OverlayLoading.IsVisible = true;
        await Task.Delay(3000);
        OverlayLoading.IsVisible = false;

        await Navigation.PushAsync(new Sales.List_Faktur());
    }
}
