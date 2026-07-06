using System.Collections.ObjectModel;
using System.Net.Http.Headers;
using Newtonsoft.Json;
using System.Globalization;

namespace MyPosAccurate2026.Produk;

public partial class List_Produk : ContentPage
{
    public ObservableCollection<ProdukModel> ProdukList { get; set; } = new ObservableCollection<ProdukModel>();

    private int _currentPage = 1;
    private bool _isFetching = false;
    private bool _hasMoreData = true;
    private int _currentSortIndex = -1;

    public List_Produk()
    {
        InitializeComponent();
        ProductList.ItemsSource = ProdukList;
        _ = LoadInitialData();
    }

    private async Task LoadInitialData()
    {
        _currentPage = 1;
        _hasMoreData = true;
        ProdukList.Clear();
        await FetchProductsFromApi();
    }

    private async void T_Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        string keyword = e.NewTextValue ?? "";
        
        // Minimal 3 huruf atau kosong (reset)
        if (keyword.Length > 0 && keyword.Length < 3) return; 

        await Task.Delay(400);

        if (keyword != T_Search.Text) return;

        _currentPage = 1;
        _hasMoreData = true;
        ProdukList.Clear();
        await FetchProductsFromApi();
    }

    private async void SortImage_Tapped(object sender, TappedEventArgs e)
    {
        string action = await DisplayActionSheet("Urutkan Produk", "Batal", null, "A - Z", "Harga Tertinggi", "Harga Terendah");
        
        if (action == "A - Z")
            _currentSortIndex = 0;
        else if (action == "Harga Tertinggi")
            _currentSortIndex = 1;
        else if (action == "Harga Terendah")
            _currentSortIndex = 2;
        else
            return; // Batal dipilih

        ApplyLocalSort();
    }

    private async void RefreshViewProduk_Refreshing(object sender, EventArgs e)
    {
        _currentPage = 1;
        _hasMoreData = true;
        ProdukList.Clear();
        await FetchProductsFromApi();
        RefreshViewProduk.IsRefreshing = false;
    }

    private async void CollectionView_RemainingItemsThresholdReached(object sender, EventArgs e)
    {
        if (_isFetching || !_hasMoreData) return;
        _currentPage++;
        await FetchProductsFromApi();
    }

    private async void ProductList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is ProdukModel selectedItem)
        {
            // Kosongkan kembali selected item agar barang yang sama bisa ditekan lagi nanti
            ProductList.SelectedItem = null;

            if (!string.IsNullOrEmpty(selectedItem.item_no))
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    try
                    {
                        await Navigation.PushAsync(new DetailScan(selectedItem.item_no));
                    }
                    catch (Exception ex)
                    {
                        await Application.Current.MainPage.DisplayAlert("Error", $"Gagal membuka detail: {ex.Message}", "OK");
                    }
                });
            }
        }
    }

    private async Task FetchProductsFromApi()
    {
        if (_isFetching || !_hasMoreData) return;

        _isFetching = true;
        try
        {
            string keyword = T_Search.Text ?? "";
            string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
            
            // Paging parameter
            int limit = 100;
            int offset = (_currentPage - 1) * limit;

            // Membangun URL dengan limit & offset / page sesuai backend. 
            // Kita coba pakai parameter &limit=100&offset=x atau &page=x (tergantung backend, ini standar).
            string apiUrl = $"{App.API_HOST}item/list-lokal.php?search={Uri.EscapeDataString(keyword)}&limit={limit}&offset={offset}";

            using (var client = new HttpClient())
            {
                if (!string.IsNullOrEmpty(cleanToken))
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);

                client.DefaultRequestHeaders.Add("User-Agent", "AccuratePOS-App/1.0");

                var response = await client.GetAsync(apiUrl);
                if (!response.IsSuccessStatusCode)
                {
                    MainThread.BeginInvokeOnMainThread(async () => 
                    {
                        await Application.Current.MainPage.DisplayAlert("Error HTTP", $"Status Code: {response.StatusCode}", "OK");
                    });
                    return;
                }

                string responseContent = await response.Content.ReadAsStringAsync();
                
                if (string.IsNullOrWhiteSpace(responseContent) || responseContent.TrimStart().StartsWith("<"))
                {
                    MainThread.BeginInvokeOnMainThread(async () => 
                    {
                        await Application.Current.MainPage.DisplayAlert("Error API", $"Respon bukan JSON:\n{responseContent}", "OK");
                    });
                    return;
                }

                var apiResult = JsonConvert.DeserializeObject<ApiResponse>(responseContent);

                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    if (apiResult?.status == "success" && apiResult.data != null)
                    {
                        if (apiResult.data.Count == 0 && string.IsNullOrEmpty(keyword) && _currentPage == 1)
                        {
                            await Application.Current.MainPage.DisplayAlert("Info", "Data API Produk kosong.", "OK");
                        }

                        // Jika data yang dikembalikan kurang dari limit, artinya ini halaman terakhir
                        if (apiResult.data.Count < limit)
                        {
                            _hasMoreData = false;
                        }

                        foreach (var item in apiResult.data)
                        {
                            ProdukList.Add(item);
                        }

                        // Terapkan sort setelah data ditambahkan
                        ApplyLocalSort();
                    }
                    else if (apiResult?.status != "success")
                    {
                        await Application.Current.MainPage.DisplayAlert("API Error", apiResult?.message ?? "Gagal memuat dari API.", "OK");
                    }
                });
            }
        }
        catch (Exception ex)
        {
            MainThread.BeginInvokeOnMainThread(async () => 
            {
                await Application.Current.MainPage.DisplayAlert("Exception", ex.Message, "OK");
            });
            System.Diagnostics.Debug.WriteLine("Fetch API Error: " + ex.Message);
        }
        finally
        {
            _isFetching = false;
        }
    }

    private void ApplyLocalSort()
    {
        if (ProdukList == null || ProdukList.Count == 0) return;

        List<ProdukModel> sortedList;
        if (_currentSortIndex == 0) // A-Z
            sortedList = ProdukList.OrderBy(x => x.name).ToList();
        else if (_currentSortIndex == 1) // Harga Tertinggi
            sortedList = ProdukList.OrderByDescending(x => x.price).ToList();
        else if (_currentSortIndex == 2) // Harga Terendah
            sortedList = ProdukList.OrderBy(x => x.price).ToList();
        else
            return;

        ProdukList.Clear();
        foreach (var item in sortedList)
        {
            ProdukList.Add(item);
        }
    }

    public class ApiResponse
    {
        public string status { get; set; }
        public string message { get; set; }
        public List<ProdukModel> data { get; set; }
    }

    public class ProdukModel
    {
        public int id { get; set; }
        public string item_no { get; set; }
        public string name { get; set; }
        public string barcode { get; set; }
        public double balance { get; set; }
        public double price { get; set; }
        public string image { get; set; }
        public string last_sync { get; set; }
        public int id_users { get; set; }

        public bool IsHabis => balance <= 0;

        public string FormattedPrice => $"Rp {price.ToString("N0", new CultureInfo("id-ID"))}";

        public string DisplayImage
        {
            get
            {
                if (string.IsNullOrWhiteSpace(image)) return "nophotoproduct150.jpg";
                if (image.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return image;
                string baseHost = App.API_HOST?.Replace("api/", "");
                string cleanFileName = image.Replace("../", "").Replace("images/", "").TrimStart('/');
                return $"{baseHost}images/{cleanFileName}";
            }
        }
    }
}