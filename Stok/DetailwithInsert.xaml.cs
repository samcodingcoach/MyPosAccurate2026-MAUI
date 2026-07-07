using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.Http.Headers;

namespace MyPosAccurate2026.Stok;

public partial class DetailwithInsert : ContentPage
{
    public string TransNumber { get; set; } = ""; 
    private List<StokOpnameDetailItem> _allItems = new List<StokOpnameDetailItem>();

    public DetailwithInsert()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadData();
    }

    private async Task LoadData()
    {
        var delayTask = Task.Delay(3000);
        
        try
        {
            string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
            if (string.IsNullOrEmpty(cleanToken))
            {
                await DisplayAlertAsync("Sesi Habis", "Anda harus login kembali.", "OK");
                return;
            }

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);

            // Step 1: Hit stokopname-result/list.php?search={TransNumber}
            string searchUrl = $"{App.API_HOST}stokopname-result/list.php?search={Uri.EscapeDataString(TransNumber)}";
            var searchResponse = await client.GetAsync(searchUrl);
            
            if (!searchResponse.IsSuccessStatusCode)
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await DisplayAlert("HTTP Error", $"Gagal mengambil daftar hasil (Status {searchResponse.StatusCode}).", "OK");
                });
                return;
            }

            var searchContent = await searchResponse.Content.ReadAsStringAsync();
            var searchResult = JsonConvert.DeserializeObject<StokOpnameResultListResponse>(searchContent);

            if (searchResult?.status != "success" || searchResult.data == null || searchResult.data.Count == 0)
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await DisplayAlert("Tidak Ditemukan", "Belum ada Hasil Stok Opname untuk perintah ini.", "OK");
                });
                return;
            }

            // Ambil number dari hasil pertama (OPR.xxxxx)
            string resultNumber = searchResult.data[0].number;

            // Step 2: Hit stokopname-result/detail.php?number={resultNumber}
            string apiUrl = $"{App.API_HOST}stokopname-result/detail.php?number={Uri.EscapeDataString(resultNumber)}";
            var response = await client.GetAsync(apiUrl);
            
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                try
                {
                    var result = JsonConvert.DeserializeObject<StokOpnameDetailResponse>(responseContent);
                    
                    if (result?.status == "success" && result.data != null)
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            var data = result.data;
                            LblNumber.Text = data.number ?? "-";
                            LblOrderNumber.Text = data.order?.number ?? "-";
                            LblTransDate.Text = data.transDate ?? "-";
                            LblDescription.Text = data.description ?? "-";
                            LblTotalItems.Text = $"{(data.detailItem?.Count ?? 0)} ITEMS";

                            _allItems = data.detailItem ?? new List<StokOpnameDetailItem>();
                            CV_Items.ItemsSource = _allItems;
                        });
                    }
                    else
                    {
                        MainThread.BeginInvokeOnMainThread(async () =>
                        {
                            await DisplayAlert("Respons Gagal", "Pesan: " + (result?.message ?? "Null") + "\n\nRaw: " + responseContent, "OK");
                        });
                    }
                }
                catch (Exception ex)
                {
                    MainThread.BeginInvokeOnMainThread(async () =>
                    {
                        await DisplayAlert("Error Parsing", "Gagal membaca JSON:\n" + ex.Message + "\n\nRaw: " + responseContent, "OK");
                    });
                }
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await DisplayAlert("HTTP Error", $"URL: {apiUrl}\nStatus Code: {response.StatusCode}\nError: {errorContent}", "OK");
                });
            }
        }
        catch (Exception ex)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await DisplayAlert("Exception", ex.Message, "OK");
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

    private async void BtnBack_Tapped(object sender, TappedEventArgs e)
    {
        await Navigation.PopAsync();
    }

    private void BtnLihatSemua_Clicked(object sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is StokOpnameDetailItem item)
        {
            item.ShowAllSerialNumbers = true;
        }
    }

    private async void BtnSearch_Tapped(object sender, TappedEventArgs e)
    {
        if (_allItems == null || _allItems.Count == 0) return;

        string searchResult = await DisplayPromptAsync("Pencarian", "Masukkan ID Produk atau Nama", "Cari", "Tampilkan Semua");
        
        if (searchResult == null) 
        {
            // Null means user pressed Tampilkan Semua / Batal
            CV_Items.ItemsSource = _allItems;
            LblTotalItems.Text = $"{_allItems.Count} ITEMS";
            return;
        }

        if (string.IsNullOrWhiteSpace(searchResult))
        {
            CV_Items.ItemsSource = _allItems;
            LblTotalItems.Text = $"{_allItems.Count} ITEMS";
        }
        else
        {
            var filtered = _allItems.Where(x => 
                (x.ItemName != null && x.ItemName.Contains(searchResult, StringComparison.OrdinalIgnoreCase)) ||
                (x.ItemNo != null && x.ItemNo.Contains(searchResult, StringComparison.OrdinalIgnoreCase))
            ).ToList();

            CV_Items.ItemsSource = filtered;
            LblTotalItems.Text = $"{filtered.Count} ITEMS";
        }
    }
}

// Models
public class StokOpnameResultListResponse
{
    public string status { get; set; }
    public string message { get; set; }
    public List<StokOpnameResultListItem> data { get; set; }
}

public class StokOpnameResultListItem
{
    public string number { get; set; }
    public string transDate { get; set; }
    public string description { get; set; }
    public int id { get; set; }
}

public class StokOpnameDetailResponse
{
    public string status { get; set; }
    public string message { get; set; }
    public StokOpnameDetail data { get; set; }
}

public class StokOpnameDetail
{
    public string number { get; set; }
    public int id { get; set; }
    public StokOpnameOrder order { get; set; }
    public List<StokOpnameDetailItem> detailItem { get; set; }
    public string description { get; set; }
    public string transDate { get; set; }
}

public class StokOpnameOrder
{
    public string number { get; set; }
    public int id { get; set; }
    public string startDate { get; set; }
    public string status { get; set; }
}

public class StokOpnameItem
{
    public string name { get; set; }
    public string no { get; set; }
    public StokOpnameUnit unit1 { get; set; }
}

public class StokOpnameUnit
{
    public string name { get; set; }
}

public class StokOpnameDetailSerialNumber
{
    public double quantity { get; set; }
    public StokOpnameSerialNumber serialNumber { get; set; }
}

public class StokOpnameSerialNumber
{
    public string number { get; set; }
    public string updateStockDate { get; set; }
}

public class StokOpnameDetailItem : INotifyPropertyChanged
{
    public StokOpnameItem item { get; set; }
    public double quantity { get; set; }
    public List<StokOpnameDetailSerialNumber> detailSerialNumber { get; set; }

    public string ItemName => item?.name ?? "-";
    public string ItemNo => $"ID: {item?.no ?? "-"}";
    public string UnitName => item?.unit1?.name ?? "-";
    public Color QuantityColor => quantity == 0 ? Colors.Red : Colors.DarkCyan;
    public bool HasSerialNumbers => detailSerialNumber != null && detailSerialNumber.Count > 0;
    public bool HasNoSerialNumbers => !HasSerialNumbers;

    public List<StokOpnameDetailSerialNumber> DisplaySerialNumbers
    {
        get
        {
            if (detailSerialNumber == null) return new List<StokOpnameDetailSerialNumber>();
            return ShowAllSerialNumbers ? detailSerialNumber : detailSerialNumber.Take(20).ToList();
        }
    }

    public bool ShowMoreButton => detailSerialNumber != null && detailSerialNumber.Count > 20 && !ShowAllSerialNumbers;

    private bool _showAllSerialNumbers;
    public bool ShowAllSerialNumbers
    {
        get => _showAllSerialNumbers;
        set
        {
            if (_showAllSerialNumbers != value)
            {
                _showAllSerialNumbers = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowAllSerialNumbers)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplaySerialNumbers)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowMoreButton)));
            }
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;
}