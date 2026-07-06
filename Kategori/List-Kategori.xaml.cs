using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.Http.Headers;
using System.Threading;
using Newtonsoft.Json;

namespace MyPosAccurate2026.Kategori;

public partial class List_Kategori : ContentPage
{
	// Seluruh data kategori (flat) yang sudah dimuat dari server
	List<KategoriItem> _allCategories = new List<KategoriItem>();

	// Data tampil: dikelompokkan per induk (parent)
	ObservableCollection<KategoriGroup> _groups = new ObservableCollection<KategoriGroup>();
	Dictionary<int, KategoriGroup> _groupMap = new Dictionary<int, KategoriGroup>();

	const int _limit = 100;
	int _currentPage = 1;
	bool _hasMoreData = true;
	bool _isFetching = false;
	bool _loaded = false;
	bool _uiReady = false;

	int? _editingCategoryId = null;
	CancellationTokenSource _searchCts;

	public List_Kategori()
	{
		InitializeComponent();
		CV_Kategori.ItemsSource = _groups;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();

		if (!_loaded)
		{
			_loaded = true;
			await LoadKategori(true);
			await LoadParentKategori();
			_uiReady = true;
		}
	}

	private async Task LoadKategori(bool reset, bool showLoading = true)
	{
		if (_isFetching) return;

		if (reset)
		{
			_currentPage = 1;
			_hasMoreData = true;
			_allCategories.Clear();
			MainThread.BeginInvokeOnMainThread(() =>
			{
				_groups.Clear();
				_groupMap.Clear();
				if (showLoading) LoadingOverlay.IsVisible = true;
			});
		}

		if (!_hasMoreData)
		{
			MainThread.BeginInvokeOnMainThread(() => LoadingOverlay.IsVisible = false);
			return;
		}

		_isFetching = true;

		try
		{
			string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
			if (string.IsNullOrEmpty(cleanToken))
			{
				await DisplayAlertAsync("Sesi Habis", "Anda harus login kembali.", "OK");
				return;
			}

			using (var client = new HttpClient())
			{
				client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);

				string search = (T_Search.Text ?? "").Trim();
				string apiUrl = $"{App.API_HOST}item-category/list.php?limit={_limit}&page={_currentPage}";
				
				if (!string.IsNullOrEmpty(search))
				{
					apiUrl += $"&search={Uri.EscapeDataString(search)}";
				}

				var response = await client.GetAsync(apiUrl);
				var responseContent = await response.Content.ReadAsStringAsync();

				if (responseContent.StartsWith("<"))
				{
					await DisplayAlertAsync("Error Server", "Gagal membaca format data dari server.", "OK");
					return;
				}

				// Endpoint bisa mengembalikan array langsung atau dibungkus { status, data }
				List<KategoriItem> data;
				if (responseContent.TrimStart().StartsWith("["))
				{
					data = JsonConvert.DeserializeObject<List<KategoriItem>>(responseContent);
				}
				else
				{
					var wrapped = JsonConvert.DeserializeObject<KategoriListResponse>(responseContent);
					data = wrapped?.data;
				}

				if (data == null || data.Count == 0)
				{
					_hasMoreData = false;
				}
				else
				{
					_allCategories.AddRange(data);

					// Gabungkan hanya data halaman baru ke dalam grup (append, agar posisi scroll terjaga)
					MainThread.BeginInvokeOnMainThread(() => AppendItems(data));

					if (data.Count < _limit) _hasMoreData = false;
					else _currentPage++;
				}
			}
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Gagal memuat data kategori: {ex.Message}");
		}
		finally
		{
			_isFetching = false;
			MainThread.BeginInvokeOnMainThread(() => LoadingOverlay.IsVisible = false);
		}
	}

	private async Task LoadParentKategori()
	{
		try
		{
			string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
			if (string.IsNullOrEmpty(cleanToken)) return;

			using (var client = new HttpClient())
			{
				client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);

				int page = 1;
				bool hasMore = true;
				List<KategoriItem> allParents = new List<KategoriItem>();

				while (hasMore)
				{
					string apiUrl = $"{App.API_HOST}item-category/list.php?limit=100&page={page}";
					var response = await client.GetAsync(apiUrl);
					var responseContent = await response.Content.ReadAsStringAsync();

					List<KategoriItem> data = new List<KategoriItem>();
					if (responseContent.TrimStart().StartsWith("["))
					{
						data = JsonConvert.DeserializeObject<List<KategoriItem>>(responseContent);
					}
					else
					{
						var wrapped = JsonConvert.DeserializeObject<KategoriListResponse>(responseContent);
						if (wrapped?.data != null) data = wrapped.data;
					}

					if (data != null && data.Count > 0)
					{
						var parents = data.Where(k => k.lvl == 1 && !k.is_sub).ToList();
						allParents.AddRange(parents);
						
						if (data.Count < 100) hasMore = false;
						else page++;
					}
					else
					{
						hasMore = false;
					}
				}

				MainThread.BeginInvokeOnMainThread(() =>
				{
					PickerParentKategori.ItemsSource = allParents;
				});
			}
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Gagal memuat parent kategori: {ex.Message}");
		}
	}

	// Gabungkan sekumpulan item ke dalam grup induk, dengan menghormati filter pencarian aktif
	private void AppendItems(IEnumerable<KategoriItem> items)
	{
		string search = (T_Search.Text ?? "").Trim();

		foreach (var item in items)
		{
			if (!string.IsNullOrEmpty(search) && !MatchSearch(item, search))
				continue;

			MergeOne(item);
		}
	}

	// Tempatkan satu item ke grup induknya (membuat grup baru bila belum ada)
	private void MergeOne(KategoriItem item)
	{
		// Kunci grup = id induk. Untuk induk (lvl 1) pakai id-nya sendiri, untuk sub pakai parent_id.
		int key = item.is_sub ? (item.parent_id ?? -1) : item.id;
		string headerName = item.is_sub ? item.parent_name : item.name;

		if (!_groupMap.TryGetValue(key, out var grp))
		{
			grp = new KategoriGroup { Id = key, Name = headerName };
			_groupMap[key] = grp;
			_groups.Add(grp);
		}

		if (!item.is_sub)
		{
			// Pastikan nama induk benar saat record induk yang sebenarnya termuat
			grp.Name = item.name;
		}
		else if (!grp.Children.Any(c => c.id == item.id))
		{
			grp.Children.Add(item);
		}
	}

	private bool MatchSearch(KategoriItem item, string search)
	{
		if (item.name != null && item.name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
			return true;

		return item.id.ToString().IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
	}

	// Bangun ulang seluruh grup dari data yang sudah dimuat (dipakai saat pencarian berubah)
	private void ApplyFilters()
	{
		_groups.Clear();
		_groupMap.Clear();
		AppendItems(_allCategories);
	}

	private async void OnRefreshing(object sender, EventArgs e)
	{
		await LoadKategori(true, showLoading: false);
		RV_Kategori.IsRefreshing = false;
	}

	private void OnThresholdReached(object sender, EventArgs e)
	{
		if (_uiReady && _hasMoreData && !_isFetching)
			_ = LoadKategori(false);
	}

	// Tap header induk = edit kategori induk
	private void OnGroupHeaderTapped(object sender, TappedEventArgs e)
	{
		if (sender is StackLayout sl && sl.BindingContext is KategoriGroup grp)
		{
			_editingCategoryId = grp.Id;
			FormTitle.Text = "Edit Kategori Induk";
			FormNamaKategori.Text = grp.Name;
			PickerParentKategori.SelectedItem = null;
			PickerParentKategori.IsEnabled = false; // Karena ini induk, tidak bisa ubah parent di sini (opsional)

			FormInput.IsVisible = true;
		}
	}

	// Tap "Edit" pada sub kategori = edit kategori
	private void OnEditTapped(object sender, TappedEventArgs e)
	{
		if (sender is Label lbl && lbl.BindingContext is KategoriItem item)
		{
			_editingCategoryId = item.id;
			FormTitle.Text = "Edit Sub Kategori";
			FormNamaKategori.Text = item.name;
			
			PickerParentKategori.IsEnabled = true;
			if (PickerParentKategori.ItemsSource is List<KategoriItem> parentList && !string.IsNullOrEmpty(item.parent_name))
			{
				PickerParentKategori.SelectedItem = parentList.FirstOrDefault(p => p.name == item.parent_name);
			}
			else
			{
				PickerParentKategori.SelectedItem = null;
			}

			FormInput.IsVisible = true;
		}
	}

	private async void T_Search_TextChanged(object sender, TextChangedEventArgs e)
	{
		if (!_uiReady) return;

		// Client-side filtering first for fast UI response
		ApplyFilters();

		// Debounce server request
		if (_searchCts != null)
			_searchCts.Cancel();
			
		_searchCts = new CancellationTokenSource();
		try
		{
			await Task.Delay(500, _searchCts.Token);
			await LoadKategori(true, showLoading: true);
		}
		catch (TaskCanceledException)
		{
			// Ignored if cancelled
		}
	}

	private void TapAdd_Tapped(object sender, TappedEventArgs e)
	{
		_editingCategoryId = null;
		FormTitle.Text = "Kategori Baru";
		FormNamaKategori.Text = "";
		PickerParentKategori.SelectedItem = null;
		PickerParentKategori.IsEnabled = true;
		
		FormInput.IsVisible = true;
	}

	private void BBatal_Clicked(object sender, EventArgs e)
	{
		FormInput.IsVisible = false;
	}

	public class KategoriListResponse
	{
		public string status { get; set; }
		public List<KategoriItem> data { get; set; }
	}

	public class KategoriItem
	{
		public int id { get; set; }
		public string name { get; set; }
		public int lvl { get; set; }
		public int? parent_id { get; set; }
		public string parent_name { get; set; }
		public bool is_sub { get; set; }
	}

	// Grup induk + daftar sub kategori
	public class KategoriGroup : INotifyPropertyChanged
	{
		public int Id { get; set; }

		string _name;
		public string Name
		{
			get => _name;
			set
			{
				if (_name != value)
				{
					_name = value;
					PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
				}
			}
		}

		public ObservableCollection<KategoriItem> Children { get; } = new ObservableCollection<KategoriItem>();

		public event PropertyChangedEventHandler PropertyChanged;
	}

    private async void BSimpan_Clicked(object sender, EventArgs e)
    {
        string namaKategori = FormNamaKategori.Text?.Trim();
        
        if (string.IsNullOrEmpty(namaKategori))
        {
            await DisplayAlert("Peringatan", "Nama kategori barang harus diisi.", "OK");
            return;
        }

        string parentName = "";
        if (PickerParentKategori.SelectedItem is KategoriItem selectedParent)
        {
            parentName = selectedParent.name;
        }

        BSimpan.IsEnabled = false;

        try
        {
            string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
            if (string.IsNullOrEmpty(cleanToken))
            {
                await DisplayAlert("Sesi Habis", "Anda harus login kembali.", "OK");
                return;
            }

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);

                bool isEdit = _editingCategoryId.HasValue;
                string apiUrl = isEdit ? $"{App.API_HOST}item-category/update.php" : $"{App.API_HOST}item-category/save.php";

                object payload;
                if (isEdit)
                {
                    payload = new
                    {
                        id = _editingCategoryId.Value.ToString(),
                        name = namaKategori,
                        parentName = parentName
                    };
                }
                else
                {
                    payload = new
                    {
                        name = namaKategori,
                        parentName = parentName
                    };
                }

                string jsonPayload = JsonConvert.SerializeObject(payload);
                var content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");

                var response = await client.PostAsync(apiUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    await DisplayAlert("Sukses", isEdit ? "Kategori berhasil diperbarui." : "Kategori berhasil disimpan.", "OK");
                    
                    // Reset input
                    _editingCategoryId = null;
                    FormNamaKategori.Text = "";
                    PickerParentKategori.SelectedItem = null;
                    
                    // Sembunyikan form input
                    FormInput.IsVisible = false;

                    // Refresh data
                    await LoadKategori(true);
                    await LoadParentKategori();
                }
                else
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    await DisplayAlert("Gagal", $"Gagal menyimpan kategori. Status: {response.StatusCode}\n{responseContent}", "OK");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Gagal menyimpan kategori: {ex.Message}");
            await DisplayAlert("Error", "Terjadi kesalahan saat menyimpan data.", "OK");
        }
        finally
        {
            BSimpan.IsEnabled = true;
        }
    }

    private void T_Search_TextChanged_1(object sender, TextChangedEventArgs e)
    {

    }

    private void TapAdd_Tapped_1(object sender, TappedEventArgs e)
    {

    }

    private void T_Search_TextChanged_2(object sender, TextChangedEventArgs e)
    {

    }
}
