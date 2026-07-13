using Newtonsoft.Json;
using System.Collections.ObjectModel;

namespace MyPosAccurate2026.Pemasok;

public partial class List_Pemasok : ContentPage
{
    private ObservableCollection<VendorItem> _vendors = new ObservableCollection<VendorItem>();
    private CancellationTokenSource _cts = new CancellationTokenSource();
    private bool _loaded = false;

    public List_Pemasok()
    {
        InitializeComponent();
        CV_Vendors.ItemsSource = _vendors;
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
        var delayTask = Task.Delay(3000);
        try
        {
            _cts.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            string url = $"{App.API_HOST}vendor/list.php?limit=100&page=1";
            if (!string.IsNullOrWhiteSpace(search))
            {
                url += $"&search={Uri.EscapeDataString(search)}";
            }

            string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
            if (string.IsNullOrEmpty(cleanToken))
            {
                await DisplayAlertAsync("Sesi Habis", "Anda harus login kembali.", "OK");
                return;
            }

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", cleanToken);
                var response = await client.GetAsync(url, token);
                var content = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    MainThread.BeginInvokeOnMainThread(async () => {
                        await DisplayAlert("API Error", $"Status {response.StatusCode}\n{content}", "OK");
                    });
                    return;
                }

                if (content.StartsWith("<"))
                {
                    MainThread.BeginInvokeOnMainThread(async () => {
                        await DisplayAlert("API Error", "Respons berupa HTML, bukan JSON.", "OK");
                    });
                    return;
                }

                try 
                {
                    var result = JsonConvert.DeserializeObject<VendorResponse>(content);
                    MainThread.BeginInvokeOnMainThread(() => 
                    {
                        _vendors.Clear();
                        if (result?.data != null)
                        {
                            foreach (var item in result.data)
                            {
                                _vendors.Add(item);
                            }
                        }
                    });
                }
                catch
                {
                    try 
                    {
                        var arrResult = JsonConvert.DeserializeObject<List<VendorItem>>(content);
                        MainThread.BeginInvokeOnMainThread(() => 
                        {
                            _vendors.Clear();
                            if (arrResult != null)
                            {
                                foreach (var item in arrResult)
                                {
                                    _vendors.Add(item);
                                }
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        MainThread.BeginInvokeOnMainThread(async () => {
                            await DisplayAlert("JSON Error", ex.Message, "OK");
                        });
                    }
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
                RV_Vendors.IsRefreshing = false;
            });
        }
    }

    private async void OnRefreshing(object sender, EventArgs e)
    {
        await LoadDataAsync(T_Search.Text ?? "");
    }

    private void T_Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        _ = LoadDataAsync(e.NewTextValue);
    }

    private async void Email_Tapped(object sender, TappedEventArgs e)
    {
        if (sender is View view && view.BindingContext is VendorItem item && !string.IsNullOrWhiteSpace(item.email))
        {
            try
            {
                var message = new EmailMessage
                {
                    To = new List<string> { item.email }
                };
                await Email.Default.ComposeAsync(message);
            }
            catch (Exception) { }
        }
    }

    private async void Phone_Tapped(object sender, TappedEventArgs e)
    {
        if (sender is View view && view.BindingContext is VendorItem item && !string.IsNullOrWhiteSpace(item.mobilePhone))
        {
            try
            {
                string phone = item.mobilePhone;
                if (phone.StartsWith("0")) phone = "62" + phone.Substring(1);
                string url = $"https://wa.me/{phone}";
                await Browser.Default.OpenAsync(url, BrowserLaunchMode.External);
            }
            catch (Exception) { }
        }
    }
}

public class VendorResponse
{
    public List<VendorItem> data { get; set; }
}

public class VendorItem
{
    public string vendorNo { get; set; }
    public string mobilePhone { get; set; }
    public string name { get; set; }
    public string vendorBranchName { get; set; }
    public string email { get; set; }
    public string lookupSubText { get; set; }

    public string CleanLookupSubText
    {
        get
        {
            if (string.IsNullOrEmpty(lookupSubText)) return "";
            return lookupSubText.StartsWith("\n") ? lookupSubText.Substring(1) : lookupSubText;
        }
    }

    public bool HasEmail => !string.IsNullOrWhiteSpace(email);
    public bool HasPhone => !string.IsNullOrWhiteSpace(mobilePhone);
}