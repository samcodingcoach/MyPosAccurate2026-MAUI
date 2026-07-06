using System.Net.Http.Headers;
using Newtonsoft.Json.Linq;
using The49.Maui.BottomSheet;
namespace MyPosAccurate2026.Sales;

public partial class Detail_Faktur2 : BottomSheet
{
    private string _invoiceNo;
    private string _receiptNo;

    public Detail_Faktur2(string invoiceNo)
    {
        InitializeComponent();
        _invoiceNo = invoiceNo;
        LoadDetailPembayaran();
    }

   

    private async Task LoadDetailPembayaran()
    {
        try
        {
            string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);

            // 1. Fetch Detail Invoice
            string invoiceUrl = $"{App.API_HOST}penjualan/detail-invoice.php?number={_invoiceNo}";
            var invResponse = await client.GetStringAsync(invoiceUrl);
            var invData = JObject.Parse(invResponse);

            var receiptHistoryArray = invData["data"]?["receiptHistory"] as JArray;
            if (receiptHistoryArray == null || receiptHistoryArray.Count == 0)
            {
                MainThread.BeginInvokeOnMainThread(async () => 
                {
                    await this.DismissAsync();
                });
                return;
            }

            // Ambil history pertama
            var history = receiptHistoryArray[0];
            string historyNumber = history["historyNumber"]?.ToString();
            _receiptNo = historyNumber;
            string historyDateTime = history["historyDateTime"]?.ToString();
            string historyPaymentName = history["historyPaymentName"]?.ToString();

            if (string.IsNullOrEmpty(historyNumber))
            {
                MainThread.BeginInvokeOnMainThread(async () => 
                {
                    await this.DismissAsync();
                });
                return;
            }

            // 2. Fetch Detail Receipt
            string receiptUrl = $"{App.API_HOST}penjualan/detail-receipt.php?number={historyNumber}";
            var recResponse = await client.GetStringAsync(receiptUrl);
            var recData = JObject.Parse(recResponse);
            var rData = recData["data"];

            if (rData == null)
            {
                MainThread.BeginInvokeOnMainThread(async () => 
                {
                    await this.DismissAsync();
                });
                return;
            }

            double totalPayment = rData["totalPayment"]?.Value<double>() ?? 0;
            string bankName = rData["bank"]?["name"]?.ToString();
            if (string.IsNullOrWhiteSpace(bankName)) bankName = "-";
            
            string charField2 = rData["charField2"]?.ToString() ?? "";

            // Ekstrak Kasir (contoh "1 - Administrator" -> "Administrator")
            string kasir = charField2;
            if (!string.IsNullOrEmpty(charField2) && charField2.Contains("-"))
            {
                var parts = charField2.Split(new[] { '-' }, 2);
                if (parts.Length == 2)
                    kasir = parts[1].Trim();
            }

            // Update UI
            MainThread.BeginInvokeOnMainThread(() =>
            {
                LoadingIndicator.IsRunning = false;
                LoadingIndicator.IsVisible = false;

                LblNoFaktur.Text = _invoiceNo;
                LblTanggal.Text = historyDateTime ?? "-";
                LblNoPembayaran.Text = historyNumber;
                LblKasir.Text = string.IsNullOrEmpty(kasir) ? "-" : kasir;
                LblMetode.Text = historyPaymentName ?? "-";
                LblBank.Text = bankName;
                LblTotal.Text = $"Rp {totalPayment:N0}";

                DetailContainer.IsVisible = true;
            });
        }
        catch (Exception ex)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await this.DismissAsync();
            });
        }
    }

    private async void TapPrintExport_Tapped(object sender, TappedEventArgs e)
    {

        if (sender is StackLayout press)
        {
            await press.FadeToAsync(0.3, 100); // Turunkan opacity ke 0.3 dalam 100ms
            await press.FadeToAsync(1, 200);   // Kembalikan opacity ke 1 dalam 200ms
        }
            await this.DismissAsync();

        // Buka halaman Print menggunakan instance Print() yang menerima receiptNumber dan invoiceNumber
        if (Application.Current?.MainPage?.Navigation != null)
        {
            await Application.Current.MainPage.Navigation.PushAsync(new Print(_receiptNo, _invoiceNo));
        }
    }
}