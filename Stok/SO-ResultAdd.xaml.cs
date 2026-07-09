using Newtonsoft.Json;
using System.Net.Http.Headers;

namespace MyPosAccurate2026.Stok;

public partial class SO_ResultAdd : ContentPage
{
    private List<SOItem> _availableOrders = new List<SOItem>();
	public SO_ResultAdd()
	{
		InitializeComponent();
	}

    private async void HiddenDatePicker_DateSelected(object sender, DateChangedEventArgs e)
    {
        string selectedDate = $"{e.NewDate:dd/MM/yyyy}";
        FormtransDate.Text = selectedDate;
        await LoadOrders(selectedDate);
    }

    private async Task LoadOrders(string date)
    {
        PickerorderNumber.ItemsSource = null;
        _availableOrders.Clear();

        try
        {
            string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);

            string apiUrl = $"{App.API_HOST}stokopname-order/list.php?transDate={Uri.EscapeDataString(date)}";
            var response = await client.GetAsync(apiUrl);
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<SOResponse>(content);
                
                if (result?.status == "success" && result.data != null)
                {
                    // Filter: statusName == "Menunggu Eksekusi" atau "Dalam Penghitungan"
                    _availableOrders = result.data.Where(x => 
                        x.statusName == "Menunggu Eksekusi" || 
                        x.statusName == "Dalam Penghitungan").ToList();
                    
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        PickerorderNumber.ItemsSource = _availableOrders.Select(x => x.number).ToList();
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error LoadOrders: {ex.Message}");
        }
    }
}