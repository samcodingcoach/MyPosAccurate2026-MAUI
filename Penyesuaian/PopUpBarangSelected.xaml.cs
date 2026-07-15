using CommunityToolkit.Maui.Views;
using Newtonsoft.Json;
using System.Net.Http.Headers;
using System.Threading.Tasks;
namespace MyPosAccurate2026.Penyesuaian;

public partial class PopUpBarangSelected : Popup
{
	string _noItem;
	public PopUpBarangSelected(string itemNo)
	{
		InitializeComponent();
		_noItem = itemNo;
        _ = LoadItemData();
	}

    private async Task LoadItemData()
    {
        try
        {
            string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
            string apiUrl = $"{App.API_HOST}item/detail_byNo.php?no={Uri.EscapeDataString(_noItem)}";

            using (var client = new HttpClient())
            {
                if (!string.IsNullOrEmpty(cleanToken))
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);

                client.DefaultRequestHeaders.Add("User-Agent", "AccuratePOS-App/1.0");

                var response = await client.GetAsync(apiUrl);
                if (response.IsSuccessStatusCode)
                {
                    string content = await response.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<ItemDetailResponse>(content);
                    
                    if (result?.data?.s == true && result.data.d != null)
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            BindingContext = result.data.d;
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load item detail: {ex.Message}");
        }
    }
}

public class ItemDetailResponse
{
    public ItemDetailData data { get; set; }
}

public class ItemDetailData
{
    public bool s { get; set; }
    public ItemDetailPayload d { get; set; }
}

public class ItemDetailPayload
{
    public double balance { get; set; }
    public ItemCategory itemCategory { get; set; }
    public double vendorPrice { get; set; }
    public string unit1Name { get; set; }
    public string name { get; set; }
    public string no { get; set; }
    public bool manageSN { get; set; }

    public string DisplayImage
    {
        get
        {
            string imagePath = no + ".jpg";
            string baseHost = App.API_HOST.Replace("api/", "");
            return $"{baseHost}images/{imagePath}";
        }
    }

    public string FormattedVendorPrice => $"Rp {vendorPrice:N0}";
    public string Subtext => $"No. {no}{(itemCategory != null && !string.IsNullOrEmpty(itemCategory.name) ? " - " + itemCategory.name : "")}";
    public string FormattedBalance => $"{balance:N0} {unit1Name}";
}

public class ItemCategory
{
    public string name { get; set; }
}