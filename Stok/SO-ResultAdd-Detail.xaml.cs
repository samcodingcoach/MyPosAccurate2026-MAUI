using ZXing.Net.Maui;
using System.Collections.ObjectModel;

namespace MyPosAccurate2026.Stok;

public partial class SO_ResultAdd_Detail : ContentPage
{
    private string _expectedItemNo = "";
    private ObservableCollection<SOSerialNumberDetail> _serialList;

	public SO_ResultAdd_Detail()
	{
		InitializeComponent();
	}

    public SO_ResultAdd_Detail(SODetailItem pItem)
    {
        InitializeComponent();
        
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

    private void BtnSimpan_Clicked(object sender, EventArgs e)
    {

    }
}