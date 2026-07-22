using Newtonsoft.Json;
using System.Net.Http.Headers;

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

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = LoadTotalPenerimaan();
    }

    private async Task LoadTotalPenerimaan()
    {
        try
        {
            SkeletonPenjualan.IsVisible = true;
            LabelPenjualan.IsVisible = false;
            _ = AnimateSkeleton(SkeletonPenjualan);

            // Simulasi delay 3 detik agar animasi skeleton terlihat jelas
            await Task.Delay(3000);

            string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
            string apiUrl = $"{App.API_HOST}dashboard/total-penerimaan.php";

            using (var client = new HttpClient())
            {
                if (!string.IsNullOrEmpty(cleanToken))
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);

                var response = await client.GetAsync(apiUrl);
                if (response.IsSuccessStatusCode)
                {
                    string content = await response.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<TotalPenerimaanResponse>(content);
                    if (result?.status == "success" && result.data != null)
                    {
                        LabelPenjualan.Text = $"Rp {result.data.totalPenerimaan:N0}";
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading total penerimaan: {ex.Message}");
            LabelPenjualan.Text = "Rp 0";
        }
        finally
        {
            SkeletonPenjualan.IsVisible = false;
            LabelPenjualan.IsVisible = true;
        }
    }

    private async Task AnimateSkeleton(View view)
    {
        while (view.IsVisible)
        {
            await view.FadeTo(0.4, 500);
            await view.FadeTo(1.0, 500);
        }
    }
}

public class TotalPenerimaanResponse
{
    public string status { get; set; }
    public string message { get; set; }
    public TotalPenerimaanData data { get; set; }
}

public class TotalPenerimaanData
{
    public double totalPenerimaan { get; set; }
    public string date { get; set; }
}
