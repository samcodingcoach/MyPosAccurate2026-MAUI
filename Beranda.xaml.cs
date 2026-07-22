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
            SkeletonPenerimaan.IsVisible = true;
            SkeletonTransaksi.IsVisible = true;
            _ = AnimateSkeleton(SkeletonPenerimaan);
            _ = AnimateSkeleton(SkeletonTransaksi);

            // Simulasi delay 3 detik agar animasi skeleton terlihat jelas
            await Task.Delay(3000);

            string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
            string apiUrlPenerimaan = $"{App.API_HOST}dashboard/total-penerimaan.php";
            string apiUrlTransaksi = $"{App.API_HOST}dashboard/jumlah-penerimaan.php";

            using (var client = new HttpClient())
            {
                if (!string.IsNullOrEmpty(cleanToken))
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);

                var taskPenerimaan = client.GetAsync(apiUrlPenerimaan);
                var taskTransaksi = client.GetAsync(apiUrlTransaksi);

                await Task.WhenAll(taskPenerimaan, taskTransaksi);

                if (taskPenerimaan.Result.IsSuccessStatusCode)
                {
                    string content = await taskPenerimaan.Result.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<TotalPenerimaanResponse>(content);
                    if (result?.status == "success" && result.data != null)
                    {
                        LabelPenjualan.Text = $"Rp {result.data.totalPenerimaan:N0}";
                    }
                }

                if (taskTransaksi.Result.IsSuccessStatusCode)
                {
                    string content = await taskTransaksi.Result.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<JumlahPenerimaanResponse>(content);
                    if (result?.status == "success" && result.data != null)
                    {
                        LabelTransaksi.Text = $"{result.data.jumlahPenerimaan:N0}";
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading dashboard: {ex.Message}");
            LabelPenjualan.Text = "Rp 0";
            LabelTransaksi.Text = "0";
        }
        finally
        {
            SkeletonPenerimaan.IsVisible = false;
            SkeletonTransaksi.IsVisible = false;
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

public class JumlahPenerimaanResponse
{
    public string status { get; set; }
    public string message { get; set; }
    public JumlahPenerimaanData data { get; set; }
}

public class JumlahPenerimaanData
{
    public int jumlahPenerimaan { get; set; }
    public string date { get; set; }
}
