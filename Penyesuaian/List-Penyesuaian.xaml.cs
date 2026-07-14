using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.Net.Http.Headers;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Maui.Views;
using Microsoft.Maui.Controls.Shapes;

namespace MyPosAccurate2026.Penyesuaian;

public partial class List_Penyesuaian : ContentPage
{
    private ObservableCollection<AdjustmentItem> _adjustments = new ObservableCollection<AdjustmentItem>();
    private CancellationTokenSource _cts = new CancellationTokenSource();
    private bool _loaded = false;
    private string _startDate = "";
    private string _endDate = "";

    public List_Penyesuaian()
    {
        InitializeComponent();
        CV_Adjustments.ItemsSource = _adjustments;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_loaded)
        {
            _loaded = true;
            await LoadDataAsync("");
        }
    }

    private async Task LoadDataAsync(string search)
    {
        MainThread.BeginInvokeOnMainThread(() => OverlayLoading.IsVisible = true);
        var delayTask = Task.Delay(3000);
        try
        {
            _cts.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            string url = $"{App.API_HOST}item-adjustment/list.php?limit=100&page=1";
            if (!string.IsNullOrWhiteSpace(search))
            {
                url += $"&search={Uri.EscapeDataString(search)}";
            }
            if (!string.IsNullOrWhiteSpace(_startDate) && !string.IsNullOrWhiteSpace(_endDate))
            {
                url += $"&startDate={Uri.EscapeDataString(_startDate)}&endDate={Uri.EscapeDataString(_endDate)}";
            }

            string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
            if (string.IsNullOrEmpty(cleanToken))
            {
                await DisplayAlertAsync("Sesi Habis", "Anda harus login kembali.", "OK");
                return;
            }

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);
                var response = await client.GetAsync(url, token);
                var content = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    MainThread.BeginInvokeOnMainThread(async () => {
                        await DisplayAlertAsync("API Error", $"Status {response.StatusCode}\n{content}", "OK");
                    });
                    return;
                }

                if (content.StartsWith("<"))
                {
                    MainThread.BeginInvokeOnMainThread(async () => {
                        await DisplayAlertAsync("API Error", "Respons berupa HTML, bukan JSON.", "OK");
                    });
                    return;
                }

                try
                {
                    var result = JsonConvert.DeserializeObject<AdjustmentResponse>(content);
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        _adjustments.Clear();
                        if (result?.data != null)
                        {
                            foreach (var item in result.data)
                            {
                                _adjustments.Add(item);
                            }
                        }
                    });
                }
                catch (Exception ex)
                {
                    MainThread.BeginInvokeOnMainThread(async () => {
                        await DisplayAlertAsync("JSON Error", ex.Message, "OK");
                    });
                }
            }
        }
        catch (TaskCanceledException) { }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
        finally
        {
            await delayTask;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                OverlayLoading.IsVisible = false;
                RV_Adjustments.IsRefreshing = false;
            });
        }
    }

    private async void OnRefreshing(object sender, EventArgs e)
    {
        await LoadDataAsync(T_Search.Text ?? "");
    }

    private void T_Search_SearchButtonPressed(object sender, EventArgs e)
    {
        _ = LoadDataAsync(T_Search.Text ?? "");
    }

    private void TapAdd_Tapped(object sender, TappedEventArgs e)
    {
        FormFilterDate.IsVisible = !FormFilterDate.IsVisible;
    }

    private async void BtnApplyFilter_Tapped(object sender, TappedEventArgs e)
    {
        _startDate = string.Format("{0:dd/MM/yyyy}", DP_StartDate.Date);
        _endDate = string.Format("{0:dd/MM/yyyy}", DP_EndDate.Date);
        FormFilterDate.IsVisible = false;
        await LoadDataAsync(T_Search.Text ?? "");
    }
}

public class AdjustmentResponse
{
    public string status { get; set; }
    public string message { get; set; }
    public List<AdjustmentItem> data { get; set; }
}

public class AdjustmentItem
{
    public int id { get; set; }
    public string number { get; set; }
    public string transDate { get; set; }
    public string transDateView { get; set; }
    public string description { get; set; }
}