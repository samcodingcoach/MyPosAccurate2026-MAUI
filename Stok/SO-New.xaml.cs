using Newtonsoft.Json;
using System.Net.Http.Headers;

namespace MyPosAccurate2026.Stok;

public partial class SO_New : ContentPage
{
    private List<WarehouseItem> _warehouses = new List<WarehouseItem>();
    private List<string> _users = new List<string>();

	public SO_New()
	{
		InitializeComponent();
        HiddenDatePicker.MinimumDate = DateTime.Today; // Kunci agar tidak boleh kurang dari sekarang
	}

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        
        var loadTasks = new List<Task>();
        if (_warehouses.Count == 0) loadTasks.Add(LoadWarehouses());
        if (_users.Count == 0) loadTasks.Add(LoadUsers());

        await Task.WhenAll(loadTasks);
    }

    private async Task LoadWarehouses()
    {
        try
        {
            string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);

            string apiUrl = $"{App.API_HOST}gudang/list.php?limit=100";
            var response = await client.GetAsync(apiUrl);
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<WarehouseResponse>(content);
                
                if (result?.status == "success" && result.data != null)
                {
                    // Hanya ambil gudang yang is_suspended == false
                    _warehouses = result.data.Where(w => !w.is_suspended).ToList();
                    
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        FormwarehouseName.ItemsSource = _warehouses.Select(w => w.name).ToList();
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error LoadWarehouses: {ex.Message}");
        }
    }

    private async Task LoadUsers()
    {
        try
        {
            string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);

            string apiUrl = $"{App.API_HOST}akses/list.php?search=stocker";
            var response = await client.GetAsync(apiUrl);
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<AksesResponse>(content);
                
                if (result?.data != null)
                {
                    var stockerData = result.data.FirstOrDefault(d => d.name != null && d.name.Equals("Stocker", StringComparison.OrdinalIgnoreCase));
                    if (stockerData != null && stockerData.userList != null)
                    {
                        _users = stockerData.userList.Select(u => u.email).ToList();
                        
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            FormUserListAccount.ItemsSource = _users;
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error LoadUsers: {ex.Message}");
        }
    }

    private async void FormwarehouseName_SelectedIndexChanged(object sender, EventArgs e)
    {
        int index = FormwarehouseName.SelectedIndex;
        if (index >= 0 && index < _warehouses.Count)
        {
            var selectedWarehouse = _warehouses[index];
            if (!string.IsNullOrWhiteSpace(selectedWarehouse.pic))
            {
                bool answer = await DisplayAlert("Penanggung Jawab", 
                    $"Apakah Penanggung Jawabnya adalah Kepala Gudang ({selectedWarehouse.pic})?", 
                    "Ya", "Tidak");
                
                if (answer)
                {
                    FormPersonCharged.Text = selectedWarehouse.pic;
                }
            }
        }
    }

    private void HiddenDatePicker_DateSelected(object sender, DateChangedEventArgs e)
    {
        FormStartDate.Text = $"{e.NewDate:dd/MM/yyyy}";
    }
}

// Models
public class WarehouseResponse
{
    public string status { get; set; }
    public string message { get; set; }
    public List<WarehouseItem> data { get; set; }
}

public class WarehouseItem
{
    public string name { get; set; }
    public bool is_suspended { get; set; }
    public string pic { get; set; }
}

public class AksesResponse
{
    public List<AksesData> data { get; set; }
}

public class AksesData
{
    public string name { get; set; }
    public int id { get; set; }
    public List<AksesUser> userList { get; set; }
}

public class AksesUser
{
    public string email { get; set; }
}