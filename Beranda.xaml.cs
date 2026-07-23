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
            SkeletonFaktur.IsVisible = true;
            SkeletonStok.IsVisible = true;
            SkeletonTerlaris.IsVisible = true;
            SkeletonLabaBersih.IsVisible = true;
            SkeletonHPP.IsVisible = true;
            _ = AnimateSkeleton(SkeletonPenerimaan);
            _ = AnimateSkeleton(SkeletonTransaksi);
            _ = AnimateSkeleton(SkeletonFaktur);
            _ = AnimateSkeleton(SkeletonStok);
            _ = AnimateSkeleton(SkeletonTerlaris);
            _ = AnimateSkeleton(SkeletonLabaBersih);
            _ = AnimateSkeleton(SkeletonHPP);

            // Simulasi delay 3 detik agar animasi skeleton terlihat jelas
            await Task.Delay(3000);

            string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
            string apiUrlPenerimaan = $"{App.API_HOST}dashboard/total-penerimaan.php";
            string apiUrlTransaksi = $"{App.API_HOST}dashboard/jumlah-penerimaan.php";
            string apiUrlFaktur = $"{App.API_HOST}dashboard/faktur-terakhir.php";
            string apiUrlStok = $"{App.API_HOST}dashboard/stok-menipis.php";
            string apiUrlTerlaris = $"{App.API_HOST}dashboard/barang-terlaris.php";
            string apiUrlRugiLaba = $"{App.API_HOST}dashboard/rugi-laba.php";

            using (var client = new HttpClient())
            {
                if (!string.IsNullOrEmpty(cleanToken))
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);

                var taskPenerimaan = client.GetAsync(apiUrlPenerimaan);
                var taskTransaksi = client.GetAsync(apiUrlTransaksi);
                var taskFaktur = client.GetAsync(apiUrlFaktur);
                var taskStok = client.GetAsync(apiUrlStok);
                var taskTerlaris = client.GetAsync(apiUrlTerlaris);
                var taskRugiLaba = client.GetAsync(apiUrlRugiLaba);

                await Task.WhenAll(taskPenerimaan, taskTransaksi, taskFaktur, taskStok, taskTerlaris, taskRugiLaba);

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

                if (taskFaktur.Result.IsSuccessStatusCode)
                {
                    string content = await taskFaktur.Result.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<FakturTerakhirResponse>(content);
                    if (result?.status == "success" && result.data != null && result.data.Count > 0)
                    {
                        BindableLayout.SetItemsSource(ListFakturTerbaru, result.data);
                        ListFakturTerbaru.IsVisible = true;
                        EmptyFakturState.IsVisible = false;
                    }
                    else
                    {
                        ListFakturTerbaru.IsVisible = false;
                        EmptyFakturState.IsVisible = true;
                    }
                }
                else
                {
                    ListFakturTerbaru.IsVisible = false;
                    EmptyFakturState.IsVisible = true;
                }
                if (taskStok.Result.IsSuccessStatusCode)
                {
                    string content = await taskStok.Result.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<StokMenipisResponse>(content);
                    if (result?.status == "success" && result.data != null)
                    {
                        BindableLayout.SetItemsSource(ListStokMenipis, result.data);
                    }
                }
                
                if (taskTerlaris.Result.IsSuccessStatusCode)
                {
                    string content = await taskTerlaris.Result.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<ProdukTerlarisResponse>(content);
                    if (result?.status == "success" && result.data != null)
                    {
                        for (int i = 0; i < result.data.Count; i++)
                        {
                            result.data[i].Rank = i + 1;
                        }
                        ListProdukTerlaris.ItemsSource = result.data;
                    }
                }

                if (taskRugiLaba.Result.IsSuccessStatusCode)
                {
                    string content = await taskRugiLaba.Result.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<RugiLabaResponse>(content);
                    if (result?.status == "success" && result.summary != null)
                    {
                        LabelLabaBersih.Text = $"Rp {result.summary.labaBersih:N0}";
                        LabelHPP.Text = $"Rp {result.summary.hpp:N0}";
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading dashboard: {ex.Message}");
            LabelPenjualan.Text = "Rp 0";
            LabelTransaksi.Text = "0";
            LabelLabaBersih.Text = "Rp 0";
            LabelHPP.Text = "Rp 0";
        }
        finally
        {
            SkeletonPenerimaan.IsVisible = false;
            SkeletonTransaksi.IsVisible = false;
            SkeletonFaktur.IsVisible = false;
            SkeletonStok.IsVisible = false;
            SkeletonTerlaris.IsVisible = false;
            SkeletonLabaBersih.IsVisible = false;
            SkeletonHPP.IsVisible = false;
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

public class FakturTerakhirResponse
{
    public string status { get; set; }
    public List<FakturTerakhirData> data { get; set; }
}

public class FakturTerakhirData
{
    public string invoiceTime { get; set; }
    public string number { get; set; }
    public string statusName { get; set; }
    public double totalAmount { get; set; }
    public string branchName { get; set; }
    public string receiptHistoryNumber { get; set; }
    
    public string FormattedTotalAmount => $"Rp {totalAmount:N0}";
}

public class StokMenipisResponse
{
    public string status { get; set; }
    public string message { get; set; }
    public List<StokMenipisData> data { get; set; }
}

public class StokMenipisData
{
    public string itemNo { get; set; }
    public string name { get; set; }
    public double quantity { get; set; }

    public string DisplayImage
    {
        get
        {
            if (string.IsNullOrWhiteSpace(itemNo)) return "nophotoproduct150.jpg";
            string baseHost = App.API_HOST.Replace("api/", "");
            return $"{baseHost}images/{itemNo}.jpg";
        }
    }
}

public class ProdukTerlarisResponse
{
    public string status { get; set; }
    public string message { get; set; }
    public List<ProdukTerlarisData> data { get; set; }
}

public class ProdukTerlarisData
{
    public string itemNo { get; set; }
    public string name { get; set; }
    public int terjual { get; set; }

    public int Rank { get; set; }
    public string RankText => $"#{Rank}";

    public string RankColor
    {
        get
        {
            if (Rank == 1) return "#006767";
            if (Rank == 2) return "#5d5f5f";
            if (Rank == 3) return "#677877";
            return "#006767";
        }
    }

    public string DisplayImage
    {
        get
        {
            if (string.IsNullOrWhiteSpace(itemNo)) return "nophotoproduct150.jpg";
            string baseHost = App.API_HOST.Replace("api/", "");
            return $"{baseHost}images/{itemNo}.jpg";
        }
    }
}

public class RugiLabaResponse
{
    public string status { get; set; }
    public string message { get; set; }
    public RugiLabaSummary summary { get; set; }
}

public class RugiLabaSummary
{
    public double hpp { get; set; }
    public double labaBersih { get; set; }
}
