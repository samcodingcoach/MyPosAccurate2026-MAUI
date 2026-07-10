using ZXing.Net.Maui;

namespace MyPosAccurate2026.Stok;

public partial class SO_ResultAdd_Detail : ContentPage
{
	public SO_ResultAdd_Detail()
	{
		InitializeComponent();
	}

    private void CameraScanner_BarcodesDetected(object sender, BarcodeDetectionEventArgs e)
    {
        // Logika pemindaian kode batang dapat ditambahkan di sini
    }

    private void BtnViewSerial_Clicked(object sender, EventArgs e)
    {
        ViewSerialInput.IsVisible = true;
    }

    private void BtnSimpan_Clicked(object sender, EventArgs e)
    {

    }
}