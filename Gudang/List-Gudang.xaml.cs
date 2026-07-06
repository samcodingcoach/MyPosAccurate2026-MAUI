using System.Collections.ObjectModel;
using System.Net.Http.Headers;
using Newtonsoft.Json;

namespace MyPosAccurate2026.Gudang;

public partial class List_Gudang : ContentPage
{
    ObservableCollection<GudangItem> _gudangList = new ObservableCollection<GudangItem>();
    const int _limit = 25;
    int _currentPage = 1;
    bool _hasMoreData = true;
    bool _isFetching = false;
    bool _loaded = false;
    int? _editingGudangId = null;
    CancellationTokenSource _searchCts;

    public List_Gudang()
    {
        InitializeComponent();
        CV_Gudang.ItemsSource = _gudangList;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_loaded)
        {
            _loaded = true;
            await LoadGudang(true);
        }
    }

    private async Task LoadGudang(bool reset)
    {
        if (_isFetching) return;

        if (reset)
        {
            _currentPage = 1;
            _hasMoreData = true;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _gudangList.Clear();
            });
        }

        if (!_hasMoreData)
        {
            MainThread.BeginInvokeOnMainThread(() => RV_Gudang.IsRefreshing = false);
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
                string apiUrl = $"{App.API_HOST}gudang/list.php?limit={_limit}&page={_currentPage}";
                
                if (!string.IsNullOrEmpty(search))
                {
                    apiUrl += $"&search={Uri.EscapeDataString(search)}";
                }

                var response = await client.GetAsync(apiUrl);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (responseContent.StartsWith("<"))
                {
                    await DisplayAlert("Error Server", "Gagal membaca format data dari server.", "OK");
                    return;
                }

                GudangResponse result = JsonConvert.DeserializeObject<GudangResponse>(responseContent);
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
                            _gudangList.Add(item);
                        }
                    });

                    if (result.data.Count < _limit) _hasMoreData = false;
                    else _currentPage++;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Gagal memuat data gudang: {ex.Message}");
        }
        finally
        {
            _isFetching = false;
            MainThread.BeginInvokeOnMainThread(() => RV_Gudang.IsRefreshing = false);
        }
    }

    private async void OnRefreshing(object sender, EventArgs e)
    {
        await LoadGudang(true);
    }

    private void OnThresholdReached(object sender, EventArgs e)
    {
        if (_hasMoreData && !_isFetching)
            _ = LoadGudang(false);
    }

    private async void T_Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_searchCts != null)
            _searchCts.Cancel();
            
        _searchCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(500, _searchCts.Token);
            await LoadGudang(true);
        }
        catch (TaskCanceledException)
        {
        }
    }

    private async void BUpdate_Clicked(object sender, EventArgs e)
    {
        if (!_editingGudangId.HasValue) return;

        string name = FormNamaGudang.Text?.Trim();
        string pic = FormPIC.Text?.Trim();
        string province = FormProvinsi.Text?.Trim();
        string street = FormAddress.Text?.Trim();

        if (string.IsNullOrEmpty(name))
        {
            await DisplayAlert("Peringatan", "Nama gudang harus diisi.", "OK");
            return;
        }

        BUpdate.IsEnabled = false;
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

                string apiUrl = $"{App.API_HOST}gudang/update.php";

                var payload = new
                {
                    id = _editingGudangId.Value.ToString(),
                    name = name,
                    pic = pic,
                    province = province,
                    street = street,
                    scrapWarehouse = true,
                    suspended = false
                };

                string jsonPayload = JsonConvert.SerializeObject(payload);
                var content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");

                var response = await client.PostAsync(apiUrl, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    await DisplayAlert("Sukses", "Gudang berhasil diperbarui.", "OK");
                    
                    FormEdit.IsVisible = false;
                    RV_Gudang.IsVisible = true;
                    _editingGudangId = null;

                    await LoadGudang(true);
                }
                else
                {
                    await DisplayAlert("Gagal", $"Gagal memperbarui gudang. Status: {response.StatusCode}\n{responseContent}", "OK");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Gagal memperbarui gudang: {ex.Message}");
            await DisplayAlert("Error", "Terjadi kesalahan saat menyimpan data.", "OK");
        }
        finally
        {
            BUpdate.IsEnabled = true;
        }
    }

    private void BBatal_Clicked(object sender, EventArgs e)
    {
        FormEdit.IsVisible = false;
        RV_Gudang.IsVisible = true;
        _editingGudangId = null;
    }

    private void UpdateGudang_Tapped(object sender, TappedEventArgs e)
    {
        if (sender is Image img && img.BindingContext is GudangItem item)
        {
            _editingGudangId = item.id;
            FormNamaGudang.Text = item.name;
            FormPIC.Text = item.pic;
            FormProvinsi.Text = item.province;
            FormAddress.Text = item.street;
            
            FormEdit.IsVisible = true;
            RV_Gudang.IsVisible = false;
        }
    }
}

public class GudangResponse
{
    public List<GudangItem> data { get; set; }
}

public class GudangItem
{
    public int id { get; set; }
    public string name { get; set; }
    public bool is_suspended { get; set; }
    public bool is_default { get; set; }
    public bool is_scrap { get; set; }
    public string street { get; set; }
    public string province { get; set; }
    public string pic { get; set; }

    public string full_address
    {
        get
        {
            if (string.IsNullOrWhiteSpace(street) && string.IsNullOrWhiteSpace(province)) return "";
            if (string.IsNullOrWhiteSpace(street)) return province;
            if (string.IsNullOrWhiteSpace(province)) return street;
            return $"{street}, {province}";
        }
    }
}