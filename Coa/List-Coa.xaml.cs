using System.Collections.ObjectModel;
using System.Net.Http.Headers;
using Newtonsoft.Json;

namespace MyPosAccurate2026.Coa;

public partial class List_Coa : ContentPage
{
    ObservableCollection<CoaGroup> _coaGroups = new ObservableCollection<CoaGroup>();
    List<CoaItem> _allCoaItems = new List<CoaItem>();
    const int _limit = 25;
    int _currentPage = 1;
    bool _hasMoreData = true;
    bool _isFetching = false;
    bool _loaded = false;
    string _currentFilter = "Pendapatan";
    CancellationTokenSource _searchCts;

    public List_Coa()
    {
        InitializeComponent();
        CV_Coa.ItemsSource = _coaGroups;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_loaded)
        {
            _loaded = true;
            await LoadCoa(true);
        }
    }

    private async Task LoadCoa(bool reset)
    {
        if (_isFetching) return;

        if (reset)
        {
            _currentPage = 1;
            _hasMoreData = true;
            _allCoaItems.Clear();
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _coaGroups.Clear();
            });
        }

        if (!_hasMoreData)
        {
            MainThread.BeginInvokeOnMainThread(() => RV_Coa.IsRefreshing = false);
            return;
        }

        _isFetching = true;

        try
        {
            string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
            if (string.IsNullOrEmpty(cleanToken))
            {
                await DisplayAlertAsync("Sesi Habis", "Anda harus login kembali.", "OK");
                return;
            }

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);

                string search = (T_Search.Text ?? "").Trim();
                
                string endpoint = "coa/list.php";
                if (_currentFilter == "Kas & Bank")
                {
                    endpoint = "coa/list-kasbank.php";
                }
                
                string apiUrl = $"{App.API_HOST}{endpoint}?limit={_limit}&page={_currentPage}";
                
                if (!string.IsNullOrEmpty(search))
                {
                    apiUrl += $"&search={Uri.EscapeDataString(search)}";
                }
                
                if (_currentFilter == "Pendapatan")
                {
                    apiUrl += $"&type=pendapatan"; // Menambahkan parameter opsional jika dibutuhkan backend
                }

                var response = await client.GetAsync(apiUrl);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (responseContent.StartsWith("<"))
                {
                    await DisplayAlertAsync("Error Server", "Gagal membaca format data dari server.", "OK");
                    return;
                }

                CoaResponse result = JsonConvert.DeserializeObject<CoaResponse>(responseContent);
                if (result?.data == null || result.data.Count == 0)
                {
                    _hasMoreData = false;
                }
                else
                {
                    _allCoaItems.AddRange(result.data);
                    
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        GroupData(_allCoaItems);
                    });

                    if (result.data.Count < _limit) _hasMoreData = false;
                    else _currentPage++;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Gagal memuat data COA: {ex.Message}");
        }
        finally
        {
            _isFetching = false;
            MainThread.BeginInvokeOnMainThread(() => RV_Coa.IsRefreshing = false);
        }
    }

    private void GroupData(List<CoaItem> items)
    {
        _coaGroups.Clear();
        var grouped = items.GroupBy(x => x.accountTypeName);
        foreach (var group in grouped)
        {
            _coaGroups.Add(new CoaGroup(group.Key ?? "Lainnya", group));
        }
    }

    private async void OnRefreshing(object sender, EventArgs e)
    {
        await LoadCoa(true);
    }

    private void OnThresholdReached(object sender, EventArgs e)
    {
        if (_hasMoreData && !_isFetching)
            _ = LoadCoa(false);
    }

    private async void T_Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_searchCts != null)
            _searchCts.Cancel();
            
        _searchCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(500, _searchCts.Token);
            await LoadCoa(true);
        }
        catch (TaskCanceledException)
        {
        }
    }

    private async void TapAdd_Tapped(object sender, TappedEventArgs e)
    {
        string action = await DisplayActionSheetAsync("Pilih Kategori", "Batal", null, "Pendapatan", "Kas & Bank", "Semua");
        if (action != "Batal" && !string.IsNullOrEmpty(action))
        {
            _currentFilter = action;
            await LoadCoa(true);
        }
    }
}

public class CoaResponse
{
    public List<CoaItem> data { get; set; }
}

public class CoaItem
{
    public int id { get; set; }
    public string no { get; set; }
    public string name { get; set; }
    public string asOf { get; set; }
    public double balance { get; set; }
    public string accountTypeName { get; set; }
    public int lvl { get; set; }

    public string balance_fmt => balance >= 0 ? $"Rp {balance:N0}" : $"-Rp {Math.Abs(balance):N0}";
    public string asOf_fmt => $"SALDO PER {asOf}";
    public string lvl_fmt => $"LEVEL: {lvl}";
}

public class CoaGroup : ObservableCollection<CoaItem>
{
    public string Name { get; set; }
    public string CountText => $"{Count} Akun";

    public CoaGroup(string name, IEnumerable<CoaItem> items) : base(items)
    {
        Name = name;
    }
}