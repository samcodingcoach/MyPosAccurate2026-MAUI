using Newtonsoft.Json;
using System.Net.Http.Headers;
using System.Collections.ObjectModel;

namespace MyPosAccurate2026.Stok;

public partial class SO_ResultAdd : ContentPage
{
    private List<SOItem> _availableOrders = new List<SOItem>();
    private ObservableCollection<SODetailItem> _detailList = new ObservableCollection<SODetailItem>();
    private List<SODetailItem> _allLoadedDetails = new List<SODetailItem>();
    private string _currentOrderNumber = "";
    private string _currentStatusName = "";

	public SO_ResultAdd()
	{
		InitializeComponent();
        CV_Barang.ItemsSource = _detailList;
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

            _currentOrderNumber = (string)PickerorderNumber.SelectedItem;
            MainThread.BeginInvokeOnMainThread(() => OverlayLoading.IsVisible = true);
            var delayTask = Task.Delay(3000);

            try
            {
                string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);

                string apiUrl = $"{App.API_HOST}stokopname-order/detail.php?number={Uri.EscapeDataString(_currentOrderNumber)}";
                var response = await client.GetAsync(apiUrl);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<SODetailResponse>(content);

                    if (result?.status == "success" && result.data?.detailItem != null)
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            string status = result.data.statusName ?? "";
                            _currentStatusName = status;
                            StatusNameLabel.Text = $"Status: {status}";
                            
                            Color bgColor = Color.FromArgb("#fff4c7"); // Default: Dalam Penghitungan
                            if (status == "Menunggu Eksekusi") bgColor = Color.FromArgb("#C6E2E2");
                            else if (status == "Selesai") bgColor = Color.FromArgb("#DCFCE7");
                            
                            StatusNameLabel.BackgroundColor = bgColor;

                            _allLoadedDetails = result.data.detailItem.OrderByDescending(x => x.quantity).ToList();
                            _detailList.Clear();
                            foreach (var item in _allLoadedDetails)
                                _detailList.Add(item);
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
            //Kosongkan data collection view dan reset form
            FormCari.IsVisible = true;
            BtnLoad.Text = "LOAD BARANG";
            _detailList.Clear();
            _allLoadedDetails.Clear();
        }
    }

    private async void BtnSearch_Tapped(object sender, TappedEventArgs e)
    {
        if (_allLoadedDetails == null || _allLoadedDetails.Count == 0)
        {
            await DisplayAlertAsync("Peringatan", "Silakan load barang terlebih dahulu sebelum mencari.", "OK");
            return;
        }

        string result = await DisplayPromptAsync("Cari Barang", "Masukkan No Item:", "Cari", "Reset/Batal", keyboard: Keyboard.Numeric);
        
        if (result != null)
        {
            string searchNo = result.Trim();
            
            _detailList.Clear();
            if (string.IsNullOrEmpty(searchNo))
            {
                foreach (var item in _allLoadedDetails)
                {
                    _detailList.Add(item);
                }
            }
            else
            {
                var foundItems = _allLoadedDetails.Where(x => x.item != null && x.item.no != null && x.item.no.Equals(searchNo, StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (var item in foundItems)
                {
                    _detailList.Add(item);
                }

                if (foundItems.Count == 0)
                {
                    await DisplayAlertAsync("Info", "Barang tidak ditemukan pada list. Pastikan nomor item benar.", "OK");
                }
            }
        }
    }

    private async void CV_Barang_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is SODetailItem selectedItem)
        {
            // Reset selection
            ((CollectionView)sender).SelectedItem = null;
            
            // Cek jika quantity adalah 0
            if (selectedItem.quantity == 0)
            {
                bool answer = await DisplayAlertAsync("Konfirmasi", "Apakah anda ingin transaksi opname barang nol (kosong)?", "Ya", "Tidak");
                if (!answer) return;
            }
            
            // Pindah ke halaman detail dengan membawa seluruh objek detail, order number, dan statusName
            await Navigation.PushAsync(new SO_ResultAdd_Detail(selectedItem, _currentOrderNumber, _currentStatusName));
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
    public string statusName { get; set; }
    public List<SODetailItem> detailItem { get; set; }
}

public class SODetailItem
{
    public ItemInfo item { get; set; }
    public double quantity { get; set; }
    public ItemUnit itemUnit { get; set; }
    public double? quantityResult { get; set; }
    public List<SOSerialNumberDetail> detailSerialNumber { get; set; }

    public bool HasSerialNumber => detailSerialNumber != null && detailSerialNumber.Count > 0;

    public string SerialNumberDisplay
    {
        get
        {
            if (detailSerialNumber == null || detailSerialNumber.Count == 0) return "";
            double totalQty = detailSerialNumber.Sum(x => x.quantity);
            return $"SERIAL NUMBER ({totalQty})";
        }
    }

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

public class SOSerialNumberDetail
{
    public double quantity { get; set; }
    public SOSerialNumberInfo serialNumber { get; set; }
}

public class SOSerialNumberInfo
{
    public string number { get; set; }
}