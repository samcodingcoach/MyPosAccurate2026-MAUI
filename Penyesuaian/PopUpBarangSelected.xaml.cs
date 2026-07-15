using CommunityToolkit.Maui.Views;
using Newtonsoft.Json;
using System.Net.Http.Headers;
using System.Threading.Tasks;
namespace MyPosAccurate2026.Penyesuaian;

public partial class PopUpBarangSelected : Popup
{
	string _noItem;
    ItemDetailPayload _currentItem;
    bool _isUpdatingQuantity = false;

	public PopUpBarangSelected(string itemNo)
	{
		InitializeComponent();
		_noItem = itemNo;
        _ = LoadItemData();
	}

    private async void BtnClose_Tapped(object sender, TappedEventArgs e)
    {
        await CloseAsync();
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
                        _currentItem = result.data.d;
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            BindingContext = _currentItem;
                            UpdateTotalCost();
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

    private void Minus_Tapped(object sender, TappedEventArgs e)
    {
        if (int.TryParse(itemQuantity.Text, out int qty))
        {
            if (qty > 0)
            {
                qty--;
                itemQuantity.Text = qty.ToString();
            }
        }
        else
        {
            itemQuantity.Text = "0";
        }
    }

    private void Plus_Tapped(object sender, TappedEventArgs e)
    {
        if (int.TryParse(itemQuantity.Text, out int qty))
        {
            qty++;
            itemQuantity.Text = qty.ToString();
        }
        else
        {
            itemQuantity.Text = "1";
        }
    }

    private void itemQuantity_TextChanged(object sender, TextChangedEventArgs e)
    {
        CheckStockLimit();
        UpdateTotalCost();
    }

    private void rbAdjustment_CheckedChanged(object sender, CheckedChangedEventArgs e)
    {
        CheckStockLimit();
    }

    private void CheckStockLimit()
    {
        if (_currentItem == null || _isUpdatingQuantity) return;

        if (rbAdjustmentOut != null && rbAdjustmentOut.IsChecked)
        {
            if (int.TryParse(itemQuantity.Text, out int qty))
            {
                if (qty > _currentItem.balance)
                {
                    _isUpdatingQuantity = true;
                    itemQuantity.Text = _currentItem.balance.ToString();
                    _isUpdatingQuantity = false;
                }
            }
        }
    }

    private void UpdateTotalCost()
    {
        if (_currentItem == null) return;

        if (int.TryParse(itemQuantity.Text, out int qty))
        {
            double total = qty * _currentItem.vendorPrice;
            TotalUnitCost.Text = $"Rp {total:N0}";
        }
        else
        {
            TotalUnitCost.Text = "Rp 0";
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