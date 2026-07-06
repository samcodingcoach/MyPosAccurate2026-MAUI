using Newtonsoft.Json;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using static MyPosAccurate2026.Sales.New_Faktur;
namespace MyPosAccurate2026.Sales;

public partial class ItemAdd : ContentPage
{
    public double ItemBalance { get; set; }
    public double balanceQty { get; set; }
    public string CustomerCode { get; set; }
    public string SelectedSalesNumber { get; private set; }
    public string ItemImagePath { get; set; }
    // TAMBAHKAN INI UNTUK MENGUNCI HARGA ASLI
    private double _originalBasePrice = 0;
    public List<SerialData> AvailableSerialNumbers { get; set; } = new List<SerialData>();
    public ObservableCollection<AddedSerialModel> AddedSerialNumbers { get; set; } = new ObservableCollection<AddedSerialModel>();

    public event EventHandler<CartItemModel> OnItemSaved;
    private int _currentPromoCount = 0;
    public ItemAdd(string itemNo, string name, double balance, string konsumenValue, string imagePath = null, int currentPromoCount = 0)
    {
        InitializeComponent();

        //cek_token();
        _currentPromoCount = currentPromoCount;
        CustomerCode = konsumenValue;
        ItemBalance = balance;
        FormNoItem.Text = itemNo;
        FormNamaBarang.Text = name;
        FormPriceCategory.Text = CustomerCode;
        ItemImagePath = imagePath;

        ListSnContainer.ItemsSource = AddedSerialNumbers;

        UpdateSerialCounter();

        _ = LoadItemStockPrice(itemNo, konsumenValue);
        _ = LoadPromoData(itemNo, konsumenValue);

        _ = LoadSalesData();

        _ = LoadSerialNumber(itemNo);
    }

  
    public class PromoResponse
    {
        public string status { get; set; }
        public string message { get; set; }
        public List<PromoModel> data { get; set; }
    }

    public class PromoModel
    {
        public int id_promo { get; set; }
        public string promo_name { get; set; }
        public string category_user { get; set; }
        public double percentage { get; set; }
        public string item_no { get; set; }

        // Properti ini yang dipanggil oleh ItemDisplayBinding di XAML
        public string DisplayPromo => $"{promo_name} / {percentage}%";
    }

    private async Task LoadPromoData(string itemNo, string customerCode)
    {
        try
        {
            string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
            if (string.IsNullOrEmpty(cleanToken)) return;

            // Tembak API Promo dengan parameter No Item dan Kategori
            string apiUrl = $"{App.API_HOST}promo/listpromo-lokal.php?no={Uri.EscapeDataString(itemNo)}&category={Uri.EscapeDataString(customerCode)}";

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);
                var response = await client.GetAsync(apiUrl);

                if (response.IsSuccessStatusCode)
                {
                    string responseContent = await response.Content.ReadAsStringAsync();
                    var apiResult = JsonConvert.DeserializeObject<PromoResponse>(responseContent);

                    if (apiResult != null && apiResult.status == "success" && apiResult.data != null)
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            PickerPromo.ItemsSource = apiResult.data;
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Gagal memuat Promo: {ex.Message}");
        }
    }

    private async Task LoadItemStockPrice(string itemNo, string customerCode)
    {
        try
        {
            string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();

            if (string.IsNullOrEmpty(cleanToken))
            {
                System.Diagnostics.Debug.WriteLine("Token tidak ditemukan.");
                return;
            }

            // Susun URL API (Gunakan Uri.EscapeDataString untuk menghindari error jika ada spasi pada string)
            string apiUrl = $"{App.API_HOST}item/stokharga.php?no={Uri.EscapeDataString(itemNo)}&priceCategoryName={Uri.EscapeDataString(customerCode)}";

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);

                var response = await client.GetAsync(apiUrl);

                if (response.IsSuccessStatusCode)
                {
                    string responseContent = await response.Content.ReadAsStringAsync();

                    // Deserialize data dari server
                    var apiResult = JsonConvert.DeserializeObject<ItemStockPriceResponse>(responseContent);

                    if (apiResult != null && apiResult.data != null)
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {

                            _originalBasePrice = apiResult.data.unitPrice;
                            FormHargaJual.Text = _originalBasePrice.ToString("N0", new CultureInfo("id-ID"));
                            balanceQty = apiResult.data.availableStock;
                            FormLabelStokAvailable.Text = "Stock tersedia: " + balanceQty;

                            HitungTotalHarga();

                            System.Diagnostics.Debug.WriteLine($"Harga: {apiResult.data.unitPrice}, Stok: {apiResult.data.availableStock}");
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Gagal memuat harga dan stok: {ex.Message}");
        }
    }

    private async Task LoadSerialNumber(string itemNo)
    {
        try
        {
            string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
            if (string.IsNullOrEmpty(cleanToken)) return;

            string apiUrl = $"{App.API_HOST}item/serial_byNo.php?no={Uri.EscapeDataString(itemNo)}";

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);

                var response = await client.GetAsync(apiUrl);
                string responseContent = await response.Content.ReadAsStringAsync();

                // Parsing JSON yang masuk
                var apiResult = JsonConvert.DeserializeObject<SerialResponse>(responseContent);

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (apiResult != null && apiResult.status == "success")
                    {
                        // Akses list melalui apiResult.data.d
                        if (apiResult.data != null && apiResult.data.d != null)
                        {
                            AvailableSerialNumbers = apiResult.data.d;
                            GridNoSeri.IsVisible = true;

                            System.Diagnostics.Debug.WriteLine($"Form SN Dimunculkan. Ada {AvailableSerialNumbers.Count} SN tersedia.");
                        }
                    }
                    else
                    {
                        // Otomatis tereksekusi jika status == "error" 
                        // (Barang yang dicari bukan merupakan barang dengan Serial Number)
                        AvailableSerialNumbers.Clear();
                        GridNoSeri.IsVisible = false;
                        FormSerialCounter.IsVisible = false;
                        System.Diagnostics.Debug.WriteLine($"Form SN Disembunyikan. Pesan: {apiResult?.message}");
                    }
                });
            }
        }
        catch (Exception ex)
        {
            MainThread.BeginInvokeOnMainThread(() => GridNoSeri.IsVisible = false);
            System.Diagnostics.Debug.WriteLine($"CRASH di LoadSerialNumber: {ex.Message}");
        }
    }

    public class ItemStockPriceResponse
    {
        public string status { get; set; }
        public string message { get; set; }

        // Perhatikan ini BUKAN List<T>, melainkan langsung satu objek
        public ItemStockPriceData data { get; set; }
    }

    public class ItemStockPriceData
    {
        public string no { get; set; }
        public string name { get; set; }
        public double unitPrice { get; set; }
        public double availableStock { get; set; }
    }

    private async void cek_token()
    {
        string token = Preferences.Get("TOKEN_KEY", string.Empty);

        // Cek dengan if
        if (!string.IsNullOrEmpty(token))
        {
            System.Diagnostics.Debug.WriteLine($"Token ditemukan: {token}");
        }
        else
        {

            await DisplayAlertAsync("Alert","Token Tidak Ditemukan", "OK");
            System.Diagnostics.Debug.WriteLine("Token tidak ditemukan");

        }

    }

    public class EmployeeResponse
    {
        // Asumsi balasan API Anda menggunakan struktur standar dengan data berupa List
        public string status { get; set; }
        public string message { get; set; }
        public List<EmployeeData> data { get; set; }
    }

    public class EmployeeData
    {
        public string number { get; set; }
        public string name { get; set; }

        // Properti khusus untuk digabungkan dan ditampilkan ke Picker (ItemDisplayBinding)
        public string DisplayName => $"{number} - {name}";
    }

    public class SerialResponse
    {
        public string status { get; set; }
        public string message { get; set; }

        // Sekarang data adalah objek wrapper, bukan list langsung
        public SerialDataWrapper data { get; set; }
    }

    public class SerialDataWrapper
    {
        public bool s { get; set; }
        // Properti 'd' inilah yang menyimpan list array Serial Number-nya
        public List<SerialData> d { get; set; }
    }

    public class SerialData
    {
        public WarehouseData warehouse { get; set; }
        public SerialNumberInfo serialNumber { get; set; }
        public double quantity { get; set; }
    }

    public class WarehouseData
    {
        public int id { get; set; }
        public string name { get; set; }
    }

    public class SerialNumberInfo
    {
        public int id { get; set; }
        public string number { get; set; }
        public string createDate { get; set; }
        public string expiredDate { get; set; }
    }

    public class AddedSerialModel
    {
        public string SerialNumber { get; set; }
        public double Qty { get; set; }
        public string WarehouseName { get; set; }

        public Color RowColor { get; set; }
    }
    private async void BSimpan_Clicked(object sender, EventArgs e)
    {
        // 1. Validasi Qty (Kuantitas)
        if (!int.TryParse(FormQty.Text, out int qty) || qty <= 0)
        {
            await DisplayAlertAsync("Peringatan", "Kuantitas tidak valid.", "OK");
            return;
        }

        // 2. Validasi Serial Number (Jika barang memerlukan SN)
        if (GridNoSeri.IsVisible && AddedSerialNumbers.Count < qty)
        {
            await DisplayAlertAsync("Peringatan", $"Barang ini butuh {qty} Serial Number, Anda baru memasukkan {AddedSerialNumbers.Count}.", "OK");
            return;
        }

        // 3. Ambil Nilai Harga Bersih (Harga Jual setelah diskon jika ada)
        string cleanHarga = FormHargaJual.Text?.Replace("Rp", "")?.Replace(".", "")?.Trim() ?? "0";
        if (!double.TryParse(cleanHarga, out double harga))
        {
            await DisplayAlertAsync("Peringatan", "Harga jual tidak valid.", "OK");
            return;
        }

        // 4. Validasi Harga Tidak Boleh 0
        if (harga <= 0)
        {
            await DisplayAlertAsync("Peringatan", "Barang belum diberi harga jual.", "OK");
            return;
        }

        // 5. Validasi Sales Wajib Dipilih
        if (string.IsNullOrWhiteSpace(SelectedSalesNumber))
        {
            await DisplayAlertAsync("Peringatan", "Sales / Penjual harus dipilih terlebih dahulu.", "OK");
            return;
        }

        // ==========================================================
        // 5.5 AMBIL DATA PERSENTASE DISKON & ID PROMO
        // ==========================================================
        double persenDiskon = 0;
        int idPromoTerpilih = 0;

        // Pastikan checkbox diskon tercentang dan field diskon muncul
        if (FormCheckDiskonItem.IsChecked == true && FormInputDiskonItem.IsVisible)
        {
            // Tangkap ID Promo dari objek yang dipilih di Picker
            if (PickerPromo.SelectedItem is PromoModel selectedPromo)
            {
                idPromoTerpilih = selectedPromo.id_promo;
            }

            // Ambil teks diskon, hilangkan tanda '%' agar menjadi angka murni
            string cleanPercent = FormPersenDiskon.Text?.Replace("%", "")?.Trim() ?? "0";
            if (double.TryParse(cleanPercent, out double parsedPercent))
            {
                persenDiskon = parsedPercent;
            }
        }

        // ==========================================================
        // 6. SUSUN JSON / DATA OBJECT UNTUK KERANJANG
        // ==========================================================
        var cartItem = new CartItemModel
        {
            itemNo = FormNoItem.Text,
            itemName = FormNamaBarang.Text,
            unitPrice = harga,
            quantity = qty,
            warehouseName = FormNamaGudang.Text ?? "Gudang Utama",
            salesmanListNumber = SelectedSalesNumber,
            imagePath = ItemImagePath,

            // Masukkan persentase diskon yang berhasil diekstrak
            itemDiscPercent = persenDiskon,
            id_promo = idPromoTerpilih,

            detailSerialNumber = AddedSerialNumbers.Select(sn => new DetailSerialNumber
            {
                serialNumberNo = sn.SerialNumber,
                quantity = (int)sn.Qty
            }).ToList()
        };

        // 7. TEMBAKKAN DATA KE HALAMAN NEW-FAKTUR
        OnItemSaved?.Invoke(this, cartItem);

        // ==========================================================
        // 8. EKSEKUSI API UPDATE KUOTA PROMO (JIKA MENGGUNAKAN PROMO)
        // ==========================================================
        if (idPromoTerpilih > 0)
        {
            // Kirim ID Promo dan jumlah barang (qty) sebagai pengurang kuota di database
            await UpdatePromoKuotaAsync(idPromoTerpilih, qty);
        }

        // 9. TUTUP HALAMAN ADD ITEM
        await Navigation.PopAsync();
    }
    private void PickerSales_SelectedIndexChanged(object sender, EventArgs e)
    {
        // Tangkap objek dari item yang dipilih
        if (PickerSales.SelectedItem is EmployeeData selectedSales)
        {
            // Simpan 'number' ke dalam variabel global halaman ini
            SelectedSalesNumber = selectedSales.number;

            System.Diagnostics.Debug.WriteLine($"Sales Dipilih: {SelectedSalesNumber} - {selectedSales.name}");
        }
    }
    private async Task LoadSalesData()
    {
        try
        {
            // 1. Ambil token dari Preferences
            string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();

            if (string.IsNullOrEmpty(cleanToken))
            {
                System.Diagnostics.Debug.WriteLine("Token tidak ditemukan.");
                return;
            }

            // 2. Susun URL API untuk mengambil list karyawan yang merupakan sales
            string apiUrl = $"{App.API_HOST}karyawan/list.php?sales=true";

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);

                // 3. Tarik data dari server
                var response = await client.GetAsync(apiUrl);

                if (response.IsSuccessStatusCode)
                {
                    string responseContent = await response.Content.ReadAsStringAsync();

                    // 4. Konversi JSON ke object C#
                    var apiResult = JsonConvert.DeserializeObject<EmployeeResponse>(responseContent);

                    if (apiResult != null && apiResult.data != null)
                    {
                        // 5. Masukkan data ke dalam Picker di Main Thread
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            PickerSales.ItemsSource = apiResult.data;
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Gagal memuat data sales: " + ex.Message);
        }
    }
    private async void BTambahSN_Clicked(object sender, EventArgs e)
    {
        string snInput = FormNomorSerial.Text?.Trim();

        // 1. Validasi Input Kosong
        if (string.IsNullOrEmpty(snInput))
        {
            await DisplayAlertAsync("Peringatan", "Kolom Nomor Serial tidak boleh kosong.", "OK");
            return;
        }

        // 2. Baca batas maksimal dari Qty
        int batasQty = 1;
        if (int.TryParse(FormQty.Text, out int parsedQty))
        {
            batasQty = parsedQty;
        }

        // 3. Validasi Batas Kuantitas
        if (AddedSerialNumbers.Count >= batasQty)
        {
            await DisplayAlertAsync("Peringatan", $"Anda hanya memasukkan Qty {batasQty}. Tidak dapat menambah SN melebihi kuantitas.", "OK");
            return;
        }

        // 4. Cegah Duplikasi Input SN yang sama
        if (AddedSerialNumbers.Any(x => x.SerialNumber.Equals(snInput, StringComparison.OrdinalIgnoreCase)))
        {
            await DisplayAlertAsync("Peringatan", "Nomor Serial ini sudah Anda tambahkan ke daftar.", "OK");
            FormNomorSerial.Text = string.Empty;
            return;
        }

        // 5. Pencocokan dengan Stok di API Accurate
        var matchedSn = AvailableSerialNumbers.FirstOrDefault(x =>
            x.serialNumber != null &&
            x.serialNumber.number.Equals(snInput, StringComparison.OrdinalIgnoreCase));

        if (matchedSn != null)
        {
            Color warnaBaru = (AddedSerialNumbers.Count % 2 == 0) ? Color.FromArgb("#F9F9F9") : Colors.White;
            // Validasi sukses, masukkan ke keranjang SN
            AddedSerialNumbers.Add(new AddedSerialModel
            {
                SerialNumber = matchedSn.serialNumber.number,
                Qty = 1,
                WarehouseName = matchedSn.warehouse?.name ?? "Gudang Default",
                RowColor = warnaBaru


            });

            // Bersihkan kolom input agar siap untuk ketikan SN berikutnya
            FormNomorSerial.Text = string.Empty;
        }
        else
        {
            await DisplayAlertAsync("Gagal", "Nomor Serial tidak ditemukan di sistem atau stok sudah habis.", "OK");
        }
    }
    private void HapusSN_Tapped(object sender, TappedEventArgs e)
    {
        // Menghapus SN jika tulisan "Hapus" diklik
        if (sender is Label label && label.BindingContext is AddedSerialModel snData)
        {
            AddedSerialNumbers.Remove(snData);
        }
    }
    private async void FormQty_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.NewTextValue))
            return;

        string cleanText = new string(e.NewTextValue.Where(char.IsDigit).ToArray());

        if (string.IsNullOrEmpty(cleanText))
        {
            FormQty.Text = string.Empty;
            return;
        }

        if (double.TryParse(cleanText, out double qty))
        {
            if (balanceQty <= 0)
            {
                if (qty != 0) FormQty.Text = "0";
                return;
            }

            if (qty == 0)
            {
                FormQty.Text = "1";
                return;
            }

            if (qty > balanceQty)
            {
                FormQty.Text = balanceQty.ToString();
                Dispatcher.Dispatch(async () =>
                {
                    await DisplayAlertAsync("Stok Terbatas", $"Kuantitas tidak boleh melebihi stok yang tersedia ({balanceQty}).", "OK");
                });
                return;
            }

            // ==========================================================
            // TAMBAHAN VALIDASI: Cegah Qty lebih kecil dari SN yang sudah diinput
            // ==========================================================
            if (qty < AddedSerialNumbers.Count)
            {
                FormQty.Text = AddedSerialNumbers.Count.ToString();
                Dispatcher.Dispatch(async () =>
                {
                    await DisplayAlertAsync("Peringatan", $"Kuantitas tidak boleh kurang dari jumlah Nomor Serial yang sudah dimasukkan ({AddedSerialNumbers.Count}). Hapus SN terlebih dahulu.", "OK");
                });
                return;
            }

            if (FormQty.Text != cleanText)
            {
                FormQty.Text = cleanText;
            }

            // TAMBAHKAN INI: Update label counter jika Qty berubah
            UpdateSerialCounter();

            HitungTotalHarga();
        }
    }
    private void UpdateSerialCounter()
    {
        int batasQty = 1;
        if (int.TryParse(FormQty.Text, out int parsedQty))
        {
            batasQty = parsedQty;
        }

        // Hitung sisa kebutuhan SN
        int sisa = batasQty - AddedSerialNumbers.Count;

        // Pastikan angka tidak negatif
        if (sisa < 0) sisa = 0;

        // Update teks ke Label UI
        FormSerialCounter.Text = $"Butuh serial: {sisa}";
    }
    private void HitungTotalHarga()
    {
        // 1. Ambil nilai Qty (Default 1 jika kosong/error)
        double qty = 1;
        if (double.TryParse(FormQty.Text, out double parsedQty))
        {
            qty = parsedQty;
        }

        // 2. Ambil nilai Harga Jual dan bersihkan dari titik atau tulisan "Rp"
        double hargaJual = 0;
        string cleanHarga = FormHargaJual.Text?.Replace("Rp", "")?.Replace(".", "")?.Trim() ?? "0";

        if (double.TryParse(cleanHarga, out double parsedHarga))
        {
            hargaJual = parsedHarga;
        }

        // 3. Kalikan
        double totalHarga = qty * hargaJual;

        // 4. Tampilkan ke Label dengan format pemisah ribuan ala Indonesia
        FormTotalHarga.Text = $"Rp {totalHarga.ToString("N0", new CultureInfo("id-ID"))}";
    }

    private async void FormCheckDiskonItem_CheckedChanged(object sender, CheckedChangedEventArgs e)
    {
        if (FormCheckDiskonItem.IsChecked == true)
        {
            FormInputDiskonItem.IsVisible = true;
        }
        else
        {
            FormInputDiskonItem.IsVisible = false;

            // RESET SEMUA KEMBALI KE HARGA NORMAL
            PickerPromo.SelectedItem = null;
            FormPersenDiskon.Text = "0%";
            FormHargaJual.Text = _originalBasePrice.ToString("N0", new CultureInfo("id-ID"));

            // Hitung ulang total dengan harga normal
            HitungTotalHarga();
        }
    }
    private void PickerPromo_SelectedIndexChanged(object sender, EventArgs e)
    {
        // 1. PENGAMAN: Blokir jika kuota 3 promo di keranjang sudah penuh
        if (PickerPromo.SelectedItem != null && _currentPromoCount >= 3)
        {
            Dispatcher.Dispatch(async () =>
            {
                await DisplayAlertAsync("Peringatan", "Batas maksimal 3 promo per faktur telah tercapai. Anda tidak dapat menggunakan promo lagi pada barang ini.", "OK");
            });
            PickerPromo.SelectedItem = null;
            return;
        }

        // 2. Lanjutkan diskon jika kuota aman
        if (PickerPromo.SelectedItem is PromoModel selectedPromo)
        {
            FormPersenDiskon.Text = $"{selectedPromo.percentage}%";

            double nilaiPotongan = _originalBasePrice * (selectedPromo.percentage / 100);
            double hargaSetelahDiskon = _originalBasePrice - nilaiPotongan;

            FormHargaJual.Text = hargaSetelahDiskon.ToString("N0", new CultureInfo("id-ID"));

            HitungTotalHarga();
        }
    }

    // =========================================================
    // FUNGSI UPDATE KUOTA PROMO KE DATABASE
    // =========================================================
    private async Task UpdatePromoKuotaAsync(int idPromo, int usedKuota)
    {
        try
        {
            string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
            string apiUrl = $"{App.API_HOST}promo/update-kuota.php";

            // Susun body/payload JSON sesuai format yang Anda minta
            var payload = new
            {
                kuota = usedKuota,
                id_promo = idPromo
            };

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);
                string jsonPayload = JsonConvert.SerializeObject(payload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                // Tembakkan POST API
                var response = await client.PostAsync(apiUrl, content);
                string responseString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"API Promo Error: {responseString}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Sukses memotong kuota Promo ID: {idPromo}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Koneksi Update Promo Gagal: {ex.Message}");
        }
    }
}