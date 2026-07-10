using ZXing.Net.Maui;

namespace MyPosAccurate2026.Stok;

public partial class SO_ResultAdd_Detail : ContentPage
{
    private string _expectedItemNo = "";

	public SO_ResultAdd_Detail()
	{
		InitializeComponent();
	}

    public SO_ResultAdd_Detail(string pItemName, string pItemNo)
    {
        InitializeComponent();
        
        _expectedItemNo = pItemNo;
        itemNamaBarang.Text = pItemName;
        itemNo.Text = $"No. {pItemNo}";
        
        string baseHost = App.API_HOST.Replace("api/", "");
        itemImage.Source = $"{baseHost}images/{pItemNo}.jpg";
    }

    private void CameraScanner_BarcodesDetected(object sender, BarcodeDetectionEventArgs e)
    {
        var results = e.Results;
        if (results != null && results.Any())
        {
            string scannedResult = results[0].Value;
            
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (scannedResult == _expectedItemNo)
                {
                    BtnViewSerial.IsEnabled = true;
                    BtnSimpan.IsEnabled = true;
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