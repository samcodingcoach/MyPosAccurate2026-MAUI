using Newtonsoft.Json;
using System.Globalization;
using System.Net.Http.Headers;
using The49.Maui.BottomSheet;

namespace MyPosAccurate2026.Receipt;

public partial class Detail_Receipt : BottomSheet
{
	private string _number;

	public Detail_Receipt(string number = "")
	{
		InitializeComponent();
		_number = number;
		if (!string.IsNullOrEmpty(_number))
		{
			_ = LoadDetail();
		}
	}

	private async Task LoadDetail()
	{
		string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
		if (string.IsNullOrEmpty(cleanToken))
		{
			if (Application.Current?.MainPage != null)
				await Application.Current.MainPage.DisplayAlertAsync("Sesi Habis", "Anda harus login kembali.", "OK");
			MainThread.BeginInvokeOnMainThread(async () => await this.DismissAsync());
			return;
		}

		try
		{
			using (var client = new HttpClient())
			{
				client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);

				string apiUrl = $"{App.API_HOST}penjualan/detail-receipt.php?number={_number}";
				var response = await client.GetAsync(apiUrl);
				var responseContent = await response.Content.ReadAsStringAsync();

				if (responseContent.StartsWith("<"))
				{
					if (Application.Current?.MainPage != null)
						await Application.Current.MainPage.DisplayAlertAsync("Error Server", "Gagal membaca format data dari server.", "OK");
					MainThread.BeginInvokeOnMainThread(async () => await this.DismissAsync());
					return;
				}

				var apiResult = JsonConvert.DeserializeObject<DetailReceiptResponse>(responseContent);
				if (apiResult?.data != null)
				{
					MainThread.BeginInvokeOnMainThread(() =>
					{
						BindingContext = apiResult.data;
						LoadingIndicator.IsRunning = false;
						LoadingIndicator.IsVisible = false;
						DetailContainer.IsVisible = true;
					});
				}
			}
		}
		catch (Exception ex)
		{
			if (Application.Current?.MainPage != null)
				await Application.Current.MainPage.DisplayAlertAsync("Error", $"Gagal memuat detail: {ex.Message}", "OK");
			MainThread.BeginInvokeOnMainThread(async () => await this.DismissAsync());
		}
	}

	private async void TapPrintExport_Tapped(object sender, TappedEventArgs e)
	{
		if (sender is StackLayout press)
		{
			await press.FadeToAsync(0.3, 100);
			await press.FadeToAsync(1, 200);
		}
		else if (sender is View view)
		{
			await view.FadeToAsync(0.3, 100);
			await view.FadeToAsync(1, 200);
		}

		string invoiceNo = "";
		if (BindingContext is DetailReceiptData data)
		{
			invoiceNo = data.InvoiceNumber;
			if (invoiceNo == "-") invoiceNo = "";
		}

		await this.DismissAsync();
		if (Application.Current?.MainPage?.Navigation != null)
		{
			await Application.Current.MainPage.Navigation.PushAsync(new MyPosAccurate2026.Sales.Print(_number, invoiceNo));
		}
	}
}

public class DetailReceiptResponse
{
	public DetailReceiptData data { get; set; }
}

public class DetailReceiptData
{
	public string number { get; set; }
	public double totalPayment { get; set; }
	public string charField2 { get; set; }
	public string charField1 { get; set; }
	public List<DetailReceiptInvoice> detailInvoice { get; set; }
	public double totalDiscount { get; set; }
	public string paymentMethod { get; set; }
	public string description { get; set; }
	public string transDate { get; set; }
	public string charField3 { get; set; }
	public DetailReceiptCustomer customer { get; set; }

	public string FormattedTotalPayment => $"Rp {totalPayment.ToString("N0", new CultureInfo("id-ID"))}";
	public string DisplayKasir
	{
		get
		{
			if (string.IsNullOrEmpty(charField2)) return "-";
			int idx = charField2.IndexOf('-');
			return idx >= 0 ? charField2.Substring(idx + 1).Trim() : charField2.Trim();
		}
	}
	public string DisplayCustomer => customer?.name ?? "-";
	public string QrisOrVaLabel
	{
		get
		{
			if (!string.IsNullOrEmpty(charField3)) return "VA";
			return "QRIS";
		}
	}

	public string QrisOrVa
	{
		get
		{
			if (!string.IsNullOrEmpty(charField3)) return charField3;
			if (!string.IsNullOrEmpty(charField1)) return charField1;
			return "-";
		}
	}

	public string InvoiceNumber => detailInvoice?.FirstOrDefault()?.invoice?.number ?? "-";

	public List<DetailReceiptDiscount> Discounts => detailInvoice?.FirstOrDefault()?.detailDiscount ?? new List<DetailReceiptDiscount>();
	
	public string FormattedGrandTotal => $"Rp {totalPayment.ToString("N0", new CultureInfo("id-ID"))}";

	public double SubTotal => totalPayment + totalDiscount;
	public string FormattedSubTotal => $"Rp {SubTotal.ToString("N0", new CultureInfo("id-ID"))}";

	public string DescriptionDisplay => string.IsNullOrEmpty(description) ? "-" : description;
}

public class DetailReceiptInvoice
{
	public InvoiceInfo invoice { get; set; }
	public List<DetailReceiptDiscount> detailDiscount { get; set; }
}

public class InvoiceInfo
{
	public string number { get; set; }
	public string status { get; set; }
}

public class DetailReceiptDiscount
{
	public double amount { get; set; }
	public DiscountAccount account { get; set; }

	public string FormattedAmount => $"Rp {amount.ToString("N0", new CultureInfo("id-ID"))}";
	public string AccountName => account?.name ?? "-";
}

public class DiscountAccount
{
	public string name { get; set; }
}

public class DetailReceiptCustomer
{
	public string name { get; set; }
	public string customerNo { get; set; }
}