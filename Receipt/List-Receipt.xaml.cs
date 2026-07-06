using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;

namespace MyPosAccurate2026.Receipt;

public partial class List_Receipt : ContentPage
{
	string customerNo = string.Empty;
	string bankNo = string.Empty;

	// Nama cara bayar yang dipilih (filter lokal berdasarkan paymentMethodName di output). Kosong = semua
	string _caraBayarFilter = string.Empty;

	ObservableCollection<KonsumenItem> _konsumenList = new ObservableCollection<KonsumenItem>();
	ObservableCollection<KasBankItem> _caraBayarList = new ObservableCollection<KasBankItem>();
	bool _filterLoaded = false;

	// Data penerimaan
	ObservableCollection<ReceiptItem> _receiptList = new ObservableCollection<ReceiptItem>();
	List<ReceiptItem> _allReceipts = new List<ReceiptItem>();
	const int _limit = 50;
	int _currentPage = 1;
	bool _hasMoreData = true;
	bool _isFetchingReceipt = false;
	bool _uiReady = false;

	public List_Receipt()
	{
		InitializeComponent();
		CV_Konsumen.ItemsSource = _konsumenList;
		CV_CaraBayar.ItemsSource = _caraBayarList;
		CV_Receipt.ItemsSource = _receiptList;

		// Default: tampilkan data hari ini saja
		var today = DateTime.Today;
		DP_startdate.Date = today;
		DP_enddate.Date = today;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();

		if (!_filterLoaded)
		{
			_filterLoaded = true;
			await LoadKonsumen();
			await LoadCaraBayar();
			await LoadReceipt(true);
			_uiReady = true;
		}
	}

	private async Task LoadKonsumen()
	{
		string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();

		if (string.IsNullOrEmpty(cleanToken))
		{
			await DisplayAlertAsync("Sesi Habis", "Anda harus login kembali.", "OK");
			return;
		}

		try
		{
			using (var client = new HttpClient())
			{
				client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);

				string apiUrl = $"{App.API_HOST}pelanggan/list.php";
				var response = await client.GetAsync(apiUrl);
				var responseContent = await response.Content.ReadAsStringAsync();

				if (responseContent.StartsWith("<"))
				{
					await DisplayAlertAsync("Error Server", "Gagal membaca format data dari server.", "OK");
					return;
				}

				// Endpoint bisa mengembalikan array langsung atau dibungkus { status, data }
				List<KonsumenItem> items;
				if (responseContent.TrimStart().StartsWith("["))
				{
					items = JsonConvert.DeserializeObject<List<KonsumenItem>>(responseContent);
				}
				else
				{
					var wrapped = JsonConvert.DeserializeObject<KonsumenListResponse>(responseContent);
					items = wrapped?.data;
				}

				MainThread.BeginInvokeOnMainThread(() =>
				{
					_konsumenList.Clear();

					// Opsi "Semua" = tanpa filter konsumen, terpilih secara default
					_konsumenList.Add(new KonsumenItem { name = "Semua", customerNo = string.Empty, IsSelected = true });
					customerNo = string.Empty;

					if (items != null)
					{
						foreach (var item in items)
							_konsumenList.Add(item);
					}
				});
			}
		}
		catch (Exception ex)
		{
			await DisplayAlertAsync("Error", $"Gagal memuat data konsumen: {ex.Message}", "OK");
		}
	}

	private void OnKonsumenTapped(object sender, TappedEventArgs e)
	{
		if (sender is Border border && border.BindingContext is KonsumenItem item)
		{
			foreach (var k in _konsumenList)
				k.IsSelected = false;

			item.IsSelected = true;
			customerNo = item.customerNo;

			// Konsumen adalah parameter server → muat ulang data penerimaan
			if (_uiReady)
				_ = LoadReceipt(true);
		}
	}

	private async Task LoadCaraBayar()
	{
		string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();

		if (string.IsNullOrEmpty(cleanToken))
		{
			await DisplayAlertAsync("Sesi Habis", "Anda harus login kembali.", "OK");
			return;
		}

		try
		{
			using (var client = new HttpClient())
			{
				client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);

				string apiUrl = $"{App.API_HOST}coa/list-kasbank.php";
				var response = await client.GetAsync(apiUrl);
				var responseContent = await response.Content.ReadAsStringAsync();

				if (responseContent.StartsWith("<"))
				{
					await DisplayAlertAsync("Error Server", "Gagal membaca format data dari server.", "OK");
					return;
				}

				var apiResult = JsonConvert.DeserializeObject<KasBankListResponse>(responseContent);

				// Sembunyikan item dengan value "Kas Kecil" (sama seperti LoadKasBankData)
				var items = apiResult?.data?
					.Where(b => b.name != null && b.name.IndexOf("Kas Kecil", StringComparison.OrdinalIgnoreCase) < 0)
					.ToList();

				MainThread.BeginInvokeOnMainThread(() =>
				{
					_caraBayarList.Clear();

					// Opsi "Semua" = tanpa filter cara bayar, terpilih secara default
					_caraBayarList.Add(new KasBankItem { name = "Semua", no = string.Empty, IsSelected = true });
					bankNo = string.Empty;

					if (items != null)
					{
						foreach (var item in items)
							_caraBayarList.Add(item);
					}
				});
			}
		}
		catch (Exception ex)
		{
			await DisplayAlertAsync("Error", $"Gagal memuat data cara bayar: {ex.Message}", "OK");
		}
	}

	private void OnCaraBayarTapped(object sender, TappedEventArgs e)
	{
		if (sender is Border border && border.BindingContext is KasBankItem item)
		{
			foreach (var b in _caraBayarList)
				b.IsSelected = false;

			item.IsSelected = true;
			bankNo = item.no;

			// Cara bayar = filter lokal berdasarkan paymentMethodName (Semua = no kosong)
			_caraBayarFilter = string.IsNullOrEmpty(item.no) ? string.Empty : item.name;
			ApplyFilters();
		}
	}

	private async Task LoadReceipt(bool reset)
	{
		if (_isFetchingReceipt) return;

		if (reset)
		{
			_currentPage = 1;
			_hasMoreData = true;
			_allReceipts.Clear();
			_receiptList.Clear();
			MainThread.BeginInvokeOnMainThread(() => LoadingOverlay.IsVisible = true);
		}

		if (!_hasMoreData) return;

		_isFetchingReceipt = true;

		try
		{
			string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
			if (string.IsNullOrEmpty(cleanToken))
			{
				await DisplayAlertAsync("Sesi Habis", "Anda harus login kembali.", "OK");
				return;
			}

			string startDate = DP_startdate.Date.GetValueOrDefault(DateTime.Today).ToString("yyyy-MM-dd");
			string endDate = DP_enddate.Date.GetValueOrDefault(DateTime.Today).ToString("yyyy-MM-dd");

			var urlBuilder = new StringBuilder(
				$"{App.API_HOST}penerimaan-jual/list-receipt.php?start_date={startDate}&end_date={endDate}&limit={_limit}&page={_currentPage}");

			if (!string.IsNullOrEmpty(customerNo))
				urlBuilder.Append($"&customerNo={Uri.EscapeDataString(customerNo)}");

			using (var client = new HttpClient())
			{
				client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);

				var response = await client.GetAsync(urlBuilder.ToString());
				var responseContent = await response.Content.ReadAsStringAsync();

				if (responseContent.StartsWith("<"))
				{
					await DisplayAlertAsync("Error Server", "Gagal membaca format data dari server.", "OK");
					return;
				}

				var apiResult = JsonConvert.DeserializeObject<ReceiptListResponse>(responseContent);
				var data = apiResult?.data;

				if (data == null || data.Count == 0)
				{
					_hasMoreData = false;
				}
				else
				{
					_allReceipts.AddRange(data);

					// Tampilkan hanya item halaman baru yang lolos filter (append, agar posisi scroll terjaga)
					var newItems = FilterItems(data).ToList();
					MainThread.BeginInvokeOnMainThread(() =>
					{
						foreach (var r in newItems)
							_receiptList.Add(r);
					});

					if (data.Count < _limit) _hasMoreData = false;
					else _currentPage++;
				}
			}
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Gagal memuat data penerimaan: {ex.Message}");
		}
		finally
		{
			_isFetchingReceipt = false;
			MainThread.BeginInvokeOnMainThread(() => LoadingOverlay.IsVisible = false);
		}
	}

	// Terapkan filter lokal (cara bayar + pencarian nomor) terhadap sumber data
	private IEnumerable<ReceiptItem> FilterItems(IEnumerable<ReceiptItem> source)
	{
		IEnumerable<ReceiptItem> filtered = source;

		if (!string.IsNullOrEmpty(_caraBayarFilter))
			filtered = filtered.Where(r =>
				string.Equals(r.paymentMethodName, _caraBayarFilter, StringComparison.OrdinalIgnoreCase));

		string search = (T_Search.Text ?? "").Trim();
		if (!string.IsNullOrEmpty(search))
			filtered = filtered.Where(r =>
				r.number != null && r.number.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);

		return filtered;
	}

	// Bangun ulang daftar tampil dari seluruh data yang sudah dimuat (dipakai saat filter lokal berubah)
	private void ApplyFilters()
	{
		_receiptList.Clear();
		foreach (var r in FilterItems(_allReceipts))
			_receiptList.Add(r);
	}

	private void OnReceiptThresholdReached(object sender, EventArgs e)
	{
		if (_uiReady && _hasMoreData && !_isFetchingReceipt)
			_ = LoadReceipt(false);
	}

	private async void OnReceiptSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (e.CurrentSelection.FirstOrDefault() is ReceiptItem selectedItem)
		{
			((CollectionView)sender).SelectedItem = null;
			var page = new Detail_Receipt(selectedItem.number);
			page.HasHandle = true;
			page.HasBackdrop = true;
			page.ShowAsync(Window);
		}
	}

	private void OnFilterDateSelected(object sender, DateChangedEventArgs e)
	{
		// Tanggal adalah parameter server → muat ulang
		if (_uiReady)
			_ = LoadReceipt(true);
	}

	private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
	{
		// Pencarian nomor = filter lokal terhadap data yang sudah dimuat
		if (_uiReady)
			ApplyFilters();
	}

	public class ReceiptListResponse
	{
		public string status { get; set; }
		public List<ReceiptItem> data { get; set; }
	}

	public class ReceiptItem
	{
		public string number { get; set; }
		public double totalPayment { get; set; }
		public string charField2 { get; set; }
		public string transDate { get; set; }
		public string paymentMethodName { get; set; }
		public ReceiptCustomer customer { get; set; }

		public string FormattedTotalPayment => $"Rp {totalPayment.ToString("N0", new CultureInfo("id-ID"))}";
		public string PaymentMethodDisplay => (paymentMethodName ?? "").ToUpper();
		public string CustomerName => customer?.name ?? "-";

		// Ikon badge sesuai cara bayar
		public string PaymentMethodIcon
		{
			get
			{
				string method = paymentMethodName ?? "";
				if (method.IndexOf("QRIS", StringComparison.OrdinalIgnoreCase) >= 0)
					return "qris100white.png";
				if (method.IndexOf("Tunai", StringComparison.OrdinalIgnoreCase) >= 0)
					return "cash100white.png";
				return "bank100white.png";
			}
		}

		// Kasir: hilangkan awalan "1 - ", ambil teks setelah tanda "-"
		public string DisplayKasir
		{
			get
			{
				if (string.IsNullOrEmpty(charField2)) return "-";
				int idx = charField2.IndexOf('-');
				return idx >= 0 ? charField2.Substring(idx + 1).Trim() : charField2.Trim();
			}
		}
	}

	public class ReceiptCustomer
	{
		public string name { get; set; }
		public string customerNo { get; set; }
	}

	public class KonsumenListResponse
	{
		public string status { get; set; }
		public List<KonsumenItem> data { get; set; }
	}

	public class KasBankListResponse
	{
		public List<KasBankItem> data { get; set; }
	}

	public class KasBankItem : INotifyPropertyChanged
	{
		public string no { get; set; }
		public string name { get; set; }
		public int id { get; set; }

		bool _isSelected;
		public bool IsSelected
		{
			get => _isSelected;
			set
			{
				if (_isSelected != value)
				{
					_isSelected = value;
					PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
				}
			}
		}

		public event PropertyChangedEventHandler PropertyChanged;
	}

	public class KonsumenItem : INotifyPropertyChanged
	{
		public string name { get; set; }
		public int id { get; set; }
		public string customerNo { get; set; }

		bool _isSelected;
		public bool IsSelected
		{
			get => _isSelected;
			set
			{
				if (_isSelected != value)
				{
					_isSelected = value;
					PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
				}
			}
		}

		public event PropertyChangedEventHandler PropertyChanged;
	}
}
