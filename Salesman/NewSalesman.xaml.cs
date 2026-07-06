using System.Net.Http.Headers;
using Newtonsoft.Json.Linq;
using The49.Maui.BottomSheet;
namespace MyPosAccurate2026.Salesman;

public partial class NewSalesman : BottomSheet
{
    public event EventHandler OnSalesmanSaved;
    private int? _editId = null;

    public async Task SetEditMode(SalesmanItem item)
    {
        _editId = item.id;
        EntryNumber.Text = item.number;
        EntryNumber.IsEnabled = false;

        LoadingFetch.IsRunning = true;
        LoadingFetch.IsVisible = true;

        try
        {
            string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
            if (!string.IsNullOrEmpty(cleanToken))
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);
                    string apiUrl = $"{App.API_HOST}karyawan/detail.php?id={item.id}";
                    var response = await client.GetAsync(apiUrl);

                    if (response.IsSuccessStatusCode)
                    {
                        var responseContent = await response.Content.ReadAsStringAsync();
                        var parsed = JObject.Parse(responseContent);
                        var data = parsed["data"];
                        if (data != null)
                        {
                            MainThread.BeginInvokeOnMainThread(() =>
                            {
                                EntryNameSales.Text = data["name"]?.ToString();
                                EntryEmail.Text = data["email"]?.ToString();
                                EntryHP.Text = data["mobilePhone"]?.ToString();
                                EntryBankAccount.Text = data["bankAccount"]?.ToString();
                                EntryBankAccountName.Text = data["bankAccountName"]?.ToString();

                                string salutation = data["salutation"]?.ToString();
                                if (!string.IsNullOrEmpty(salutation))
                                {
                                    var selTitle = (PickerPanggilan.ItemsSource as List<TitleOption>)?.FirstOrDefault(x => x.Value == salutation);
                                    if (selTitle != null) PickerPanggilan.SelectedItem = selTitle;
                                }

                                string bankCode = data["bankCode"]?.ToString();
                                if (!string.IsNullOrEmpty(bankCode))
                                {
                                    var selBank = (PickerBank.ItemsSource as List<BankOption>)?.FirstOrDefault(x => x.Code == bankCode);
                                    if (selBank != null) PickerBank.SelectedItem = selBank;
                                }
                            });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Gagal load detail karyawan: {ex.Message}");
            MainThread.BeginInvokeOnMainThread(() =>
            {
                EntryNameSales.Text = item.name;
            });
        }
        finally
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                LoadingFetch.IsRunning = false;
                LoadingFetch.IsVisible = false;
            });
        }
    }

	public NewSalesman()
	{
		InitializeComponent();
        
        var titles = new List<TitleOption>
        {
            new TitleOption { Display = "Bapak", Value = "MR" },
            new TitleOption { Display = "Ibu", Value = "MRS" }
        };

        PickerPanggilan.ItemsSource = titles;
        PickerPanggilan.ItemDisplayBinding = new Binding("Display");
        PickerPanggilan.SelectedIndex = 0;

        var banks = new List<BankOption>
        {
            new BankOption { Name = "BANK CENTRAL ASIA", Code = "014" },
            new BankOption { Name = "BANK MANDIRI", Code = "008" },
            new BankOption { Name = "BANK NEGARA INDONESIA", Code = "009" },
            new BankOption { Name = "BANK RAKYAT INDONESIA", Code = "002" },
            new BankOption { Name = "BANK CIMB NIAGA", Code = "022" },
            new BankOption { Name = "BANK SYARIAH INDONESIA", Code = "451" },
            new BankOption { Name = "BANK PERMATA", Code = "013" },
            new BankOption { Name = "BANK DANAMON INDONESIA", Code = "011" },
            new BankOption { Name = "LAINNYA", Code = "000" }
        };

        PickerBank.ItemsSource = banks;
        PickerBank.ItemDisplayBinding = new Binding("Name");
	}

    private void BBatal_Clicked(object sender, EventArgs e)
    {
        this.DismissAsync();
    }

    private async void BSimpan_Clicked(object sender, EventArgs e)
    {
        string name = EntryNameSales.Text?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            await Application.Current.MainPage.DisplayAlert("Peringatan", "Nama karyawan harus diisi.", "OK");
            return;
        }

        string salutation = (PickerPanggilan.SelectedItem as TitleOption)?.Value ?? "MR";
        
        BSimpan.IsEnabled = false;

        try
        {
            string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
            if (string.IsNullOrEmpty(cleanToken))
            {
                await Application.Current.MainPage.DisplayAlert("Sesi Habis", "Anda harus login kembali.", "OK");
                return;
            }

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);

                string apiUrl = $"{App.API_HOST}karyawan/save.php";

                var selectedBank = PickerBank.SelectedItem as BankOption;
                string bankName = selectedBank?.Name ?? "";
                string bankCode = selectedBank?.Code ?? "";

                var payload = new System.Collections.Generic.Dictionary<string, object>
                {
                    { "name", name },
                    { "salutation", salutation },
                    { "transDate", DateTime.Now.ToString("dd/MM/yyyy") },
                    { "bankAccount", EntryBankAccount.Text?.Trim() ?? "" },
                    { "bankAccountName", EntryBankAccountName.Text?.Trim() ?? "" },
                    { "bankName", bankName },
                    { "bankCode", bankCode },
                    { "domisiliType", "INA" },
                    { "email", EntryEmail.Text?.Trim() ?? "" },
                    { "mobilePhone", EntryHP.Text?.Trim() ?? "" },
                    { "salesman", true }
                };

                if (_editId.HasValue)
                {
                    payload.Add("id", _editId.Value);
                }
                else
                {
                    payload.Add("number", EntryNumber.Text?.Trim() ?? "");
                }

                string jsonPayload = Newtonsoft.Json.JsonConvert.SerializeObject(payload);
                var content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");

                var response = await client.PostAsync(apiUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    await Application.Current.MainPage.DisplayAlert("Sukses", "Data karyawan berhasil disimpan.", "OK");
                    OnSalesmanSaved?.Invoke(this, EventArgs.Empty);
                    await this.DismissAsync();
                }
                else
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    await Application.Current.MainPage.DisplayAlert("Gagal", $"Gagal menyimpan data. Status: {response.StatusCode}\n{responseContent}", "OK");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Gagal menyimpan karyawan: {ex.Message}");
            await Application.Current.MainPage.DisplayAlert("Error", "Terjadi kesalahan saat menyimpan data.", "OK");
        }
        finally
        {
            BSimpan.IsEnabled = true;
        }
    }
}

public class TitleOption
{
    public string Display { get; set; }
    public string Value { get; set; }
}

public class BankOption
{
    public string Name { get; set; }
    public string Code { get; set; }
}