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

public partial class New_Faktur : ContentPage
{
    // Variabel penanda jika masuk mode edit faktur
    private int _editInvoiceId = 0;
    private string _editInvoiceNumber = "";
    public string? SelectedKonsumenValue { get; private set; }

    private bool _isFormattingDiskonNominal = false;
    public record KonsumenOption(string Text, string Value);

    public ObservableCollection<ItemModel> AutoCompleteResults { get; set; } = new ObservableCollection<ItemModel>();
    public ObservableCollection<SelectedBiayaModel> SelectedBiayaList { get; set; } = new ObservableCollection<SelectedBiayaModel>();

    public ObservableCollection<CartItemModel> CartItems { get; set; } = new ObservableCollection<CartItemModel>();

    // 1. Deklarasikan HttpClient tanpa langsung mengisi string URL di sini
    private readonly HttpClient _httpClient;

    private bool _isFormattingBiaya = false;

    private List<CartItemModel> _deletedCartItems = new List<CartItemModel>();
    private List<SelectedBiayaModel> _deletedBiayaList = new List<SelectedBiayaModel>();

    public New_Faktur()
	{
		InitializeComponent();
        //cek_token();
        _httpClient = new HttpClient { BaseAddress = new Uri(App.API_HOST) };

        var listKonsumen = new List<KonsumenOption>
        {
            new("Free - MB003", "Free"),
            new("Shopee - C.00001", "Shopee"),
            new("Membership - MB002", "Membership"),
            new("Non Member - MB001", "Umum")
        };

        // 2. Bind ke Picker
        PickerKonsumen.ItemsSource = listKonsumen;

        List_AutoComplete.ItemsSource = AutoCompleteResults;

       
        BindableLayout.SetItemsSource(ListBiayaContainer, SelectedBiayaList);

        // Tambahkan di dalam constructor New_Faktur()
        //BindableLayout.SetItemsSource(CartContainer, CartItems);

        CartContainer.ItemsSource = CartItems;
        _ = LoadCoaData();
    }

    private async void cek_token()
    {
        string token = Preferences.Get("TOKEN_KEY", string.Empty);

       

        // Cek dengan if
        if (!string.IsNullOrEmpty(token))
        {

            await DisplayAlertAsync("TES",token,"OK");
            System.Diagnostics.Debug.WriteLine($"Token ditemukan: {token}");
          
        }
        else
        {
           
            System.Diagnostics.Debug.WriteLine("Token tidak ditemukan");
            
        }

    }

    public class ApiResponse
    {
        public string status { get; set; }
        public string message { get; set; }
        public List<ItemModel> data { get; set; }
    }

    public class ItemModel
    {
        public string item_no { get; set; }
        public string name { get; set; }
        public double balance { get; set; }

        public double price { get; set; }
        public string image { get; set; }


        public Color StockColor => balance > 0 ? Color.FromArgb("#006400") : Color.FromArgb("#FF0000");
    }

    public class CoaResponse
    {
        public string status { get; set; }
        public string message { get; set; }
        public List<CoaData> data { get; set; }
    }

    public class CoaData
    {
        public string no { get; set; }
        public string name { get; set; }
        public int id { get; set; }
        public string DisplayName => $"{no} - {name}";
    }

    public class SelectedBiayaModel
    {
        public string No { get; set; }
        public string Name { get; set; }
        public double Nominal { get; set; }
        public string FormattedNominal => $"Rp {Nominal:N0}";
        public int? id { get; set; }
    }


    

    // Tampilkan alamat & tombol Maps hanya jika pengiriman "Kurir Toko".
    private void PickerPengirim_SelectedIndexChanged(object sender, EventArgs e)
    {
        bool kurirToko = string.Equals(PickerPengirim.SelectedItem as string, "Kurir Toko", StringComparison.OrdinalIgnoreCase);
        BorderAlamat.IsVisible = kurirToko;
        B_CariAlamatMaps.IsVisible = kurirToko;
    }

    // Buka halaman Maps untuk memilih alamat; hasil dimuat ke EntryAlamat.
    // Format: dari GPS -> "[GPS:lat,lng] alamat", selain itu -> "alamat".
    private async void B_CariAlamatMaps_Clicked(object sender, EventArgs e)
    {
        var maps = new Maps();
        maps.LokasiDipilih += (s, ev) =>
        {
            // Lokasi dari Maps selalu punya koordinat -> sertakan prefix [GPS:lat,lng].
            string lat = ev.Latitude.ToString("0.######", CultureInfo.InvariantCulture);
            string lng = ev.Longitude.ToString("0.######", CultureInfo.InvariantCulture);
            string alamat = $"[GPS:{lat},{lng}] {ev.Alamat}";

            MainThread.BeginInvokeOnMainThread(() => EntryAlamat.Text = alamat);
        };

        await Navigation.PushAsync(maps);
    }

    // Fungsi pemeta data dari API ke Element UI
    public void LoadEditData(List_Faktur.DetailInvoiceData data)
    {
        _editInvoiceId = data.id;
        _editInvoiceNumber = data.number;

       
        _deletedCartItems.Clear();
        _deletedBiayaList.Clear();

        B_NewFaktur.Text = "UPDATE FAKTUR";

        EntryNoPO.Text = data.poNumber;
        EntryKeterangan.Text = data.description;
        EntryAlamat.Text = data.toAddress;

        if (data.cashDiscount > 0) EntryDiskonNominal.Text = data.cashDiscount.ToString();

        CheckBoxPPN.IsChecked = data.tax1Amount > 0;

        if (data.shipment != null && !string.IsNullOrEmpty(data.shipment.name))
            PickerPengirim.SelectedItem = data.shipment.name;

        if (data.customer != null && !string.IsNullOrEmpty(data.customer.customerNo))
        {
            foreach (KonsumenOption opt in PickerKonsumen.ItemsSource)
            {
                if (opt.Text.Contains($"- {data.customer.customerNo}"))
                {
                    PickerKonsumen.SelectedItem = opt;
                    break;
                }
            }
        }

        SelectedBiayaList.Clear();
        if (data.detailExpense != null)
        {
            foreach (var exp in data.detailExpense)
            {
                SelectedBiayaList.Add(new SelectedBiayaModel
                {
                    id = exp.id,
                    No = exp.account?.no,
                    Name = exp.detailName,
                    Nominal = exp.expenseAmount
                });
            }
        }

        CartItems.Clear();
        if (data.detailItem != null)
        {
            for (int i = 0; i < data.detailItem.Count; i++)
            {
                var itm = data.detailItem[i];
                string currentItemNo = itm.item?.no ?? "N/A";

                int currentIdPromo = 0;
                if (i == 0) currentIdPromo = data.numericField1;
                else if (i == 1) currentIdPromo = data.numericField2;
                else if (i == 2) currentIdPromo = data.numericField3;

                var cartItem = new CartItemModel
                {
                    id = itm.id,
                    itemNo = currentItemNo,
                    id_promo = currentIdPromo,
                    itemName = itm.detailName,
                    unitPrice = itm.unitPrice,
                    quantity = (int)itm.quantity,
                    itemDiscPercent = itm.itemDiscPercent ?? 0,
                    warehouseName = itm.warehouse?.name ?? "Gudang Utama",
                    salesmanListNumber = itm.salesmanList?.FirstOrDefault()?.number,
                    imagePath = currentItemNo != "N/A" ? $"{currentItemNo}.jpg" : ""
                };

                if (itm.detailSerialNumber != null && itm.detailSerialNumber.Count > 0)
                {
                    cartItem.detailSerialNumber = itm.detailSerialNumber.Select(sn => new DetailSerialNumber
                    {
                        id = sn.id,
                        serialNumberNo = sn.serialNumber?.number,
                        quantity = sn.quantity
                    }).ToList();
                }

                CartItems.Add(cartItem);
            }
        }

        KalkulasiSemuaTotal();
    }

    // =========================================================
    // MODEL KERANJANG BELANJA (CART ITEM)
    // =========================================================
    public class CartItemModel
    {
        public string itemNo { get; set; }
        public int? id { get; set; } 
        public int id_promo { get; set; }
        public string itemName { get; set; }
        public double unitPrice { get; set; }
        public int quantity { get; set; }
        public string warehouseName { get; set; }
        public string salesmanListNumber { get; set; }
        public string imagePath { get; set; }
        public double itemDiscPercent { get; set; } // Pastikan properti diskon ini ada dari tahap sebelumnya

        public string DisplayImage
        {
            get
            {
                if (string.IsNullOrWhiteSpace(imagePath)) return "nophotoproduct150.jpg";
                if (imagePath.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return imagePath;
                string baseHost = App.API_HOST.Replace("api/", "");
                string cleanFileName = imagePath.Replace("../", "").Replace("images/", "").TrimStart('/');
                return $"{baseHost}images/{cleanFileName}";
            }
        }

        public List<DetailSerialNumber> detailSerialNumber { get; set; }

        // 1. Hitung nilai asli (Harga x Qty) dikurangi persentase diskon
        public double TotalPriceAfterDiscount => (unitPrice * quantity) - ((unitPrice * quantity) * (itemDiscPercent / 100.0));

        // 2. Format ke rupiah berdasarkan nilai yang sudah didiskon
        public string FormattedTotalPrice => $"Rp {TotalPriceAfterDiscount.ToString("N0", new CultureInfo("id-ID"))}";

        public string FormattedUnitPrice => $"Rp {unitPrice.ToString("N0", new CultureInfo("id-ID"))}";
        public string DisplayQty => $"x {quantity}";
        public bool HasSerialNumbers => detailSerialNumber != null && detailSerialNumber.Count > 0;
        public string SerialNumbersDisplay => HasSerialNumbers ? "SN: " + string.Join(", ", detailSerialNumber.Select(x => x.serialNumberNo)) : "";

        public string ItemInfoDisplay => $"{itemNo} | {warehouseName} | Sales: {salesmanListNumber}";
        public string PriceAndQtyDisplay => $"{FormattedUnitPrice} {DisplayQty}";
        public string TotalHargaLabel => itemDiscPercent > 0 ? $"Total Harga - Diskon {itemDiscPercent}% :" : "Total Harga :";
    }

    public class DetailSerialNumber
    {
        public string serialNumberNo { get; set; }
        public int quantity { get; set; }
        public int? id { get; set; } // <--- TAMBAHAN
    }

    private async void SearchBar_Item_TextChanged(object sender, TextChangedEventArgs e)
    {
        string keyword = e.NewTextValue;

        if (string.IsNullOrWhiteSpace(keyword) || keyword.Length < 2)
        {
            Border_AutoComplete.IsVisible = false;
            AutoCompleteResults.Clear();
            return;
        }

        await Task.Delay(400); // Debounce

        // Pastikan teks tidak berubah lagi saat kita menunggu
        if (keyword != SearchBar_Item.Text) return;

        await FetchItemsFromApi(keyword);
    }

    private async Task FetchItemsFromApi(string keyword)
    {
        try
        {
            string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();

            // Bangun URL persis seperti yang Anda tes di browser
            string apiUrl = $"{App.API_HOST}item/list-lokal.php?search={Uri.EscapeDataString(keyword)}&limit=10";

            using (var client = new HttpClient())
            {
                if (!string.IsNullOrEmpty(cleanToken))
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);

                // Bypass blokir keamanan hosting
                client.DefaultRequestHeaders.Add("User-Agent", "AccuratePOS-App/1.0");

                var response = await client.GetAsync(apiUrl);
                string responseContent = await response.Content.ReadAsStringAsync();

                // -------------------------------------------------------------
                // BUKA KOMENTAR INI JIKA MASIH KOSONG UNTUK MELIHAT RAW JSON DI HP
                // await DisplayAlert("JSON DITERIMA", responseContent, "OK");
                // -------------------------------------------------------------

                if (!response.IsSuccessStatusCode)
                {
                    MainThread.BeginInvokeOnMainThread(() => Border_AutoComplete.IsVisible = false);
                    return;
                }

                var apiResult = JsonConvert.DeserializeObject<ApiResponse>(responseContent);

                // Pastikan UI diupdate di Main Thread agar CollectionView kerender
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    AutoCompleteResults.Clear();

                    if (apiResult != null && apiResult.status == "success")
                    {
                        if (apiResult.data != null && apiResult.data.Count > 0)
                        {
                            foreach (var item in apiResult.data)
                            {
                                AutoCompleteResults.Add(item);
                            }
                        }
                        // Tetap true, jika Count > 0 maka data muncul. 
                        // Jika Count 0 maka EmptyView "Tidak ada barang" muncul.
                        Border_AutoComplete.IsVisible = true;
                    }
                });
            }
        }
        catch (Exception ex)
        {
            MainThread.BeginInvokeOnMainThread(() => Border_AutoComplete.IsVisible = false);
            System.Diagnostics.Debug.WriteLine("Fetch API Error: " + ex.Message);
        }
    }

    private async void List_AutoComplete_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is ItemModel selectedItem)
        {
            // 1. Validasi Stok Tidak Boleh 0
            if (selectedItem.balance <= 0)
            {
                List_AutoComplete.SelectedItem = null;
                await DisplayAlertAsync("Stok Habis", "Barang ini tidak dapat dipilih karena stok kosong (0).", "OK");
                return;
            }

            // 2. Validasi Harga Tidak Boleh 0
            if (selectedItem.price <= 0)
            {
                List_AutoComplete.SelectedItem = null;
                await DisplayAlertAsync("Harga Tidak Valid", "Barang ini belum memiliki harga jual (Rp 0). Silakan perbarui harga master barang terlebih dahulu.", "OK");
                return;
            }

            // 3. Validasi Konsumen (Harus dipilih dulu)
            if (string.IsNullOrWhiteSpace(SelectedKonsumenValue))
            {
                List_AutoComplete.SelectedItem = null;
                Border_AutoComplete.IsVisible = false;

                await DisplayAlertAsync("Peringatan", "Silakan pilih Konsumen / Pelanggan terlebih dahulu!", "OK");
                PickerKonsumen.Focus();
                return;
            }

            // 4. Validasi Barang Sudah Ada di Keranjang
            // Jika barang sudah termuat di CollectionView, jangan tambah baris baru.
            // Tawarkan untuk mengubah kuantitasnya saja.
            var existingItem = CartItems.FirstOrDefault(x => x.itemNo == selectedItem.item_no);
            if (existingItem != null)
            {
                List_AutoComplete.SelectedItem = null;
                Border_AutoComplete.IsVisible = false;
                SearchBar_Item.Text = string.Empty;

                await EditKuantitasBarangAsync(existingItem);
                return;
            }

            // Jika semua lolos validasi, proses navigasi
            SearchBar_Item.Text = selectedItem.item_no;
            Border_AutoComplete.IsVisible = false;

            System.Diagnostics.Debug.WriteLine($"Barang Dipilih: {selectedItem.name} - Harga: {selectedItem.price}");

            // 1. Hitung promo saat ini
            int currentPromoCount = CartItems.Count(x => x.id_promo > 0);

            // 2. Buka halaman ItemAdd dengan menyisipkan currentPromoCount di paling belakang
            var itemAddPage = new ItemAdd(selectedItem.item_no, selectedItem.name, selectedItem.balance, SelectedKonsumenValue, selectedItem.image, currentPromoCount);
            
            // Tangkap Data yang dikirim dari BSimpan_Clicked
            itemAddPage.OnItemSaved += (s, cartItem) =>
            {
                CartItems.Insert(0, cartItem);
                KalkulasiSemuaTotal();

            };

            await Navigation.PushAsync(itemAddPage);

            List_AutoComplete.SelectedItem = null;
        }
    }

    // =========================================================
    // EDIT KUANTITAS BARANG YANG SUDAH ADA DI KERANJANG
    // Dipanggil saat user memilih barang yang sudah termuat di CollectionView.
    // =========================================================
    private async Task EditKuantitasBarangAsync(CartItemModel existingItem)
    {
        // Barang dengan Serial Number tidak bisa diubah qty-nya langsung,
        // karena jumlah SN harus sama persis dengan qty. Arahkan untuk hapus & tambah ulang.
        if (existingItem.HasSerialNumbers)
        {
            await DisplayAlertAsync(
                "Barang Sudah Ada",
                $"\"{existingItem.itemName}\" sudah ada di keranjang dan menggunakan Nomor Serial. " +
                "Untuk mengubah kuantitas, silakan hapus barang ini lalu tambahkan kembali.",
                "OK");
            return;
        }

        bool wantEdit = await DisplayAlertAsync(
            "Barang Sudah Ada",
            $"\"{existingItem.itemName}\" sudah ada di keranjang dengan kuantitas {existingItem.quantity}. " +
            "Apakah Anda ingin mengubah kuantitasnya?",
            "Ya, Ubah", "Batal");

        if (!wantEdit) return;

        // Ambil stok ONLINE terkini dari server (bukan stok lokal hasil pencarian)
        double stokTersedia = await FetchOnlineStockAsync(existingItem.itemNo);
        if (stokTersedia < 0)
        {
            await DisplayAlertAsync("Gagal", "Tidak dapat memuat stok online barang ini. Coba lagi.", "OK");
            return;
        }

        string input = await DisplayPromptAsync(
            "Ubah Kuantitas",
            $"Masukkan kuantitas baru (Stok tersedia: {stokTersedia:N0}):",
            "Simpan", "Batal",
            initialValue: existingItem.quantity.ToString(),
            keyboard: Keyboard.Numeric);

        // User menekan Batal
        if (input == null) return;

        string cleanInput = new string(input.Where(char.IsDigit).ToArray());
        if (!int.TryParse(cleanInput, out int qtyBaru) || qtyBaru <= 0)
        {
            await DisplayAlertAsync("Peringatan", "Kuantitas tidak valid. Masukkan angka lebih besar dari 0.", "OK");
            return;
        }

        // Cegah qty melebihi stok yang tersedia
        if (qtyBaru > stokTersedia)
        {
            await DisplayAlertAsync(
                "Stok Terbatas",
                $"Kuantitas tidak boleh melebihi stok yang tersedia ({stokTersedia:N0}).",
                "OK");
            return;
        }

        int qtyLama = existingItem.quantity;
        int selisih = qtyBaru - qtyLama;

        // Jika tidak ada perubahan, tidak perlu lakukan apa-apa
        if (selisih == 0) return;

        // Jika barang memakai promo, sesuaikan kuota di database:
        // - penambahan qty => potong kuota tambahan
        // - pengurangan qty => kembalikan kuota
        if (existingItem.id_promo > 0)
        {
            if (selisih > 0)
                await UpdatePromoKuotaAsync(existingItem.id_promo, selisih);
            else
                await CancelPromoKuotaAsync(existingItem.id_promo, -selisih);
        }

        // Terapkan kuantitas baru. CartItemModel bukan INotifyPropertyChanged,
        // jadi refresh baris dengan remove + insert di posisi yang sama.
        int index = CartItems.IndexOf(existingItem);
        existingItem.quantity = qtyBaru;
        if (index >= 0)
        {
            CartItems.RemoveAt(index);
            CartItems.Insert(index, existingItem);
        }

        KalkulasiSemuaTotal();
    }

    public class StockResponse
    {
        public string status { get; set; }
        public string message { get; set; }
        public StockData data { get; set; }
    }

    public class StockData
    {
        public double availableStock { get; set; }
    }

    // =========================================================
    // AMBIL STOK ONLINE TERKINI DARI SERVER (item/stock.php)
    // Mengembalikan -1 jika gagal memuat.
    // =========================================================
    private async Task<double> FetchOnlineStockAsync(string itemNo)
    {
        try
        {
            string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
            string apiUrl = $"{App.API_HOST}item/stock.php?no={Uri.EscapeDataString(itemNo)}";

            using (var client = new HttpClient())
            {
                if (!string.IsNullOrEmpty(cleanToken))
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);

                var response = await client.GetAsync(apiUrl);
                if (!response.IsSuccessStatusCode) return -1;

                string responseContent = await response.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(responseContent) || responseContent.TrimStart().StartsWith("<"))
                    return -1;

                var apiResult = JsonConvert.DeserializeObject<StockResponse>(responseContent);
                if (apiResult?.data == null) return -1;

                return apiResult.data.availableStock;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Gagal memuat stok online: {ex.Message}");
            return -1;
        }
    }

    // =========================================================
    // FUNGSI API UNTUK MEMOTONG KUOTA PROMO (PENAMBAHAN QTY)
    // =========================================================
    private async Task UpdatePromoKuotaAsync(int idPromo, int usedKuota)
    {
        try
        {
            string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
            string apiUrl = $"{App.API_HOST}promo/update-kuota.php";

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

                var response = await client.PostAsync(apiUrl, content);

                if (!response.IsSuccessStatusCode)
                    System.Diagnostics.Debug.WriteLine($"Gagal memotong Kuota Promo ID: {idPromo}");
                else
                    System.Diagnostics.Debug.WriteLine($"Sukses memotong Kuota Promo ID: {idPromo} sejumlah {usedKuota}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Koneksi Update Promo Gagal: {ex.Message}");
        }
    }

    private void B_ShipmentTapGesture_Tapped(object sender, TappedEventArgs e)
    {
        ViewBarang.IsVisible = false;
        ViewDiskon.IsVisible = false;
        ViewPengirim.IsVisible = true;
        ViewBiaya.IsVisible = false;
        BClose.IsVisible = true;
    }

    private void B_BiayaTapGesture_Tapped(object sender, TappedEventArgs e)
    {
        ViewBarang.IsVisible = false;
        ViewDiskon.IsVisible = false;
        ViewPengirim.IsVisible = false;
        ViewBiaya.IsVisible = true;
        BClose.IsVisible =true;
    }

    private async void B_NewFaktur_Clicked(object sender, EventArgs e)
    {
        if (CartItems.Count == 0)
        {
            await DisplayAlertAsync("Peringatan", "Keranjang belanja masih kosong. Tambahkan barang terlebih dahulu.", "OK");
            return;
        }

        string finalCustomerCode = "";
        if (PickerKonsumen.SelectedItem is KonsumenOption selectedOption)
        {
            string teksYangTampil = selectedOption.Text;
            if (teksYangTampil.Contains("- "))
            {
                var splitArray = teksYangTampil.Split(new string[] { "- " }, StringSplitOptions.None);
                finalCustomerCode = splitArray.Last().Trim();
            }
            else finalCustomerCode = teksYangTampil.Trim();
        }
        else
        {
            await DisplayAlertAsync("Peringatan", "Silakan pilih Konsumen / Pelanggan terlebih dahulu.", "OK");
            return;
        }

        string cleanDiskon = EntryTotalDiskon.Text?.Replace("Rp", "")?.Replace(".", "")?.Trim() ?? "0";
        if (!double.TryParse(cleanDiskon, out double totalDiskon)) totalDiskon = 0;

        // CARA CERDAS MENGAMBIL ID PROMO
        var promoItems = CartItems.Where(x => x.id_promo > 0).ToList();

        if (promoItems.Count > 3)
        {
            await DisplayAlertAsync("Peringatan", "Maksimal hanya 3 promo yang diizinkan dalam 1 faktur.", "OK");
            return;
        }

        int num1 = promoItems.Count > 0 ? promoItems[0].id_promo : 0;
        int num2 = promoItems.Count > 1 ? promoItems[1].id_promo : 0;
        int num3 = promoItems.Count > 2 ? promoItems[2].id_promo : 0;

        string idUser = Preferences.Get("ID_USER", "");
        string userName = Preferences.Get("USERNAME", "");
        string charFieldString = $"{idUser} - {userName}";

        // =========================================================
        // SUSUN LIST DETAIL ITEM (AKTIF + HAPUS)
        // =========================================================
        var finalDetailItems = new List<object>();

        // 1. Masukkan semua barang yang aktif di layar
        finalDetailItems.AddRange(CartItems.Select(item => new
        {
            id = item.id > 0 ? (int?)item.id : null,
            itemNo = item.itemNo,
            unitPrice = item.unitPrice,
            quantity = item.quantity,
            warehouseName = item.warehouseName,
            salesmanListNumber = item.salesmanListNumber,
            itemDiscPercent = item.itemDiscPercent,
            detailSerialNumber = item.detailSerialNumber?.Select(sn => new
            {
                id = sn.id > 0 ? (int?)sn.id : null,
                serialNumberNo = sn.serialNumberNo,
                quantity = sn.quantity
            }).ToList()
        }));

        // 2. Masukkan barang yang dihapus (KHUSUS MODE EDIT)
        if (_editInvoiceId > 0 && _deletedCartItems.Count > 0)
        {
            finalDetailItems.AddRange(_deletedCartItems.Select(item => new
            {
                id = item.id,
                itemNo = item.itemNo,
                unitPrice = item.unitPrice,
                _status = "delete" // Flag hapus untuk Accurate
            }));
        }

        // =========================================================
        // SUSUN LIST DETAIL BIAYA (AKTIF + HAPUS)
        // =========================================================
        var finalDetailExpenses = new List<object>();

        // 1. Masukkan semua biaya yang aktif di layar
        finalDetailExpenses.AddRange(SelectedBiayaList.Select(biaya => new
        {
            id = biaya.id > 0 ? (int?)biaya.id : null,
            accountNo = biaya.No,
            expenseAmount = biaya.Nominal
        }));

        // 2. Masukkan biaya yang dihapus (KHUSUS MODE EDIT)
        if (_editInvoiceId > 0 && _deletedBiayaList.Count > 0)
        {
            finalDetailExpenses.AddRange(_deletedBiayaList.Select(biaya => new
            {
                id = biaya.id,
                accountNo = biaya.No,
                expenseAmount = biaya.Nominal,
                _status = "delete" // Flag hapus untuk Accurate
            }));
        }

        // =========================================================
        // SUSUN PAYLOAD AKHIR
        // =========================================================
        var payload = new
        {
            id = _editInvoiceId > 0 ? (int?)_editInvoiceId : null,
            number = !string.IsNullOrEmpty(_editInvoiceNumber) ? _editInvoiceNumber : null,
            customerNo = finalCustomerCode,
            transDate = DateTime.Now.ToString("yyyy-MM-dd"),
            cashDiscount = totalDiskon,
            taxable = CheckBoxPPN.IsChecked == true,
            shipmentName = PickerPengirim.SelectedItem?.ToString() ?? "",
            toAddress = EntryAlamat.Text ?? "",
            description = EntryKeterangan.Text ?? "",
            poNumber = EntryNoPO.Text ?? "",
            numericField1 = num1,
            numericField2 = num2,
            numericField3 = num3,
            charField1 = charFieldString,

            detailItem = finalDetailItems,      // Masukkan gabungan item
            detailExpense = finalDetailExpenses // Masukkan gabungan biaya
        };

        try
        {
            B_NewFaktur.IsEnabled = false;
            B_NewFaktur.Text = "MENYIMPAN...";

            string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
            string apiUrl = $"{App.API_HOST}penjualan/save-invoice.php";

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);

                string jsonPayload = JsonConvert.SerializeObject(payload, new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore
                });

                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                // Mulai tembak API (Masuk Background Thread)
                var response = await client.PostAsync(apiUrl, content);
                string responseString = await response.Content.ReadAsStringAsync();

                // KEMBALI KE UI THREAD UNTUK ALERT & NAVIGASI
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    if (response.IsSuccessStatusCode)
                    {
                        await DisplayAlertAsync("Sukses", "Faktur Penjualan berhasil disimpan ke sistem.", "OK");
                        await Navigation.PopAsync();
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
                B_NewFaktur.IsEnabled = true;
                B_NewFaktur.Text = _editInvoiceId > 0 ? "UPDATE FAKTUR" : "BUAT FAKTUR";
            });
        }
    }

    private void BClose_Clicked(object sender, EventArgs e)
    {
        ViewBarang.IsVisible = true;
        ViewDiskon.IsVisible = false;
        ViewPengirim.IsVisible = false;
        ViewBiaya.IsVisible = false;
        BClose.IsVisible = false;

    }

    private void B_Diskon_Tapped(object sender, TappedEventArgs e)
    {
        ViewBarang.IsVisible = false;
        ViewDiskon.IsVisible = true;
        ViewPengirim.IsVisible = false;
        ViewBiaya.IsVisible = false;
        BClose.IsVisible = true;
    }

    private void OnKonsumenSelected(object sender, EventArgs e)
    {
        var picker = sender as Picker;

        // Cek apakah ada yang dipilih (hindari null saat user batal pilih)
        if (picker?.SelectedItem is KonsumenOption selected)
        {
            SelectedKonsumenValue = selected.Value;

            // 1. Kunci Picker agar tidak bisa diklik/diubah secara langsung lagi
            PickerKonsumen.IsEnabled = false;

            // 2. Sembunyikan ikon pencarian, munculkan ikon cancel (silang merah)
            ImageSearchKonsumen.IsVisible = false;
            ImageCancelKonsumen.IsVisible = true;
        }
    }

    private async Task LoadCoaData()
    {
        try
        {
            string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
            if (string.IsNullOrEmpty(cleanToken)) return;

            string apiUrl = $"{App.API_HOST}coa/list.php";

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);
                var response = await client.GetAsync(apiUrl);

                if (response.IsSuccessStatusCode)
                {
                    string responseContent = await response.Content.ReadAsStringAsync();
                    var apiResult = JsonConvert.DeserializeObject<CoaResponse>(responseContent);

                    if (apiResult != null && apiResult.status == "success" && apiResult.data != null)
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            PickerBiaya.ItemsSource = apiResult.data;
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Gagal memuat COA: {ex.Message}");
        }
    }

    private void EntryHargaBiaya_TextChanged(object sender, TextChangedEventArgs e)
    {
        // 1. Jika sistem sedang memformat teks, hentikan proses untuk mencegah infinite loop
        if (_isFormattingBiaya) return;

        if (string.IsNullOrWhiteSpace(e.NewTextValue))
            return;

        // 2. Buang semua karakter yang BUKAN angka
        string cleanText = new string(e.NewTextValue.Where(char.IsDigit).ToArray());

        if (string.IsNullOrEmpty(cleanText))
        {
            _isFormattingBiaya = true;
            EntryHargaBiaya.Text = string.Empty;
            _isFormattingBiaya = false;
            return;
        }

        // 3. Ubah ke format angka dan titik ribuan
        if (long.TryParse(cleanText, out long value))
        {
            string formatted = value.ToString("N0", new CultureInfo("id-ID"));

            if (EntryHargaBiaya.Text != formatted)
            {
                _isFormattingBiaya = true;

                // 4. BUNGKUS DENGAN DISPATCHER (Solusi Anti-Crash Android)
                Dispatcher.Dispatch(() =>
                {
                    EntryHargaBiaya.Text = formatted;

                    // Pindahkan posisi kursor ke paling kanan agar tidak kembali ke kiri
                    EntryHargaBiaya.CursorPosition = formatted.Length;

                    _isFormattingBiaya = false;
                });
            }
        }
    }

    private async void BTambahBiaya_Clicked(object sender, EventArgs e)
    {
        if (PickerBiaya.SelectedItem is CoaData selectedCoa)
        {
            // HILANGKAN TITIK SEBELUM DI-PARSE JADI ANGKA
            string cleanNominal = EntryHargaBiaya.Text?.Replace(".", "") ?? "";

            if (string.IsNullOrWhiteSpace(cleanNominal) || !double.TryParse(cleanNominal, out double nominal))
            {
                await DisplayAlertAsync("Validasi", "Masukkan nominal harga yang valid.", "OK");
                return;
            }

            if (SelectedBiayaList.Any(x => x.No == selectedCoa.no))
            {
               await DisplayAlertAsync("Validasi", "Biaya ini sudah ditambahkan sebelumnya.", "OK");
                return;
            }

            SelectedBiayaList.Add(new SelectedBiayaModel
            {
                No = selectedCoa.no,
                Name = selectedCoa.name,
                Nominal = nominal
            });

            PickerBiaya.SelectedItem = null;
            EntryHargaBiaya.Text = string.Empty;

            KalkulasiSemuaTotal();

        }
        else
        {
           await DisplayAlertAsync("Peringatan", "Pilih jenis biaya terlebih dahulu dari dropdown.", "OK");
        }
    }

    private void HapusBiaya_Tapped(object sender, TappedEventArgs e)
    {
        if (sender is Label label && label.BindingContext is SelectedBiayaModel biayaData)
        {
            // =========================================================
            // JIKA BIAYA DARI DATABASE (PUNYA ID), MASUKKAN KE TONG SAMPAH
            // =========================================================
            if (biayaData.id != null && biayaData.id > 0)
            {
                _deletedBiayaList.Add(biayaData);
            }

            // Hapus dari koleksi layar UI
            SelectedBiayaList.Remove(biayaData);
            KalkulasiSemuaTotal();
        }
    }

    private async void BHapusDiskon_Clicked(object sender, EventArgs e)
    {
        // Kosongkan input dan kembalikan output ke Rp 0
        EntryDiskonNominal.Text = string.Empty;
        EntryDiskonPersen.Text = string.Empty;
        EntryTotalDiskon.Text = "Rp 0";

        KalkulasiSemuaTotal(); KalkulasiSemuaTotal();
    }

    private async void BTambahkanDiskon_Clicked(object sender, EventArgs e)
    {

        KalkulasiSemuaTotal();

    }

    private void CheckBoxPPN_CheckedChanged(object sender, CheckedChangedEventArgs e) => KalkulasiSemuaTotal();

    private async void HapusCartItem_Tapped(object sender, TappedEventArgs e)
    {

        

        if (sender is Image label && label.BindingContext is CartItemModel cartItem)
        {
            await label.FadeToAsync(0.3, 100); // Turunkan opacity ke 0.3 dalam 100ms
            await label.FadeToAsync(1, 200);   // Kembalikan opacity ke 1 dalam 200ms

            // Cek apakah barang yang dihapus dari keranjang memiliki ID Promo
            if (cartItem.id_promo > 0)
            {
                // Eksekusi API cancel kuota sejumlah qty barang yang dihapus
                await CancelPromoKuotaAsync(cartItem.id_promo, cartItem.quantity);
            }

            // =========================================================
            // JIKA BARANG DARI DATABASE (PUNYA ID), MASUKKAN KE TONG SAMPAH
            // =========================================================
            if (cartItem.id != null && cartItem.id > 0)
            {
                _deletedCartItems.Add(cartItem);
            }

            // Hapus dari koleksi layar UI
            CartItems.Remove(cartItem);
            KalkulasiSemuaTotal();
        }
    }

    // =========================================================
    // FUNGSI API UNTUK MEMBATALKAN & MENGEMBALIKAN KUOTA PROMO
    // =========================================================
    private async Task CancelPromoKuotaAsync(int idPromo, int canceledKuota)
    {
        try
        {
            string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();

            // Menggunakan endpoint baru khusus untuk pembatalan
            string apiUrl = $"{App.API_HOST}promo/cancel-kuota.php";

            // Susun payload menggunakan angka murni (positif) dari qty
            var payload = new
            {
                kuota = canceledKuota,
                id_promo = idPromo
            };

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);
                string jsonPayload = JsonConvert.SerializeObject(payload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                // Eksekusi POST
                var response = await client.PostAsync(apiUrl, content);

                if (!response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"Gagal membatalkan Kuota Promo ID: {idPromo}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Sukses membatalkan Kuota Promo ID: {idPromo} sejumlah {canceledKuota}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Koneksi Cancel Promo Gagal: {ex.Message}");
        }
    }

    private void EntryDiskonNominal_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Cegah infinite loop saat sistem sedang menyisipkan titik
        if (_isFormattingDiskonNominal) return;

        if (!string.IsNullOrEmpty(e.NewTextValue))
        {
            // Hilangkan semua titik yang sudah ada
            string cleanInput = e.NewTextValue.Replace(".", "");

            // Validasi apakah benar-benar angka
            if (double.TryParse(cleanInput, out double parsedNumber))
            {
                _isFormattingDiskonNominal = true;

                // Format ulang ke ribuan (titik)
                EntryDiskonNominal.Text = parsedNumber.ToString("N0", new CultureInfo("id-ID"));

                _isFormattingDiskonNominal = false;
            }
            else
            {
                // Jika user mengetik huruf, tolak dan kembalikan ke teks lama
                _isFormattingDiskonNominal = true;
                EntryDiskonNominal.Text = e.OldTextValue;
                _isFormattingDiskonNominal = false;
            }
        }

        // Panggil fungsi master kalkulasi ke bawah
        KalkulasiSemuaTotal();
    }

    private async void TapCancelKonsumen_Tapped(object sender, TappedEventArgs e)
    {
        // 1. Jika ada barang di keranjang, beri peringatan terlebih dahulu
        if (CartItems.Count > 0)
        {
            bool confirm = await DisplayAlertAsync("Konfirmasi", "Membatalkan Konsumen akan menghapus seluruh barang di keranjang belanja Anda. Lanjutkan?", "Ya, Hapus Semua", "Batal");

            // Jika user memilih Batal, hentikan proses pembatalan
            if (!confirm) return;

            // 2. Jika lanjut, kembalikan (cancel) semua kuota promo yang sedang menempel di keranjang
            foreach (var item in CartItems.ToList())
            {
                if (item.id_promo > 0)
                {
                    await CancelPromoKuotaAsync(item.id_promo, item.quantity);
                }
            }

            // Bersihkan isi keranjang
            CartItems.Clear();
        }

        // 3. Reset Picker, buka kembali kuncinya agar user bisa memilih konsumen baru
        PickerKonsumen.SelectedItem = null;
        SelectedKonsumenValue = null;
        PickerKonsumen.IsEnabled = true;

        // 4. Kembalikan posisi ikon seperti semula
        ImageSearchKonsumen.IsVisible = true;
        ImageCancelKonsumen.IsVisible = false;

        // 5. Kalkulasi ulang untuk mereset seluruh angka uang kembali ke Rp 0
        KalkulasiSemuaTotal();
    }

    private void KalkulasiSemuaTotal()
    {
        // PENGAMAN CRASH: Hentikan fungsi jika layar UI belum selesai dibuat
        if (EntrySubtotal == null || EntryGrandTotal == null) return;

        double subtotal = CartItems.Sum(x => x.TotalPriceAfterDiscount);

        // 1. HITUNG SUBTOTAL (Langsung dari keranjang memori, sangat aman)
        
        EntrySubtotal.Text = $"Rp {subtotal.ToString("N0", new CultureInfo("id-ID"))}";

        // 2. HITUNG DISKON (Aman dari inputan ngawur seperti simbol % atau titik)
        double totalDiskon = 0;
        string cleanNominal = EntryDiskonNominal.Text?.Replace(".", "")?.Trim();
        string cleanPersen = EntryDiskonPersen.Text?.Replace("%", "")?.Trim();

        if (!string.IsNullOrWhiteSpace(cleanNominal) && double.TryParse(cleanNominal, out double diskonNominal))
        {
            totalDiskon = diskonNominal;
            KeteranganDiskon.Text = $"Diskon diterapkan: Rp {diskonNominal.ToString("N0", new CultureInfo("id-ID"))}";
        }
        else if (!string.IsNullOrWhiteSpace(cleanPersen) && double.TryParse(cleanPersen, out double diskonPersen))
        {
            totalDiskon = (diskonPersen / 100) * subtotal; // Dinamis ikut subtotal baru
            KeteranganDiskon.Text = $"Diskon diterapkan: {cleanPersen}%";
        }

        if (totalDiskon > subtotal) totalDiskon = subtotal; // Pengaman
        if (totalDiskon < 0) totalDiskon = 0;


       

        EntryTotalDiskon.Text = $"Rp {totalDiskon.ToString("N0", new CultureInfo("id-ID"))}";


        // 3. HITUNG BIAYA LAIN (Langsung dari list biaya)
        double totalBiaya = SelectedBiayaList.Sum(x => x.Nominal);
        EntryTotalBiaya.Text = $"Rp {totalBiaya.ToString("N0", new CultureInfo("id-ID"))}";

        // 4. HITUNG PPN 11%
        double totalPajak = 0;
        if (CheckBoxPPN != null && CheckBoxPPN.IsChecked)
        {
            double nilaiSetelahDiskon = subtotal - totalDiskon;
            if (nilaiSetelahDiskon < 0) nilaiSetelahDiskon = 0;
            totalPajak = nilaiSetelahDiskon * 0.11;
        }
        EntryTotalPajak.Text = $"Rp {totalPajak.ToString("N0", new CultureInfo("id-ID"))}";

        // 5. HITUNG GRAND TOTAL MURNI
        double grandTotal = (subtotal - totalDiskon) + totalBiaya + totalPajak;
        if (grandTotal < 0) grandTotal = 0;
        EntryGrandTotal.Text = $"Rp {grandTotal.ToString("N0", new CultureInfo("id-ID"))}";

        // 6. PEMBULATAN KE BAWAH (KELIPATAN 100 PERAK)
        double grandTotalRounded = Math.Floor(grandTotal / 100) * 100;
        EntryGrandTotalRounded.Text = $"Rp {grandTotalRounded.ToString("N0", new CultureInfo("id-ID"))}";
    }


}