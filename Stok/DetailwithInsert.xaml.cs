using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.Http.Headers;

namespace MyPosAccurate2026.Stok;

public partial class DetailwithInsert : ContentPage
{
    public int DetailId { get; set; } = 50; 

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

            // Per user correction: stokopname-result
            string apiUrl = $"{App.API_HOST}stokopname-result/detail.php?id={DetailId}";
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

                            CV_Items.ItemsSource = data.detailItem;
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
}

// Models
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