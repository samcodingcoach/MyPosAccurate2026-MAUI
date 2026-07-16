using CommunityToolkit.Maui.Views;
using Newtonsoft.Json;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Collections.Generic;
namespace MyPosAccurate2026.Penyesuaian;

public partial class PopUpBarangSelected : Popup<AdjustmentPayloadItem>
{
	string _noItem;
    ItemDetailPayload _currentItem;
    bool _isUpdatingQuantity = false;
    List<string> _availableSerials = new List<string>();
    Action<AdjustmentPayloadItem> _onSimpan;

	public PopUpBarangSelected(string itemNo, Action<AdjustmentPayloadItem> onSimpan = null)
	{
		InitializeComponent();
		_noItem = itemNo;
        _onSimpan = onSimpan;
        _ = LoadItemData();
	}

    private async void BtnClose_Tapped(object sender, TappedEventArgs e)
    {
        await CloseAsync(null);
    }

    private async Task LoadItemData()
    {
        MainThread.BeginInvokeOnMainThread(() => OverlayLoading.IsVisible = true);
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
                        
                        if (_currentItem.manageSN)
                        {
                            await LoadSerialData();
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load item detail: {ex.Message}");
        }
        finally
        {
            MainThread.BeginInvokeOnMainThread(() => OverlayLoading.IsVisible = false);
        }
    }

    private async Task LoadSerialData()
    {
        try
        {
            string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
            string apiUrl = $"{App.API_HOST}item/serial_byNo.php?no={Uri.EscapeDataString(_noItem)}";

            using (var client = new HttpClient())
            {
                if (!string.IsNullOrEmpty(cleanToken))
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);

                client.DefaultRequestHeaders.Add("User-Agent", "AccuratePOS-App/1.0");

                var response = await client.GetAsync(apiUrl);
                if (response.IsSuccessStatusCode)
                {
                    string content = await response.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<SerialApiResponse>(content);
                    if (result?.data?.s == true && result.data.d != null)
                    {
                        _availableSerials.Clear();
                        foreach (var item in result.data.d)
                        {
                            if (item.serialNumber != null && !string.IsNullOrEmpty(item.serialNumber.number))
                            {
                                _availableSerials.Add(item.serialNumber.number);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load serials: {ex.Message}");
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
            
            if (_currentItem.manageSN)
            {
                if (_currentItem.SerialEntries == null) _currentItem.SerialEntries = new ObservableCollection<SerialEntryModel>();
                
                while (_currentItem.SerialEntries.Count < qty)
                {
                    _currentItem.SerialEntries.Add(new SerialEntryModel());
                }
                while (_currentItem.SerialEntries.Count > qty)
                {
                    _currentItem.SerialEntries.RemoveAt(_currentItem.SerialEntries.Count - 1);
                }
            }
        }
        else
        {
            TotalUnitCost.Text = "Rp 0";
            if (_currentItem.manageSN && _currentItem.SerialEntries != null)
            {
                _currentItem.SerialEntries.Clear();
            }
        }
    }

    private async void BSimpanTemp_Clicked(object sender, EventArgs e)
    {
        if (_currentItem == null) return;
        if (!int.TryParse(itemQuantity.Text, out int qty) || qty <= 0)
        {
            qty = 1;
        }

        string adjType = rbAdjustmentIn.IsChecked ? "ADJUSTMENT_IN" : "ADJUSTMENT_OUT";
        string typeDisp = rbAdjustmentIn.IsChecked ? "STOK IN" : "STOK OUT";
        
        var resultPayload = new AdjustmentPayloadItem
        {
            itemAdjustmentType = adjType,
            itemNo = _currentItem.no,
            itemName = _currentItem.name,
            quantity = qty,
            unitCost = _currentItem.vendorPrice,
            warehouseName = "Gudang Utama",
            detailNotes = "Hilang",
            image = _currentItem.DisplayImage,
            itemNoDisplay = $"No. {_currentItem.no}",
            qtyDisplay = $"{typeDisp} : {qty}",
            totalCostDisplay = $"Rp {(qty * _currentItem.vendorPrice):N0}"
        };

        if (_currentItem.manageSN && _currentItem.SerialEntries != null)
        {
            resultPayload.detailSerialNumber = new List<SerialPayload>();
            foreach(var sn in _currentItem.SerialEntries)
            {
                if (!string.IsNullOrWhiteSpace(sn.SerialNumber))
                {
                    resultPayload.detailSerialNumber.Add(new SerialPayload 
                    {
                        serialNumberNo = sn.SerialNumber,
                        quantity = 1
                    });
                }
            }
        }

        _onSimpan?.Invoke(resultPayload);
        await CloseAsync(null);
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

public class ItemDetailPayload : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    public double balance { get; set; }
    public ItemCategory itemCategory { get; set; }
    public double vendorPrice { get; set; }
    public string unit1Name { get; set; }
    public string name { get; set; }
    public string no { get; set; }
    public bool manageSN { get; set; }

    private ObservableCollection<SerialEntryModel> _serialEntries = new ObservableCollection<SerialEntryModel>();
    public ObservableCollection<SerialEntryModel> SerialEntries
    {
        get => _serialEntries;
        set 
        { 
            _serialEntries = value; 
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SerialEntries))); 
        }
    }

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

public class SerialEntryModel : INotifyPropertyChanged
{
    private string _serialNumber;
    public string SerialNumber 
    { 
        get => _serialNumber; 
        set 
        { 
            _serialNumber = value; 
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SerialNumber))); 
        } 
    }
    public event PropertyChangedEventHandler PropertyChanged;
}

public class SerialApiResponse
{
    public SerialApiData data { get; set; }
}

public class SerialApiData
{
    public bool s { get; set; }
    public List<SerialData> d { get; set; }
}

public class SerialData
{
    public SerialNumberObj serialNumber { get; set; }
    public double quantity { get; set; }
}

public class SerialNumberObj
{
    public string number { get; set; }
}