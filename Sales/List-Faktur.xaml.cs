using System.Text;
using Newtonsoft.Json;
using System.Net.Http;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.ObjectModel;
using System.Net.Http.Headers;
using UXDivers.Popups.Maui.Controls;

namespace MyPosAccurate2026.Sales;

public partial class List_Faktur : ContentPage
{
    public ObservableCollection<InvoiceData> InvoiceList { get; set; } = new ObservableCollection<InvoiceData>();

    
    private double _grandTotalAmount = 0;
    private int _currentPage = 1;
    private bool _isFetching = false;
    private bool _hasMoreData = true;

  
    private string _activeStartDate = "";
    private string _activeEndDate = "";
    private string _activeSearch = "";

   
    public string FormattedGrandTotal => $"Rp {_grandTotalAmount:N0}";

    public List_Faktur()
    {
        InitializeComponent();

        // Wajib diset agar {Binding FormattedGrandTotal} dapat terbaca oleh layar utama
        BindingContext = this;
        InvoiceCollectionView.ItemsSource = InvoiceList;

        // Set default nilai input tanggal ke Hari Ini menggunakan x:Name Anda
        DP_startdate.Date = DateTime.Today;
        DP_enddate.Date = DateTime.Today;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        ResetFilterState();
        await LoadDataFromServer(isRefresh: true);
    }

    
    private void ResetFilterState()
    {
        DP_startdate.Date = DateTime.Today;
        DP_enddate.Date = DateTime.Today;
        Search_FakturKonsumen.Text = string.Empty;

        _activeStartDate = $"{DateTime.Today:yyyy-MM-dd}";
        _activeEndDate = $"{DateTime.Today:yyyy-MM-dd}";
        _activeSearch = string.Empty;
    }

    
    private async void OnLoadMoreItems(object sender, EventArgs e)
    {
        // Tarik data mode Load More (lanjut ke page berikutnya)
        await LoadDataFromServer(isRefresh: false);
    }

  
    private async Task LoadDataFromServer(bool isRefresh)
    {
        // Cegah pemanggilan ganda / cegah load jika data sudah habis
        if (_isFetching || (!_hasMoreData && !isRefresh)) return;

        _isFetching = true;

        if (isRefresh)
        {
            _currentPage = 1;
            _grandTotalAmount = 0;
            _hasMoreData = true;
            InvoiceList.Clear();
            OnPropertyChanged(nameof(FormattedGrandTotal)); // Beritahu UI kalau nilai jadi 0 sementara
        }

        try
        {
            // Susun Endpoint beserta paging
            var urlBuilder = new StringBuilder($"{App.API_HOST}penjualan/list-invoice.php?page={_currentPage}&limit=100");

            // Filter Parameter logic
            if (!string.IsNullOrEmpty(_activeSearch))
            {
                urlBuilder.Append($"&search={Uri.EscapeDataString(_activeSearch)}");
            }
            else
            {
                urlBuilder.Append($"&start_date={_activeStartDate}&end_date={_activeEndDate}");
            }

            string apiUrl = urlBuilder.ToString();

            string secureToken = Preferences.Get("TOKEN_KEY", "");
            string cleanToken = secureToken.Replace("Bearer ", "").Trim();

            if (string.IsNullOrEmpty(cleanToken))
            {
                await DisplayAlertAsync("Sesi Habis", "Anda harus login kembali.", "OK");
                _isFetching = false;
                return;
            }

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);

                var response = await client.GetAsync(apiUrl);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (responseContent.StartsWith("<"))
                {
                    await DisplayAlertAsync("Error Server", "Gagal membaca format data dari server.", "OK");
                    _isFetching = false;
                    return;
                }

                var responseObject = JsonConvert.DeserializeObject<InvoiceListResponse>(responseContent);

                if (responseObject != null && responseObject.status == "success")
                {
                    var data = responseObject.data;

                    if (data == null || data.Count == 0)
                    {
                        _hasMoreData = false; // Tandai data sudah habis dari API
                    }
                    else
                    {
                        foreach (var invoice in data)
                        {
                            InvoiceList.Add(invoice);

                            // Tambahkan total uang ke Grand Total
                            _grandTotalAmount += invoice.totalAmount;
                        }

                        // Beri tahu halaman agar text Grand Total ter-update di layar UI
                        OnPropertyChanged(nameof(FormattedGrandTotal));

                        // Jika data yg didapat < limit(100), maka tidak ada lagi halaman berikutnya
                        if (data.Count < 100)
                        {
                            _hasMoreData = false;
                        }
                        else
                        {
                            _currentPage++;
                        }
                    }
                }
                else
                {
                    string errorServer = responseObject?.message ?? "Format respons server tidak sesuai.";
                    await DisplayAlertAsync("Gagal (Respon Server)", errorServer, "OK");
                }
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Koneksi Gagal", ex.Message, "OK");
        }
        finally
        {
            _isFetching = false;
        }
    }

    public class InvoiceListResponse
    {
        public string status { get; set; }
        public string message { get; set; }
        public List<InvoiceData> data { get; set; }
    }

    public class InvoiceData
    {
        public string number { get; set; }
        public double totalAmount { get; set; }
        public string transDate { get; set; }
        public string statusName { get; set; }
        public int id { get; set; }
        public string transDateView { get; set; }
        public CustomerData customer { get; set; }

        public string FormattedTotalAmount => $"Rp {totalAmount:N0}";
        public string CustomerDisplay => $"{customer?.customerNo} - {customer?.name}";

        public Color StatusTextColor =>
            statusName?.Trim() == "Lunas"
            ? Color.FromArgb("#155724")
            : Color.FromArgb("#ff4f4f");

        public Color StatusBgColor =>
            statusName?.Trim() == "Lunas"
            ? Color.FromArgb("#d4edda")
            : Color.FromArgb("#ff9191");
    }

    public class CustomerData
    {
        public string name { get; set; }
        public int id { get; set; }
        public string customerNo { get; set; }
    }

    

    private async void TapFilter_Tapped(object sender, TappedEventArgs e)
    {
        _activeStartDate = $"{DP_startdate.Date:yyyy-MM-dd}";
        _activeEndDate = $"{DP_enddate.Date:yyyy-MM-dd}";
        _activeSearch = Search_FakturKonsumen.Text?.Trim() ?? string.Empty;
        await LoadDataFromServer(isRefresh: true);
    }

    private async void TapReset_Tapped(object sender, TappedEventArgs e)
    {
        ResetFilterState();
        await LoadDataFromServer(isRefresh: true);
    }

    private async void TapNewFak_Tapped(object sender, TappedEventArgs e)
    {
        await Navigation.PushAsync(new Sales.New_Faktur());
    }

    // =========================================================
    // MODEL DATA PENANGKAP JSON DETAIL INVOICE API
    // =========================================================
    public class DetailInvoiceResponse
    {
        public string status { get; set; }
        public DetailInvoiceData data { get; set; }
    }

    public class DetailInvoiceData
    {
        public int id { get; set; }
        public double tax1Amount { get; set; }
        public int numericField1 { get; set; }
        public int numericField2 { get; set; }
        public int numericField3 { get; set; }
        public string poNumber { get; set; }
        public string toAddress { get; set; }
        public ShipmentInfo shipment { get; set; }
        public string description { get; set; }
        public string transDate { get; set; }
        public double cashDiscount { get; set; }
        public string number { get; set; }
        public CustomerInfo customer { get; set; }
        public List<DetailExpenseInfo> detailExpense { get; set; }
        public List<DetailItemInfo> detailItem { get; set; }
    }

    public class ShipmentInfo { public string name { get; set; } }
    public class CustomerInfo { public string name { get; set; } public string customerNo { get; set; } }

    public class DetailExpenseInfo
    {
        public int id { get; set; }
        public string detailName { get; set; }
        public double expenseAmount { get; set; }
        public AccountInfo account { get; set; }
    }
    public class AccountInfo { public string no { get; set; } }

    public class DetailItemInfo
    {
        public int id { get; set; }
        public ItemDetailNo item { get; set; }
        public string detailName { get; set; }
        public double unitPrice { get; set; }
        public double quantity { get; set; }
        public double? itemDiscPercent { get; set; }
        public WarehouseInfo warehouse { get; set; }
        public List<SalesmanInfo> salesmanList { get; set; }
        public List<DetailSnInfo> detailSerialNumber { get; set; }
    }

    public class ItemDetailNo { public string no { get; set; } }
    public class WarehouseInfo { public string name { get; set; } }
    public class SalesmanInfo { public string number { get; set; } }

    public class DetailSnInfo
    {
        public int id { get; set; }
        public int quantity { get; set; }
        public SnNumberInfo serialNumber { get; set; }
    }
    public class SnNumberInfo { public string number { get; set; } }

    private async void InvoiceItem_Tapped(object sender, TappedEventArgs e)
    {
        if (sender is Border border && border.BindingContext is InvoiceData invoice)
        {
            if (invoice.statusName?.Trim().Equals("Belum Lunas", StringComparison.OrdinalIgnoreCase) == true)
            {
                string action = await DisplayActionSheetAsync($"Pilih action invoice: {invoice.number} ?", "Cancel", null, "Edit", "Hapus","Bayar");

                if (action == "Edit")
                {
                    try
                    {
                        string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
                        string apiUrl = $"{App.API_HOST}penjualan/detail-invoice.php?number={invoice.number}";

                        using (var client = new HttpClient())
                        {
                            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);

                            var response = await client.GetAsync(apiUrl);
                            string responseContent = await response.Content.ReadAsStringAsync();

                            if (response.IsSuccessStatusCode)
                            {
                                var apiResult = JsonConvert.DeserializeObject<DetailInvoiceResponse>(responseContent);

                                if (apiResult != null && apiResult.status == "success" && apiResult.data != null)
                                {
                                    // PENTING: Paksa pindah ke UI Thread agar tidak crash di Windows Machine!
                                    MainThread.BeginInvokeOnMainThread(async () =>
                                    {
                                        var editPage = new Sales.New_Faktur();
                                        editPage.LoadEditData(apiResult.data);
                                        await Navigation.PushAsync(editPage);
                                    });
                                }
                                else
                                {
                                    MainThread.BeginInvokeOnMainThread(async () =>
                                        await DisplayAlertAsync("Gagal", "Format data detail faktur tidak valid.", "OK"));
                                }
                            }
                            else
                            {
                                MainThread.BeginInvokeOnMainThread(async () =>
                                    await DisplayAlertAsync("Gagal", $"Gagal mengambil detail: {response.StatusCode}", "OK"));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MainThread.BeginInvokeOnMainThread(async () =>
                            await DisplayAlertAsync("Error", $"Koneksi gagal: {ex.Message}", "OK"));
                    }
                }
                else if (action == "Hapus")
                {
                    // 1. Berikan peringatan konfirmasi sebelum menghapus
                    bool confirm = await DisplayAlertAsync("Konfirmasi Hapus", $"Apakah Anda yakin ingin menghapus faktur {invoice.number}?", "Ya, Hapus", "Batal");
                    if (!confirm) return;

                    try
                    {
                        string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
                        string apiUrl = $"{App.API_HOST}penjualan/delete-invoice.php";

                        // 2. Susun Payload JSON sesuai permintaan endpoint
                        var payload = new
                        {
                            number = invoice.number
                        };

                        using (var client = new HttpClient())
                        {
                            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);

                            string jsonPayload = JsonConvert.SerializeObject(payload);

                            // 3. Gunakan HttpRequestMessage untuk mengirim Body JSON pada method DELETE
                            var request = new HttpRequestMessage(HttpMethod.Delete, apiUrl)
                            {
                                Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
                            };

                            // Eksekusi API
                            var response = await client.SendAsync(request);
                            string responseString = await response.Content.ReadAsStringAsync();

                            // 4. Proses Hasil (Gunakan MainThread untuk menghindari Crash UI)
                            MainThread.BeginInvokeOnMainThread(async () =>
                            {
                                if (response.IsSuccessStatusCode)
                                {
                                    await DisplayAlertAsync("Sukses", $"Faktur {invoice.number} berhasil dihapus dari sistem.", "OK");

                                    // Refresh layar secara otomatis setelah berhasil dihapus
                                    await LoadDataFromServer(isRefresh: true);
                                }
                                else
                                {
                                    await DisplayAlertAsync("Gagal Menghapus", $"Sistem merespons: {responseString}", "OK");
                                }
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        MainThread.BeginInvokeOnMainThread(async () =>
                            await DisplayAlertAsync("Error Koneksi", $"Terjadi kesalahan saat menghapus: {ex.Message}", "OK"));
                    }
                    System.Diagnostics.Debug.WriteLine($"Menghapus Faktur: {invoice.number}");
                }

                else if(action == "Bayar")
                {
                    bool confirm = await DisplayAlertAsync("Konfirmasi Bayar", $"Apakah Anda yakin ingin bayar {invoice.number}?", "Ya, Bayar", "Batal");
                    if (!confirm) return;

                    MainThread.BeginInvokeOnMainThread(async () =>
                        await Navigation.PushAsync(new Sales.Pembayaran_Faktur(invoice.number)));
                }
            }
        }
    }

    private async void DP_enddate_DateSelected(object sender, DateChangedEventArgs e)
    {
        _activeStartDate = $"{DP_startdate.Date:yyyy-MM-dd}";
        _activeEndDate = $"{DP_enddate.Date:yyyy-MM-dd}";
        _activeSearch = Search_FakturKonsumen.Text?.Trim() ?? string.Empty;
        await LoadDataFromServer(isRefresh: true);
    }

    private async void DP_startdate_DateSelected(object sender, DateChangedEventArgs e)
    {
        _activeStartDate = $"{DP_startdate.Date:yyyy-MM-dd}";
        _activeEndDate = $"{DP_enddate.Date:yyyy-MM-dd}";
        _activeSearch = Search_FakturKonsumen.Text?.Trim() ?? string.Empty;
        await LoadDataFromServer(isRefresh: true);
    }

    private async void TapDetailPembayaran_Tapped(object sender, TappedEventArgs e)
    {
        if (sender is Image image)
        {
            await image.FadeToAsync(0.3, 100); // Turunkan opacity ke 0.3 dalam 100ms
            await image.FadeToAsync(1, 200);   // Kembalikan opacity ke 1 dalam 200ms
            
            if (image.BindingContext is InvoiceData invoice)
            {
                // await Navigation.PushAsync(new Sales.Detail_Faktur2(invoice.number));

                var page = new Sales.Detail_Faktur2(invoice.number);
                page.HasHandle = true;
                page.HasBackdrop = true;
                //page.HandleColor = Color.FromArgb()
                _ = page.ShowAsync(Window);


            }
        }
    }
}