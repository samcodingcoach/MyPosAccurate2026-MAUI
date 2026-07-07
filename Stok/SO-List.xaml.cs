using System.Collections.ObjectModel;
using System.Net.Http.Headers;
using Newtonsoft.Json;

namespace MyPosAccurate2026.Stok;

public partial class SO_List : ContentPage
{
    ObservableCollection<SOItem> _soList = new ObservableCollection<SOItem>();
    const int _limit = 100;
    int _currentPage = 1;
    bool _hasMoreData = true;
    bool _isFetching = false;
    bool _loaded = false;
    CancellationTokenSource _searchCts;
    string _selectedDate = "";

	public SO_List()
	{
		InitializeComponent();
        CV_SO.ItemsSource = _soList;
	}

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_loaded)
        {
            _loaded = true;
            await LoadData(true);
        }
    }

    private async Task LoadData(bool reset)
    {
        if (_isFetching) return;

        if (reset)
        {
            _currentPage = 1;
            _hasMoreData = true;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _soList.Clear();
            });
        }

        if (!_hasMoreData)
        {
            MainThread.BeginInvokeOnMainThread(() => RV_SO.IsRefreshing = false);
            return;
        }

        _isFetching = true;

        try
        {
            string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
            if (string.IsNullOrEmpty(cleanToken))
            {
                await DisplayAlert("Sesi Habis", "Anda harus login kembali.", "OK");
                return;
            }

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);

                string search = (T_Search.Text ?? "").Trim();
                string apiUrl = $"{App.API_HOST}stokopname-order/list.php?limit={_limit}&page={_currentPage}";
                
                if (!string.IsNullOrEmpty(search))
                {
                    apiUrl += $"&search={Uri.EscapeDataString(search)}";
                }

                if (!string.IsNullOrEmpty(_selectedDate))
                {
                    apiUrl += $"&transDate={Uri.EscapeDataString(_selectedDate)}";
                }

                var response = await client.GetAsync(apiUrl);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (responseContent.StartsWith("<"))
                {
                    await DisplayAlert("Error Server", "Gagal membaca format data dari server.", "OK");
                    return;
                }

                SOResponse result = JsonConvert.DeserializeObject<SOResponse>(responseContent);
                if (result?.data == null || result.data.Count == 0)
                {
                    _hasMoreData = false;
                }
                else
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        foreach (var item in result.data)
                        {
                            _soList.Add(item);
                        }
                    });

                    if (result.data.Count < _limit) _hasMoreData = false;
                    else _currentPage++;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Gagal memuat data SO: {ex.Message}");
        }
        finally
        {
            _isFetching = false;
            MainThread.BeginInvokeOnMainThread(() => RV_SO.IsRefreshing = false);
        }
    }

    private async void OnRefreshing(object sender, EventArgs e)
    {
        await LoadData(true);
    }

    private void OnThresholdReached(object sender, EventArgs e)
    {
        if (_hasMoreData && !_isFetching)
            _ = LoadData(false);
    }

    private async void T_Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_searchCts != null)
            _searchCts.Cancel();
            
        _searchCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(500, _searchCts.Token);
            await LoadData(true);
        }
        catch (TaskCanceledException)
        {
        }
    }

    private async void FilterDate_DateSelected(object sender, DateChangedEventArgs e)
    {
        // Parameter date dalam format DD/MM/YYYY
        _selectedDate = string.Format("{0:dd/MM/yyyy}", e.NewDate);
        await LoadData(true);
    }

    private async void BtnHasilStokOpname_Tapped(object sender, TappedEventArgs e)
    {
        if (sender is Border border && border.BindingContext is SOItem item)
        {
            var detailPage = new DetailwithInsert { TransNumber = item.number };
            await Navigation.PushAsync(detailPage);
        }
    }
}

// ===== DTO =====

public class SOResponse
{
    public string status { get; set; }
    public string message { get; set; }
    public List<SOItem> data { get; set; }
}

public class SOItem
{
    public int id { get; set; }
    public string number { get; set; }
    public string transDate { get; set; }
    public string statusName { get; set; }
    public string description { get; set; }
    public string personCharged { get; set; }
    public string startDateView { get; set; }
    public Warehouse warehouse { get; set; }
    public string transDateView { get; set; }
    public string startDate { get; set; }
}

public class Warehouse
{
    public bool defaultWarehouse { get; set; }
    public bool scrapWarehouse { get; set; }
    public int locationId { get; set; }
    public int optLock { get; set; }
    public string name { get; set; }
    public string description { get; set; }
    public string pic { get; set; }
    public int id { get; set; }
    public bool suspended { get; set; }
}