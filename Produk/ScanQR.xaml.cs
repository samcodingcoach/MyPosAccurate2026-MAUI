using System.Globalization;
using System.Net.Http.Headers;
using Newtonsoft.Json;
using ZXing.Net.Maui;

namespace MyPosAccurate2026.Produk;

public partial class ScanQR : ContentPage
{
    private bool _isProcessing = false;
    private string _scannedItemNo = "";

    public ScanQR()
    {
        InitializeComponent();

        CameraScanner.Options = new BarcodeReaderOptions
        {
            Formats = BarcodeFormats.All,
            AutoRotate = true,
            Multiple = false
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
        if (status != PermissionStatus.Granted)
            status = await Permissions.RequestAsync<Permissions.Camera>();

        if (status != PermissionStatus.Granted)
        {
            await DisplayAlertAsync("Izin Kamera", "Aplikasi membutuhkan izin kamera untuk memindai barcode produk.", "OK");
            await Navigation.PopAsync();
            return;
        }

        _isProcessing = false;
        CameraScanner.IsDetecting = true;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        CameraScanner.IsDetecting = false;
    }

    private void CameraScanner_BarcodesDetected(object sender, BarcodeDetectionEventArgs e)
    {
        if (_isProcessing) return;

        var first = e.Results?.FirstOrDefault();
        if (first == null || string.IsNullOrWhiteSpace(first.Value)) return;

        _isProcessing = true;
        CameraScanner.IsDetecting = false;

        string no = first.Value.Trim();

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await LoadDetailBarang(no);
        });
    }

    private async Task LoadDetailBarang(string itemNo)
    {
        try
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                LoadingIndicator.IsVisible = true;
                LoadingIndicator.IsRunning = true;
            });

            string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
            if (string.IsNullOrEmpty(cleanToken))
            {
                await DisplayAlertAsync("Sesi Berakhir", "Token tidak ditemukan. Silakan login ulang.", "OK");
                ResetScanner();
                return;
            }

            string apiUrl = $"{App.API_HOST}item/detail_byNo.php?no={Uri.EscapeDataString(itemNo)}";

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);

                var response = await client.GetAsync(apiUrl);
                string responseContent = await response.Content.ReadAsStringAsync();

                if (responseContent.StartsWith("<"))
                {
                    await DisplayAlertAsync("Gagal", "Server mengembalikan respons tidak valid.", "OK");
                    ResetScanner();
                    return;
                }

                var apiResult = JsonConvert.DeserializeObject<DetailBarangResponse>(responseContent);

                if (apiResult == null || apiResult.status != "success" || apiResult.data?.d == null)
                {
                    await DisplayAlertAsync("Tidak Ditemukan", apiResult?.message ?? $"Barang dengan nomor {itemNo} tidak ditemukan.", "OK");
                    ResetScanner();
                    return;
                }

                var d = apiResult.data.d;
                _scannedItemNo = d.no;

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    var idID = new CultureInfo("id-ID");

                    nameProduct.Text = d.name;
                    noitemProduct.Text = d.no;
                    priceProduct.Text = $"Rp {d.unitPrice.ToString("N0", idID)}";

                    string unit = d.unit1?.name ?? "Unit";
                    stokavailableProduct.Text = $"{d.balance.ToString("N0", idID)} {unit}";

                    var gudang = d.detailWarehouseData?.FirstOrDefault();
                    warehouseProduct.Text = string.IsNullOrWhiteSpace(gudang?.name) ? "-" : gudang.name;

                    imageProduct.Source = BuildImageSource(d.no);

                    ExpanderResult.IsExpanded = true;
                });
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Gagal", $"Gagal memuat detail barang: {ex.Message}", "OK");
            ResetScanner();
        }
        finally
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                LoadingIndicator.IsVisible = false;
                LoadingIndicator.IsRunning = false;
            });
        }
    }

    private ImageSource BuildImageSource(string itemNo)
    {
        if (string.IsNullOrWhiteSpace(itemNo)) return "nophotoproduct150.jpg";
        string baseHost = App.API_HOST.Replace("api/", "");
        return $"{baseHost}images/{itemNo}.jpg";
    }

    private void ResetScanner()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _isProcessing = false;
            ExpanderResult.IsExpanded = false;
            CameraScanner.IsDetecting = true;
        });
    }

    private void BDetail_Clicked(object sender, EventArgs e)
    {
        if (!string.IsNullOrEmpty(_scannedItemNo))
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    // Hentikan proses kamera sebelum navigasi untuk menghindari JavaProxyThrowable (Bug di ZXing MAUI)
                    CameraScanner.IsDetecting = false;
                    await Task.Delay(150); // Jeda sejenak agar resource native surface Android terlepas dengan aman

                    await Navigation.PushAsync(new DetailScan(_scannedItemNo));
                }
                catch (Exception ex)
                {
                    await Application.Current.MainPage.DisplayAlertAsync("Error", ex.Message, "OK");
                }
            });
        }
    }

    private void BScanAgain_Clicked(object sender, EventArgs e)
    {
        ResetScanner();
    }

    // ===== Response DTO =====
    public class DetailBarangResponse
    {
        public string status { get; set; }
        public string message { get; set; }
        public DetailBarangData data { get; set; }
    }

    public class DetailBarangData
    {
        public bool s { get; set; }
        public DetailBarang d { get; set; }
    }

    public class DetailBarang
    {
        public int balance { get; set; }
        public double unitPrice { get; set; }
        public string name { get; set; }
        public string no { get; set; }
        public Unit unit1 { get; set; }
        public List<WarehouseData> detailWarehouseData { get; set; }
    }

    public class Unit
    {
        public string name { get; set; }
    }

    public class WarehouseData
    {
        public string balanceUnit { get; set; }
        public string pic { get; set; }
        public string name { get; set; }
    }
}
