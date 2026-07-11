using ZXing.Net.Maui;
using System.Collections.ObjectModel;
using Newtonsoft.Json;

namespace MyPosAccurate2026.Stok;

public partial class SO_ResultAdd_Detail : ContentPage
{
    private string _expectedItemNo = "";
    private string _orderNumber = "";
    private string _statusName = "";
    private ObservableCollection<SOSerialNumberDetail> _serialList;

	public SO_ResultAdd_Detail()
	{
		InitializeComponent();
	}

    public SO_ResultAdd_Detail(SODetailItem pItem, string orderNumber, string statusName)
    {
        InitializeComponent();
        
        _orderNumber = orderNumber;
        _statusName = statusName;
        _expectedItemNo = pItem.item.no;
        itemNamaBarang.Text = pItem.item.name;
        itemNo.Text = $"No. {pItem.item.no}";
        
        string baseHost = App.API_HOST.Replace("api/", "");
        itemImage.Source = $"{baseHost}images/{pItem.item.no}.jpg";

        if (pItem.HasSerialNumber)
        {
            ViewSerialInput.IsVisible = true;
            _serialList = new ObservableCollection<SOSerialNumberDetail>(pItem.detailSerialNumber);
            CV_Serial.ItemsSource = _serialList;
        }
        else
        {
            ViewSerialInput.IsVisible = false;
        }
    }

    private void BtnDeleteSerial_Tapped(object sender, TappedEventArgs e)
    {
        if (sender is Image image && image.BindingContext is SOSerialNumberDetail selectedSerial)
        {
            _serialList.Remove(selectedSerial);
        }
    }

    private void CameraScanner_BarcodesDetected(object sender, BarcodeDetectionEventArgs e)
    {
        var results = e.Results;
        if (results != null && results.Any())
        {
            string scannedResult = results[0].Value;
            
            MainThread.BeginInvokeOnMainThread(() =>
            {
                LblScanStatus.IsVisible = true;
                if (scannedResult == _expectedItemNo)
                {
                    LblScanStatus.Text = "COCOK / SESUAI";
                    LblScanStatus.BackgroundColor = Colors.DarkGreen;
                    
                    BtnViewSerial.IsEnabled = true;
                    BtnSimpan.IsEnabled = true;
                }
                else
                {
                    LblScanStatus.Text = $"TIDAK SESUAI ({scannedResult})";
                    LblScanStatus.BackgroundColor = Colors.DarkRed;
                    
                    BtnViewSerial.IsEnabled = false;
                    BtnSimpan.IsEnabled = false;
                }
            });
        }
    }

    private void BtnViewSerial_Clicked(object sender, EventArgs e)
    {
        ViewSerialInput.IsVisible = true;
    }

    private async void BtnSimpan_Clicked(object sender, EventArgs e)
    {
        double qty = 0;
        if (!double.TryParse(EntryQuantity.Text, out qty) || qty < 0)
        {
            await DisplayAlertAsync("Error", "Kuantitas tidak valid.", "OK");
            return;
        }

        var detailItem = new SOSaveDetailItem
        {
            itemNo = _expectedItemNo,
            quantity = qty
        };

        if (_serialList != null && _serialList.Count > 0)
        {
            detailItem.detailSerialNumber = _serialList.Select(s => new SOSaveSerialNumber
            {
                serialNumberNo = s.serialNumber.number,
                quantity = s.quantity
            }).ToList();
        }

        var payload = new SOSavePayload
        {
            orderNumber = _orderNumber,
            transDate = DateTime.Now.ToString("dd/MM/yyyy"),
            detailItem = new List<SOSaveDetailItem> { detailItem }
        };

        try
        {
            string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", cleanToken);

            string apiUrl = $"{App.API_HOST}stokopname-result/save.php";
            string json = JsonConvert.SerializeObject(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            MainThread.BeginInvokeOnMainThread(() => {
                LoadingIndicator.IsVisible = true;
                LoadingIndicator.IsRunning = true;
            });
            
            var response = await client.PostAsync(apiUrl, content);
            
            if (response.IsSuccessStatusCode)
            {
                var responseStr = await response.Content.ReadAsStringAsync();
                await DisplayAlertAsync("Sukses", "Data berhasil disimpan.", "OK");
                await Navigation.PopAsync();
            }
            else
            {
                string err = await response.Content.ReadAsStringAsync();
                await DisplayAlertAsync("Gagal", $"Error {(int)response.StatusCode}: {err}", "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", $"Terjadi kesalahan: {ex.Message}", "OK");
        }
        finally
        {
            MainThread.BeginInvokeOnMainThread(() => {
                LoadingIndicator.IsVisible = false;
                LoadingIndicator.IsRunning = false;
            });
        }
    }
}

// ===== Save DTOs =====
public class SOSavePayload
{
    public string orderNumber { get; set; }
    public string transDate { get; set; }
    public List<SOSaveDetailItem> detailItem { get; set; }
}

public class SOSaveDetailItem
{
    public string itemNo { get; set; }
    public double quantity { get; set; }
    
    [JsonProperty("detailSerialNumber", NullValueHandling = NullValueHandling.Ignore)]
    public List<SOSaveSerialNumber> detailSerialNumber { get; set; }
}

public class SOSaveSerialNumber
{
    public string serialNumberNo { get; set; }
    public double quantity { get; set; }
}