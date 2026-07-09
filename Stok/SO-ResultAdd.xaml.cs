using Newtonsoft.Json;
using System.Net.Http.Headers;

namespace MyPosAccurate2026.Stok;

public partial class SO_ResultAdd : ContentPage
{
    private List<SOItem> _availableOrders = new List<SOItem>();
	public SO_ResultAdd()
	{
		InitializeComponent();
	}

    private async void HiddenDatePicker_DateSelected(object sender, DateChangedEventArgs e)
    {
        string selectedDate = $"{e.NewDate:dd/MM/yyyy}";
        FormtransDate.Text = selectedDate;
        await LoadOrders(selectedDate);
    }

    private async Task LoadOrders(string date)
    {
        PickerorderNumber.ItemsSource = null;
        _availableOrders.Clear();

        try
        {
            string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);

            string apiUrl = $"{App.API_HOST}stokopname-order/list.php?transDate={Uri.EscapeDataString(date)}";
            var response = await client.GetAsync(apiUrl);
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<SOResponse>(content);
                
                if (result?.status == "success" && result.data != null)
                {
                    // Filter: statusName == "Menunggu Eksekusi" atau "Dalam Penghitungan"
                    _availableOrders = result.data.Where(x => 
                        x.statusName == "Menunggu Eksekusi" || 
                        x.statusName == "Dalam Penghitungan").ToList();
                    
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        PickerorderNumber.ItemsSource = _availableOrders.Select(x => x.number).ToList();
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error LoadOrders: {ex.Message}");
        }
    }

    private async void BtnLoad_Clicked(object sender, EventArgs e)
    {
        if(BtnLoad.Text == "LOAD BARANG")
        {
            if (PickerorderNumber.SelectedIndex < 0)
            {
                await DisplayAlertAsync("Validasi", "Pilih Perintah Opname terlebih dahulu", "OK");
                return;
            }

            string orderNumber = (string)PickerorderNumber.SelectedItem;

            MainThread.BeginInvokeOnMainThread(() => OverlayLoading.IsVisible = true);
            var delayTask = Task.Delay(3000);

            try
            {
                string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);

                string apiUrl = $"{App.API_HOST}stokopname-order/detail.php?number={Uri.EscapeDataString(orderNumber)}";
                var response = await client.GetAsync(apiUrl);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<SODetailResponse>(content);

                    if (result?.status == "success" && result.data?.detailItem != null)
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            CV_Barang.ItemsSource = result.data.detailItem;
                        });
                        FormCari.IsVisible = false;
                        BtnLoad.Text = "Pencarian Lain";


                    }
                    else
                    {
                        MainThread.BeginInvokeOnMainThread(async () =>
                        {
                            await DisplayAlertAsync("Gagal", result?.message ?? "Gagal memuat data barang", "OK");
                        });
                    }
                }
                else
                {
                    string err = await response.Content.ReadAsStringAsync();
                    MainThread.BeginInvokeOnMainThread(async () =>
                    {
                        await DisplayAlertAsync($"Error {(int)response.StatusCode}", err, "OK");
                    });
                }
            }
            catch (Exception ex)
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await DisplayAlertAsync("Error", $"Terjadi kesalahan: {ex.Message}", "OK");
                });
            }
            finally
            {
                await delayTask;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    OverlayLoading.IsVisible = false;
                });
            }
        }

        else
        {

        }
    }
}

// ===== DTOs =====
public class SODetailResponse
{
    public string status { get; set; }
    public string message { get; set; }
    public SODetailData data { get; set; }
}

public class SODetailData
{
    public List<SODetailItem> detailItem { get; set; }
}

public class SODetailItem
{
    public ItemInfo item { get; set; }
    public double quantity { get; set; }
    public ItemUnit itemUnit { get; set; }
    public double? quantityResult { get; set; }

    public string DisplayImage
    {
        get
        {
            string imgName = $"{item?.no}.jpg";
            string baseHost = App.API_HOST.Replace("api/", "");
            return $"{baseHost}images/{imgName}";
        }
    }
}

public class ItemInfo
{
    public string no { get; set; }
    public string name { get; set; }
}

public class ItemUnit
{
    public string name { get; set; }
}