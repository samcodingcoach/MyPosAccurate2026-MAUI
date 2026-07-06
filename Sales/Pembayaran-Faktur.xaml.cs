using System;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using Newtonsoft.Json;
using System.Globalization;
namespace MyPosAccurate2026.Sales;

public partial class Pembayaran_Faktur : ContentPage
{
    string bankNo { get; set; }
    string nomor_faktur { get; set; }

    string DiskonAccountNo = "720009";
   
    string pembulatanNo = "720008"; 

    double nilai_pembulatan { get; set; }
    double nilai_selisih_pembulatan { get; set; }

    string nomor_pelanggan { get; set; }

    string paymentMethodVal { get; set; }

    string charFieldString2 { get; set; }

    // Nilai total faktur asli (sebelum diskon pembayaran) — dipakai sebagai batas maksimal diskon
    double _totalAmountFaktur = 0;
    // Nilai diskon pembayaran yang sedang aktif
    double _diskonPembayaran = 0;
    // Guard agar TextChanged tidak infinite-loop saat menyisipkan pemisah ribuan
    bool _isFormattingDiskon = false;

    // Total yang harus dibayar konsumen (setelah diskon & pembulatan) — acuan kembalian
    double _totalTagihan = 0;
    // Nominal yang dibayarkan konsumen
    double _nominalBayarKonsumen = 0;
    // Guard format ribuan untuk nominal bayar konsumen
    bool _isFormattingBayar = false;

    public Pembayaran_Faktur(string nomorFaktur)
	{
		InitializeComponent();

        // Simpan nomor faktur yang dikirim dari List-Faktur
        nomor_faktur = nomorFaktur;

        // Atur tanggal default = hari ini dan izinkan memilih tanggal ke depan (mis. besok)
        PickerTanggalBayar.MinimumDate = new DateTime(2000, 1, 1);
        PickerTanggalBayar.MaximumDate = DateTime.Today.AddYears(5);
        PickerTanggalBayar.Date = DateTime.Today;

        PickerBank.ItemDisplayBinding = new Binding("name");

        // Panggil fungsi pengambilan data saat halaman dibuka
        _ = LoadKasBankData();
        _ = LoadDetailFaktur();

        // Tampilkan nomor faktur pada form
        FormNumber.Text = nomor_faktur;

        //id user
        string idUser = Preferences.Get("ID_USER", "");
        string userName = Preferences.Get("USERNAME", "");
        charFieldString2 = $"{idUser} - {userName}";

    }


    
    public class DetailPembayaranResponse
    {
        public string status { get; set; }
        public DetailPembayaranData data { get; set; }
    }

    public class DetailPembayaranData
    {
        public double subTotal { get; set; }
        public double cashDiscount { get; set; }
        public double totalExpense { get; set; }
        public double tax1Amount { get; set; }
        public double totalAmount { get; set; }
        public string number { get; set; }
        public string description { get; set; }
        public string transDate { get; set; }
        public CustomerInvoiceData customer { get; set; }
        public List<DetailItemPembayaran> detailItem { get; set; }
    }

    public class CustomerInvoiceData
    {
        public string name { get; set; }
        public string customerNo { get; set; }
    }

    public class DetailItemPembayaran
    {
        public ItemDetailPembayaran item { get; set; }
        public double itemCashDiscount { get; set; }
        public double totalPrice { get; set; }
        public double quantity { get; set; }

        // Properti pembantu (Helper) untuk ditampilkan langsung di BindableLayout XAML
        public string QtyAndPrice => $"{quantity} x Rp {item?.unitPrice.ToString("N0", new CultureInfo("id-ID"))}";
        public string FormattedItemDiscount => itemCashDiscount > 0 ? $"- Rp {itemCashDiscount.ToString("N0", new CultureInfo("id-ID"))}" : "Rp 0";
        public string FormattedTotalPrice => $"Rp {totalPrice.ToString("N0", new CultureInfo("id-ID"))}";
    }

    public class ItemDetailPembayaran
    {
        public double unitPrice { get; set; }
        public string name { get; set; }
        public string no { get; set; }
    }

    private async Task LoadDetailFaktur()
    {
        try
        {
            string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
            if (string.IsNullOrEmpty(cleanToken)) return;

            // Memanggil endpoint berdasarkan variabel nomor_faktur
            string apiUrl = $"{App.API_HOST}penjualan/detail-invoice.php?number={Uri.EscapeDataString(nomor_faktur)}";

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);
                var response = await client.GetAsync(apiUrl);

                if (response.IsSuccessStatusCode)
                {
                    string responseContent = await response.Content.ReadAsStringAsync();
                    var apiResult = JsonConvert.DeserializeObject<DetailPembayaranResponse>(responseContent);

                    if (apiResult != null && apiResult.data != null)
                    {
                        var inv = apiResult.data;

                        // Simpan total faktur asli sebagai batas maksimal diskon pembayaran
                        _totalAmountFaktur = inv.totalAmount;

                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            // 1. Mapping Informasi Konsumen

                            FormNameCustomerNo.Text = $"{inv.customer?.customerNo} - {inv.customer?.name}";
                            nomor_pelanggan = inv.customer?.customerNo;

                            // Mapping keterangan faktur ke kolom keterangan pembayaran
                            EntryKeterangan.Text = inv.description ?? "";

                            // 2. Mapping Jumlah Pembayaran Default
                            FormtotalAmount.Text = inv.totalAmount.ToString("N0", new CultureInfo("id-ID"));

                            // 3. Mapping Daftar Barang ke BindableLayout UI
                            ListBarangContainer.ItemsSource = inv.detailItem;

                            // 4. Hitung jumlah item
                            double totalQty = inv.detailItem?.Sum(x => x.quantity) ?? 0;
                            LabelHeaderDetail.Text = $"Detail Barang ({totalQty} items)";
                           // LabelItemCount.Text = $"{totalQty} item";

                            // 5. Mapping Ringkasan Uang
                            LabelTotalDiskon.Text = $"Rp {inv.cashDiscount.ToString("N0", new CultureInfo("id-ID"))}";
                            LabelTotalBiaya.Text = $"Rp {inv.totalExpense.ToString("N0", new CultureInfo("id-ID"))}";
                            LabelTotalPajak.Text = $"Rp {inv.tax1Amount.ToString("N0", new CultureInfo("id-ID"))}";
                            LabelGrandTotal.Text = $"Rp {inv.totalAmount.ToString("N0", new CultureInfo("id-ID"))}";

                            // 6. Hitung Pembulatan ke Bawah (Kelipatan Ratusan)
                            double pembulatan = Math.Floor(inv.totalAmount / 100) * 100;
                            nilai_pembulatan = pembulatan;
                            LabelPembulatan.Text = $"Rp {pembulatan.ToString("N0", new CultureInfo("id-ID"))}";

                            // Selisih pembulatan (mis. 1021 -> 21), tampung & tampilkan
                            nilai_selisih_pembulatan = inv.totalAmount - pembulatan;
                            TampilkanSelisihPembulatan();

                            // Set total tagihan awal sebagai acuan kembalian (sebelum ada diskon)
                            _totalTagihan = pembulatan;
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Gagal memuat detail faktur: {ex.Message}");
        }
    }

    private void PickerBank_SelectedIndexChanged(object sender, EventArgs e)
    {
        // Tangkap objek dari item yang dipilih pengguna
        if (PickerBank.SelectedItem is KasBankData selectedBank)
        {
            System.Diagnostics.Debug.WriteLine($"Bank Dipilih: {selectedBank.name} dengan ID: {selectedBank.id} & No: {selectedBank.no}");
            bankNo = selectedBank.no;

            // Tentukan metode pembayaran berdasarkan nama Kas/Bank
            string namaBank = selectedBank.name ?? "";
            bool isTunai = namaBank.IndexOf("Tunai", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isBank = namaBank.IndexOf("BANK", StringComparison.OrdinalIgnoreCase) >= 0;

            if (namaBank.IndexOf("QRIS", StringComparison.OrdinalIgnoreCase) >= 0)
                paymentMethodVal = "QRIS";
            else if (isTunai)
                paymentMethodVal = "CASH_OTHER";
            else if (isBank)
                paymentMethodVal = "BANK_TRANSFER";

            // Pembulatan, Nominal Pembayaran, dan Kembalian hanya relevan untuk Tunai
            RowPembulatan.IsVisible = isTunai;
            ViewNominalPembayaran.IsVisible = isTunai;

            // Input Nomor Virtual Account muncul saat memilih Transfer Bank
            bool isBankTransfer = paymentMethodVal == "BANK_TRANSFER";
            RowNoVA.IsVisible = isBankTransfer;
            LineNoVA.IsVisible = isBankTransfer;

            System.Diagnostics.Debug.WriteLine($"paymentMethodVal: {paymentMethodVal}");
        }
    }

    private async Task LoadKasBankData()
    {
        try
        {
            string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
            if (string.IsNullOrEmpty(cleanToken)) return;

            string apiUrl = $"{App.API_HOST}coa/list-kasbank.php";

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);
                var response = await client.GetAsync(apiUrl);

                if (response.IsSuccessStatusCode)
                {
                    string responseContent = await response.Content.ReadAsStringAsync();
                    var apiResult = JsonConvert.DeserializeObject<KasBankResponse>(responseContent);

                    if (apiResult != null && apiResult.data != null)
                    {
                        // Sembunyikan item dengan value "Kas Kecil"
                        var filteredData = apiResult.data
                            .Where(b => b.name != null && b.name.IndexOf("Kas Kecil", StringComparison.OrdinalIgnoreCase) < 0)
                            .ToList();

                        // Eksekusi perubahan UI wajib menggunakan MainThread
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            PickerBank.ItemsSource = filteredData;
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Gagal memuat daftar Kas/Bank: {ex.Message}");
        }
    }


    public class KasBankResponse
    {
        public List<KasBankData> data { get; set; }
    }

    public class KasBankData
    {
        public string no { get; set; }
        public string name { get; set; }
        public int id { get; set; }
    }

    private async void B_SimpanPembayaran_Clicked(object sender, EventArgs e)
    {
        // ===== Validasi =====
        if (_totalAmountFaktur <= 0)
        {
            await DisplayAlertAsync("Peringatan", "Data faktur belum dimuat. Mohon tunggu sejenak.", "OK");
            return;
        }

        if (string.IsNullOrEmpty(bankNo))
        {
            await DisplayAlertAsync("Peringatan", "Silakan pilih Metode Bayar (Bank) terlebih dahulu.", "OK");
            return;
        }

        // Khusus Tunai: nominal bayar konsumen harus mencukupi total tagihan
        if (paymentMethodVal == "CASH_OTHER" && _nominalBayarKonsumen < _totalTagihan)
        {
            await DisplayAlertAsync("Peringatan",
                "Nominal pembayaran kurang dari total tagihan. Mohon lengkapi nominal pembayaran.", "OK");
            return;
        }

        // Nilai yang dibayar = nilai setelah pembulatan (nilai_pembulatan)
        double chequeAmount = nilai_pembulatan;
        if (chequeAmount < 0) chequeAmount = 0;

        // Tanggal bayar (default hari ini, mengikuti pilihan DatePicker)
        string tanggal = PickerTanggalBayar.Date.GetValueOrDefault(DateTime.Today).ToString("yyyy-MM-dd");

        // ===== Khusus QRIS: alihkan ke halaman QRIS =====
        // Simpan pembayaran baru dieksekusi di halaman QRIS setelah status settlement.
        if (paymentMethodVal == "QRIS")
        {
            // Gross amount ke Midtrans = total faktur tanpa pembulatan, dikurangi diskon
            double grossAmount = _totalAmountFaktur - _diskonPembayaran;
            if (grossAmount < 0) grossAmount = 0;

            // Bawa data yang dibutuhkan untuk save-receipt.php (chequeAmount & beban dihitung di QRIS)
            var receiptData = new QRIS.PaymentReceiptData
            {
                BankNo = bankNo,
                Number = string.IsNullOrWhiteSpace(EntryNoBukti.Text) ? "" : EntryNoBukti.Text.Trim(),
                CustomerNo = nomor_pelanggan,
                TransDate = tanggal,
                PaymentMethod = paymentMethodVal,
                Description = EntryKeterangan.Text ?? "",
                CharField2 = charFieldString2,
                InvoiceNo = nomor_faktur,
                PaymentAmount = _totalAmountFaktur, // gross sebelum diskon & beban
                DiskonPembayaran = _diskonPembayaran,
                DiskonAccountNo = DiskonAccountNo
            };

            await Navigation.PushAsync(new QRIS(nomor_faktur, grossAmount, receiptData));
            return;
        }

        // ===== Khusus Transfer Bank: alihkan ke halaman Virtual Account =====
        // Simpan pembayaran baru dieksekusi di halaman VA setelah status settlement.
        if (paymentMethodVal == "BANK_TRANSFER")
        {
            string vaNumber = (EntryNoVA.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(vaNumber))
            {
                await DisplayAlertAsync("Peringatan", "Nomor Virtual Account wajib diisi.", "OK");
                return;
            }

            // Gross amount ke Midtrans = total faktur dikurangi diskon (nilai asli, tanpa pembulatan)
            double grossAmount = _totalAmountFaktur - _diskonPembayaran;
            if (grossAmount < 0) grossAmount = 0;

            var receiptData = new QRIS.PaymentReceiptData
            {
                BankNo = bankNo,
                Number = string.IsNullOrWhiteSpace(EntryNoBukti.Text) ? "" : EntryNoBukti.Text.Trim(),
                CustomerNo = nomor_pelanggan,
                TransDate = tanggal,
                PaymentMethod = paymentMethodVal,
                Description = EntryKeterangan.Text ?? "",
                CharField2 = charFieldString2,
                InvoiceNo = nomor_faktur,
                PaymentAmount = _totalAmountFaktur, // gross sebelum diskon
                DiskonPembayaran = _diskonPembayaran,
                DiskonAccountNo = DiskonAccountNo
            };

            // Nama bank untuk ditampilkan di halaman VA
            string namaBank = (PickerBank.SelectedItem as KasBankData)?.name ?? "Virtual Account";

            await Navigation.PushAsync(new VirtualAccount(nomor_faktur, grossAmount, vaNumber, namaBank, receiptData));
            return;
        }

        // ===== Susun detailDiscount =====
        var detailDiscount = new List<object>();
        if (_diskonPembayaran > 0)
        {
            detailDiscount.Add(new
            {
                accountNo = int.Parse(DiskonAccountNo), // kirim sebagai angka
                amount = _diskonPembayaran
            });
        }
        // Selisih pembulatan: hanya untuk Tunai (CASH_OTHER) dan bila selisih > 0
        if (paymentMethodVal == "CASH_OTHER" && nilai_selisih_pembulatan > 0)
        {
            detailDiscount.Add(new
            {
                accountNo = int.Parse(pembulatanNo),
                amount = nilai_selisih_pembulatan
            });
        }

        // ===== Susun detailInvoice =====
        // paymentAmount = total faktur (dari FormtotalAmount.Text)
        string rawPaymentAmount = (FormtotalAmount.Text ?? "")
            .Replace("Rp", "").Replace(".", "").Replace(" ", "").Trim();
        double.TryParse(rawPaymentAmount, out double paymentAmount);

        var detailInvoice = new List<object>
        {
            new
            {
                invoiceNo = FormNumber.Text,
                paymentAmount = paymentAmount,
                detailDiscount = detailDiscount
            }
        };

        // ===== Payload akhir =====
        var payload = new
        {
            bankNo = bankNo,
            number = string.IsNullOrWhiteSpace(EntryNoBukti.Text) ? "" : EntryNoBukti.Text.Trim(),
            chequeAmount = chequeAmount,
            customerNo = nomor_pelanggan,
            transDate = tanggal,
            chequeDate = tanggal,
            paymentMethod = paymentMethodVal,
            description = EntryKeterangan.Text ?? "",
            charField1 = "", // QRIS_ID menyusul
            charField2 = charFieldString2,
            detailInvoice = detailInvoice
        };

        try
        {
            B_SimpanPembayaran.IsEnabled = false;
            B_SimpanPembayaran.Text = "MENYIMPAN...";

            string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
            string apiUrl = $"{App.API_HOST}penjualan/save-receipt.php";

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);

                string jsonPayload = JsonConvert.SerializeObject(payload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                var response = await client.PostAsync(apiUrl, content);
                string responseString = await response.Content.ReadAsStringAsync();

                // Ambil nomor struk dari respon (untuk preview di halaman Print)
                string receiptNo = ExtractReceiptNumber(responseString);

                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    if (response.IsSuccessStatusCode)
                    {
                        // Lanjut ke pratinjau struk (kirim nominal bayar konsumen untuk struk tunai)
                        await Navigation.PushAsync(new Print(receiptNo, nomor_faktur, _nominalBayarKonsumen));
                    }
                    else
                    {
                        await DisplayAlertAsync("Gagal Menyimpan", $"Sistem merespons: {responseString}", "OK");
                    }
                });
            }
        }
        catch (Exception ex)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
                await DisplayAlertAsync("Error Koneksi", $"Terjadi kesalahan: {ex.Message}", "OK"));
        }
        finally
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                B_SimpanPembayaran.IsEnabled = true;
                B_SimpanPembayaran.Text = "BAYAR";
            });
        }
    }

    private void TapViewDiskon_Tapped(object sender, TappedEventArgs e)
    {
        ViewNominalPembayaran.IsVisible = false;
        ViewDiskon.IsVisible = true;

    }

    private void EntryDiskonNominal_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Cegah infinite-loop saat kita menulis ulang .Text dengan pemisah ribuan
        if (_isFormattingDiskon) return;

        var culture = new CultureInfo("id-ID");

        // Ambil angka murni: buang pemisah ribuan, "Rp", "%", dan spasi
        string raw = (e.NewTextValue ?? "")
            .Replace(".", "")
            .Replace("Rp", "")
            .Replace("%", "")
            .Trim();

        // Kosong → diskon 0
        if (string.IsNullOrEmpty(raw))
        {
            _diskonPembayaran = 0;
            KalkulasiUlangTotal();
            return;
        }

        if (!double.TryParse(raw, out double diskon) || diskon < 0)
            return;

        // Batasi agar tidak melebihi total faktur (LabelGrandTotal / FormtotalAmount awal)
        if (diskon > _totalAmountFaktur)
            diskon = _totalAmountFaktur;

        _diskonPembayaran = diskon;

        // Tampilkan ulang dengan pemisah ribuan
        string formatted = diskon.ToString("N0", culture);
        if (formatted != (e.NewTextValue ?? ""))
        {
            // Gunakan Dispatcher.Dispatch untuk manipulasi elemen UI secara aman
            Dispatcher.Dispatch(() =>
            {
                _isFormattingDiskon = true;
                EntryDiskonNominal.Text = formatted;

                // =========================================================
                // TAMBAHKAN BARIS INI: Obat penangkal crash di Android
                // Memaksa kursor selalu diamankan ke posisi paling akhir teks
                // =========================================================
                EntryDiskonNominal.CursorPosition = formatted.Length;

                _isFormattingDiskon = false;
            });
        }

        // Hitung ulang semua total
        KalkulasiUlangTotal();
    }

    // Hitung ulang Grand Total, Nilai Faktur, Diskon Pembayaran, dan Pembulatan
    private void KalkulasiUlangTotal()
    {
        var culture = new CultureInfo("id-ID");

        double grandTotal = _totalAmountFaktur - _diskonPembayaran;
        if (grandTotal < 0) grandTotal = 0;

        // Diskon pembayaran tampil dinamis
        LabelDiskonPembayaran.Text = $"Rp {_diskonPembayaran.ToString("N0", culture)}";

        // Grand total menyesuaikan diskon.
        // Catatan: FormtotalAmount (Nilai Faktur) sengaja TIDAK diubah —
        // tetap berisi total faktur sebelum diskon (dipakai sebagai paymentAmount saat simpan).
        LabelGrandTotal.Text = $"Rp {grandTotal.ToString("N0", culture)}";

        // Pembulatan ke bawah (kelipatan ratusan)
        double pembulatan = Math.Floor(grandTotal / 100) * 100;
        nilai_pembulatan = pembulatan;
        LabelPembulatan.Text = $"Rp {pembulatan.ToString("N0", culture)}";

        // Selisih pembulatan (mis. 1021 -> 21), tampung & tampilkan
        nilai_selisih_pembulatan = grandTotal - pembulatan;
        TampilkanSelisihPembulatan();

        // Total tagihan akhir = nilai setelah pembulatan; jadi acuan kembalian
        _totalTagihan = pembulatan;
        HitungKembalian();
    }

    // Tampilkan teks selisih pembulatan pada caption, mis. "Pembulatan (- Rp 21)"
    private void TampilkanSelisihPembulatan()
    {
        var culture = new CultureInfo("id-ID");
        if (nilai_selisih_pembulatan > 0)
            LabelSelisihPembulatan.Text = $"Pembulatan (- Rp {nilai_selisih_pembulatan.ToString("N0", culture)})";
        else
            LabelSelisihPembulatan.Text = "Pembulatan";
    }

    // Kembalian = nominal bayar konsumen - total tagihan akhir
    private void HitungKembalian()
    {
        var culture = new CultureInfo("id-ID");

        double kembalian = _nominalBayarKonsumen - _totalTagihan;
        if (kembalian < 0) kembalian = 0; // belum/kurang bayar → tidak ada kembalian

        KembalianKonsumen.Text = $"Rp {kembalian.ToString("N0", culture)}";
    }

    private void NominalBayarKonsumen_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Cegah infinite-loop saat menulis ulang .Text dengan pemisah ribuan
        if (_isFormattingBayar) return;

        var culture = new CultureInfo("id-ID");

        // Ambil angka murni: buang pemisah ribuan, "Rp", "%", dan spasi
        string raw = (e.NewTextValue ?? "")
            .Replace(".", "")
            .Replace("Rp", "")
            .Replace("%", "")
            .Trim();

        // Kosong → nominal 0
        if (string.IsNullOrEmpty(raw))
        {
            _nominalBayarKonsumen = 0;
            HitungKembalian();
            return;
        }

        if (!double.TryParse(raw, out double nominal) || nominal < 0)
            return;

        _nominalBayarKonsumen = nominal;

        // Tampilkan ulang dengan pemisah ribuan; tunda agar Android tidak crash
        string formatted = nominal.ToString("N0", culture);
        if (formatted != (e.NewTextValue ?? ""))
        {
            Dispatcher.Dispatch(() =>
            {
                _isFormattingBayar = true;
                NominalBayarKonsumen.Text = formatted;
                NominalBayarKonsumen.CursorPosition = formatted.Length;
                _isFormattingBayar = false;
            });
        }

        // Hitung ulang kembalian
        HitungKembalian();
    }

    private void TapCloseDiskon_Tapped(object sender, TappedEventArgs e)
    {
        // Nominal Pembayaran & Kembalian hanya untuk Tunai
        ViewNominalPembayaran.IsVisible = paymentMethodVal == "CASH_OTHER";
        ViewDiskon.IsVisible = false;
    }

    private void TapViewKeterangan_Tapped(object sender, TappedEventArgs e)
    {
        ViewKeterangan.IsVisible = false;
        // Nominal Pembayaran & Kembalian hanya untuk Tunai
        ViewNominalPembayaran.IsVisible = paymentMethodVal == "CASH_OTHER";
    }

    private void TapViewKet_Tapped(object sender, TappedEventArgs e)
    {
        ViewKeterangan.IsVisible = true;
        ViewNominalPembayaran.IsVisible = false;
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
}