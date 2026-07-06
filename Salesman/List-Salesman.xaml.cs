using System.Collections.ObjectModel;
using System.Net.Http.Headers;
using System.Threading;
using Newtonsoft.Json;

namespace MyPosAccurate2026.Salesman;

public partial class List_Salesman : ContentPage
{
    ObservableCollection<SalesmanItem> _salesmen = new ObservableCollection<SalesmanItem>();
    
    int _currentPage = 1;
    const int _limit = 100;
    bool _hasMoreData = true;
    bool _isFetching = false;
    bool _loaded = false;
    
    CancellationTokenSource _searchCts;

    public List_Salesman()
    {
        InitializeComponent();
        CV_Salesman.ItemsSource = _salesmen;
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

    private async Task LoadData(bool reset, bool showLoading = true)
    {
        if (_isFetching) return;

        if (reset)
        {
            _currentPage = 1;
            _hasMoreData = true;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _salesmen.Clear();
                if (showLoading) LoadingOverlay.IsVisible = true;
            });
        }

        if (!_hasMoreData)
        {
            MainThread.BeginInvokeOnMainThread(() => LoadingOverlay.IsVisible = false);
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
                string apiUrl = $"{App.API_HOST}karyawan/list.php?sales=true&limit={_limit}&page={_currentPage}";
                
                if (!string.IsNullOrEmpty(search))
                {
                    apiUrl += $"&search={Uri.EscapeDataString(search)}";
                }

                var response = await client.GetAsync(apiUrl);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (responseContent.StartsWith("<"))
                {
                    await DisplayAlertAsync("Error Server", "Gagal membaca format data dari server.", "OK");
                    return;
                }

                var apiResponse = JsonConvert.DeserializeObject<SalesmanResponse>(responseContent);
                var data = apiResponse?.data;

                if (data == null || data.Count == 0)
                {
                    _hasMoreData = false;
                }
                else
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        foreach (var item in data)
                        {
                            _salesmen.Add(item);
                        }
                    });

                    if (data.Count < _limit) _hasMoreData = false;
                    else _currentPage++;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Gagal memuat data salesman: {ex.Message}");
        }
        finally
        {
            _isFetching = false;
            MainThread.BeginInvokeOnMainThread(() => LoadingOverlay.IsVisible = false);
        }
    }

    private async void T_Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loaded) return;

        if (_searchCts != null)
            _searchCts.Cancel();
            
        _searchCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(500, _searchCts.Token);
            await LoadData(true, showLoading: true);
        }
        catch (TaskCanceledException)
        {
        }
    }

    private async void TapAdd_Tapped(object sender, TappedEventArgs e)
    {
        var page = new NewSalesman();
        page.OnSalesmanSaved += async (s, args) =>
        {
            await LoadData(true);
        };
        //page.HandleColor = Color.FromArgb()
        _ = page.ShowAsync(Window);
    }

    private async void CV_Salesman_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var item = e.CurrentSelection.FirstOrDefault() as SalesmanItem;
        if (item == null) return;
        
        string action = await DisplayActionSheet("Opsi", "BATAL", null, "EDIT");
        if (action == "EDIT")
        {
            var sheet = new NewSalesman();
            sheet.OnSalesmanSaved += async (s, args) =>
            {
                await LoadData(true);
            };
            
            _ = sheet.ShowAsync(Window);
            _ = sheet.SetEditMode(item);
        }
        
        CV_Salesman.SelectedItem = null;
    }

    private async void OnRefreshing(object sender, EventArgs e)
    {
        await LoadData(true, showLoading: false);
        RV_Salesman.IsRefreshing = false;
    }

    private void OnThresholdReached(object sender, EventArgs e)
    {
        if (_loaded && _hasMoreData && !_isFetching)
            _ = LoadData(false);
    }
}

public class SalesmanResponse
{
    public string status { get; set; }
    public string message { get; set; }
    public List<SalesmanItem> data { get; set; }
}

public class SalesmanItem
{
    public string number { get; set; }
    public int? branchId { get; set; }
    public string salesmanUser { get; set; }
    public string name { get; set; }
    public bool salesman { get; set; }
    public int id { get; set; }
    public bool suspended { get; set; }

    public string Initial => string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpper();
    public string StatusIcon => suspended ? "nonactive1.png" : "active1.png";
    public Microsoft.Maui.Graphics.Color AvatarColor
    {
        get
        {
            var colors = new[] { Colors.Cornsilk, Colors.AliceBlue, Colors.Honeydew, Colors.Lavender, Colors.MistyRose };
            return colors[Math.Abs(id) % colors.Length];
        }
    }
}