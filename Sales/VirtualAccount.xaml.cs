using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;

namespace MyPosAccurate2026.Sales;

public partial class VirtualAccount : ContentPage
{
    private IDispatcherTimer _countdownTimer;
    private IDispatcherTimer _statusTimer;
    private TimeSpan _sisaWaktu = TimeSpan.FromHours(24); // VA umumnya kadaluarsa 24 jam
    private bool _isExpired = false;
    private bool _isPaid = false;
    private bool _isCheckingStatus = false;

    private static readonly CultureInfo IdCulture = new CultureInfo("id-ID");

    private string _orderId = "";       // diambil dari pembayaran-faktur
    private double _grossAmount = 0;    // total yang harus dibayar (faktur - diskon)
    private string _vaNumber = "";      // nomor virtual account
    private string _bankName = "Virtual Account";
    private string _transaction_id = "";
    private string _receiptNumber = "";                 // nomor struk hasil save-receipt (untuk halaman Print)
    private double _potonganVA = 4400;                  // beban VA: nilai potongan statis (fix)
    private string _accountNo_BebanVA = "600025";       // akun beban khusus Virtual Account
   

    // Data pembayaran yang dieksekusi (save-receipt.php) saat status settlement
    private QRIS.PaymentReceiptData _receiptData;

    public VirtualAccount()
    {
        InitializeComponent();
        LabelOrderId.Text = _orderId;
        LabelTotalPembayaran.Text = FormatRupiah(_grossAmount);
    }

    // Konstruktor: terima nomor VA yang sudah dibuat + data transaksi
    public VirtualAccount(string orderId, double grossAmount, string vaNumber, string bankName,
        QRIS.PaymentReceiptData receiptData) : this()
    {
        _vaNumber = vaNumber ?? "";
        _bankName = string.IsNullOrWhiteSpace(bankName) ? "Virtual Account" : bankName;
        _receiptData = receiptData;
        SetDataTransaksi(orderId, grossAmount);
    }

    public void SetDataTransaksi(string orderId, double grossAmount)
    {
        _orderId = orderId;
        _grossAmount = grossAmount;

        LabelOrderId.Text = orderId;
        LabelTotalPembayaran.Text = FormatRupiah(grossAmount);
        LabelBankName.Text = _bankName;
        UpdateVaDisplay();
    }

    // Tampilkan nomor VA: kartu (LabelVaNumber) + baris metode "{bank} - {nomor}" (LabelVa)
    private void UpdateVaDisplay()
    {
        bool ada = !string.IsNullOrWhiteSpace(_vaNumber);
        LabelVaNumber.Text = ada ? _vaNumber : "-";
        LabelVa.Text = ada ? $"{_bankName} - {_vaNumber}" : _bankName;
    }

    private static string FormatRupiah(double nilai) => $"Rp {nilai.ToString("N0", IdCulture)}";

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_grossAmount <= 0)
        {
            await DisplayAlertAsync("Virtual Account Gagal",
                "Nilai pembayaran tidak valid. Total harus lebih dari Rp 0.", "OK");
            await Navigation.PopAsync();
            return;
        }

        if (string.IsNullOrWhiteSpace(_vaNumber))
        {
            await DisplayAlertAsync("Virtual Account Gagal",
                "Nomor Virtual Account tidak tersedia.", "OK");
            await Navigation.PopAsync();
            return;
        }

        StartCountdown();

        // Daftarkan transaksi VA ke Midtrans, lalu mulai polling jika berhasil
        bool dibuat = await CreateVaAsync();
        if (dibuat)
            StartStatusPolling();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        StopCountdown();
        StopStatusPolling();
    }

    // === Buat Virtual Account via Midtrans (create_va-bca.php) ===
    private async Task<bool> CreateVaAsync()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            VaLoading.IsRunning = true;
            VaLoading.IsVisible = true;
        });

        try
        {
            using var client = new HttpClient();

            var payload = new
            {
                gross_amount = _grossAmount,
                order_id = _orderId,
                va_number = _vaNumber
            };

            var content = new StringContent(
                JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

            var response = await client.PostAsync(App.API_MIDTRANS + "create_va-bca.php", content);
            string responseContent = await response.Content.ReadAsStringAsync();

            if (string.IsNullOrWhiteSpace(responseContent) || responseContent.TrimStart().StartsWith("<"))
            {
                await ShowVaErrorAsync("Server mengembalikan respons tidak valid.");
                return false;
            }

            var result = JsonConvert.DeserializeObject<VaResponse>(responseContent);

            if (result != null && result.status == "success")
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    // Gunakan nomor VA & nama bank dari respons bila tersedia
                    if (result.data != null && !string.IsNullOrWhiteSpace(result.data.va_number))
                    {
                        _vaNumber = result.data.va_number;

                        // Nama bank dari respons (mis. "bca") → tampilkan kapital "BCA"
                        if (!string.IsNullOrWhiteSpace(result.data.bank))
                            _bankName = result.data.bank.ToUpperInvariant();

                        if (!string.IsNullOrWhiteSpace(result.data.transaction_id))
                            _transaction_id = result.data.transaction_id;

                        LabelBankName.Text = _bankName;
                        UpdateVaDisplay();
                    }
                    VaLoading.IsRunning = false;
                    VaLoading.IsVisible = false;
                });
                return true;
            }

            await ShowVaErrorAsync(result?.message ?? "Gagal membuat Virtual Account.");
            return false;
        }
        catch (Exception ex)
        {
            await ShowVaErrorAsync(ex.Message);
            return false;
        }
    }

    private async Task ShowVaErrorAsync(string message)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            VaLoading.IsRunning = false;
            VaLoading.IsVisible = false;
        });
        await DisplayAlertAsync("Virtual Account Gagal", message, "OK");
    }

    // === Salin nomor VA ke clipboard ===
    private async void TapCopy_Tapped(object sender, TappedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_vaNumber))
            return;

        await Clipboard.SetTextAsync(_vaNumber);
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            LabelCopy.Text = "TERSALIN";
            await Task.Delay(1500);
            LabelCopy.Text = "SALIN";
        });
    }

    // === Salin nomor VA dari baris Metode ("{bank} - {nomor}") ===
    private async void TapCopyVa_Tapped(object sender, TappedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_vaNumber))
            return;

        await Clipboard.SetTextAsync(_vaNumber);
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            LabelVaCopyIcon.Text = "✅"; // ✅ centang sebagai konfirmasi tersalin
            await Task.Delay(1500);
            LabelVaCopyIcon.Text = "\U0001F4CB"; // 📋 ikon salin
        });
    }

    // === Hitung mundur expired (format HH:MM:SS) ===
    private void StartCountdown()
    {
        StopCountdown();

        _countdownTimer = Dispatcher.CreateTimer();
        _countdownTimer.Interval = TimeSpan.FromSeconds(1);
        _countdownTimer.Tick += CountdownTimer_Tick;
        _countdownTimer.Start();

        UpdateCountdownLabel();
    }

    private void StopCountdown()
    {
        if (_countdownTimer != null)
        {
            _countdownTimer.Stop();
            _countdownTimer.Tick -= CountdownTimer_Tick;
            _countdownTimer = null;
        }
    }

    private void CountdownTimer_Tick(object sender, EventArgs e)
    {
        _sisaWaktu = _sisaWaktu.Subtract(TimeSpan.FromSeconds(1));

        if (_sisaWaktu <= TimeSpan.Zero)
        {
            _sisaWaktu = TimeSpan.Zero;
            StopCountdown();
            OnExpired();
        }

        UpdateCountdownLabel();
    }

    private void UpdateCountdownLabel()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            LabelCountdown.Text = $"{(int)_sisaWaktu.TotalHours:D2}:{_sisaWaktu.Minutes:D2}:{_sisaWaktu.Seconds:D2}";
        });
    }

    private void OnExpired()
    {
        _isExpired = true;
        StopStatusPolling();
        MainThread.BeginInvokeOnMainThread(() =>
        {
            LabelCountdown.Text = "00:00:00";
            LabelStatus.Text = "Kadaluarsa";
            LabelStatus.TextColor = Color.FromArgb("#C0392B");
            B_CekStatus.IsEnabled = false;
            B_CekStatus.Text = "VA KADALUARSA";
            B_CekStatus.BackgroundColor = Color.FromArgb("#B0B0B0");
        });
    }

    // === Polling cek status pembayaran tiap 5 detik ===
    private void StartStatusPolling()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            StopStatusPolling();

            _statusTimer = Dispatcher.CreateTimer();
            _statusTimer.Interval = TimeSpan.FromSeconds(5);
            _statusTimer.Tick += async (s, e) => await CekStatusPembayaranAsync();
            _statusTimer.Start();
        });
    }

    private void StopStatusPolling()
    {
        if (_statusTimer != null)
        {
            _statusTimer.Stop();
            _statusTimer = null;
        }
    }

    private async Task CekStatusPembayaranAsync()
    {
        if (_isExpired || _isPaid || _isCheckingStatus)
            return;

        _isCheckingStatus = true;
        try
        {
            using var client = new HttpClient();
            string url = App.API_MIDTRANS + "midtrans_status.php?order_id=" + Uri.EscapeDataString(_orderId);

            var response = await client.GetAsync(url);
            string responseContent = await response.Content.ReadAsStringAsync();

            if (string.IsNullOrWhiteSpace(responseContent) || responseContent.TrimStart().StartsWith("<"))
                return;

            var list = JsonConvert.DeserializeObject<List<StatusResponse>>(responseContent);
            if (list == null || list.Count == 0)
                return;

            string status = (list[0].transaction_status ?? "").ToLowerInvariant();

            if (status == "settlement" || status == "capture")
            {
                _transaction_id = list[0].transaction_id;
                OnPaid();
            }
            else if (status == "expire" || status == "deny" || status == "cancel" || status == "failure")
            {
                StopStatusPolling();
                StopCountdown();
                OnExpired();
            }
            // "pending" => tetap menunggu, polling lanjut
        }
        catch
        {
            // abaikan error sementara, polling lanjut di tick berikutnya
        }
        finally
        {
            _isCheckingStatus = false;
        }
    }

    private void OnPaid()
    {
        _isPaid = true;
        StopStatusPolling();
        StopCountdown();

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            LabelStatus.Text = "Pembayaran Berhasil";
            LabelStatus.TextColor = Color.FromArgb("#27AE60");
            B_CekStatus.IsEnabled = false;
            B_CekStatus.Text = "MENYIMPAN PEMBAYARAN...";
            B_CekStatus.BackgroundColor = Color.FromArgb("#27AE60");

            bool tersimpan = await SimpanPembayaranAsync();

            if (tersimpan)
            {
                // Lanjut ke pratinjau struk (instance baru agar stack bersih)
                Application.Current.MainPage = new NavigationPage(
                    new Print(_receiptNumber, _receiptData?.InvoiceNo));
            }
            else
            {
                B_CekStatus.Text = "PEMBAYARAN BERHASIL";
            }
        });
    }

    // Simpan pembayaran ke save-receipt.php (VA: tanpa beban merchant)
    private async Task<bool> SimpanPembayaranAsync()
    {
        if (_receiptData == null)
            return false;

        // VA: nilai yang diterima = gross (faktur - diskon) dipotong beban VA statis
        double chequeAmount = _grossAmount - _potonganVA;
        if (chequeAmount < 0) chequeAmount = 0;

        var detailDiscount = new List<object>();

        // 1. Diskon pembayaran (jika ada)
        if (_receiptData.DiskonPembayaran > 0)
        {
            detailDiscount.Add(new
            {
                accountNo = int.Parse(_receiptData.DiskonAccountNo),
                amount = _receiptData.DiskonPembayaran
            });
        }

        // 2. Beban VA = nilai statis (fix) — khusus pembayaran Virtual Account
        detailDiscount.Add(new
        {
            accountNo = int.Parse(_accountNo_BebanVA),
            amount = _potonganVA
        });

        var detailInvoice = new List<object>
        {
            new
            {
                invoiceNo = _receiptData.InvoiceNo,
                paymentAmount = _receiptData.PaymentAmount, // gross sebelum diskon
                detailDiscount = detailDiscount
            }
        };

        var payload = new
        {
            bankNo = _receiptData.BankNo,
            number = _receiptData.Number ?? "",
            chequeAmount = chequeAmount,
            customerNo = _receiptData.CustomerNo,
            transDate = _receiptData.TransDate,
            chequeDate = _receiptData.TransDate,
            paymentMethod = _receiptData.PaymentMethod,
            description = _receiptData.Description ?? "",
            charField1 = _transaction_id, // referensi transaksi VA
            charField2 = _receiptData.CharField2,
            charField3 = _vaNumber,
            detailInvoice = detailInvoice
        };

        try
        {
            string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
            string apiUrl = $"{App.API_HOST}penjualan/save-receipt.php";

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);

            string jsonPayload = JsonConvert.SerializeObject(payload);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            var response = await client.PostAsync(apiUrl, content);
            string responseString = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                // Simpan nomor struk untuk ditampilkan di halaman Print
                _receiptNumber = ExtractReceiptNumber(responseString);
                return true;
            }

            await DisplayAlertAsync("Gagal Menyimpan", $"Sistem merespons: {responseString}", "OK");
            return false;
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error Koneksi", $"Terjadi kesalahan: {ex.Message}", "OK");
            return false;
        }
    }

    private async void B_CekStatus_Clicked(object sender, EventArgs e)
    {
        if (_isExpired || _isPaid)
            return;

        await CekStatusPembayaranAsync();

        if (!_isPaid)
            await DisplayAlertAsync("Cek Status", "Pembayaran masih menunggu. Silakan selesaikan pembayaran Virtual Account.", "OK");
    }

    private async void TapTutup_Tapped(object sender, TappedEventArgs e)
    {
        bool konfirmasi = await DisplayAlertAsync("Batalkan Virtual Account",
            "Tutup halaman pembayaran Virtual Account?", "Ya", "Tidak");
        if (konfirmasi)
        {
            StopCountdown();
            StopStatusPolling();
            await Navigation.PopAsync();
        }
    }

    // Ambil nomor struk dari respon save-receipt.php (mendukung bentuk data.number atau number di root)
    private static string ExtractReceiptNumber(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.TrimStart().StartsWith("<"))
            return "";
        try
        {
            var jo = Newtonsoft.Json.Linq.JObject.Parse(json);
            return (string)(jo["data"]?["number"] ?? jo["number"]) ?? "";
        }
        catch
        {
            return "";
        }
    }

    // Response midtrans_status.php (berupa array JSON)
    private class StatusResponse
    {
        public string order_id { get; set; }
        public string gross_amount { get; set; }
        public string transaction_status { get; set; }
        public string settlement_time { get; set; }
        public string transaction_id { get; set; }
    }

    // Response create_va-bca.php
    private class VaResponse
    {
        public string status { get; set; }
        public string message { get; set; }
        public VaData data { get; set; }
    }

    private class VaData
    {
        public string transaction_id { get; set; }
        public string order_id { get; set; }
        public string gross_amount { get; set; }
        public string payment_type { get; set; }
        public string transaction_status { get; set; }
        public string va_number { get; set; }
        public string bank { get; set; }
        public string mode { get; set; }
    }
}
