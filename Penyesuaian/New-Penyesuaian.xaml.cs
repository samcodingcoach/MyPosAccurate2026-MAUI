using System.Collections.Generic;
using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Maui.Views;
using Microsoft.Maui.Controls.Shapes;


namespace MyPosAccurate2026.Penyesuaian;

public partial class New_Penyesuaian : ContentPage
{
    string AdjustmentAccountNo;

    public ObservableCollection<ItemModel> AutoCompleteResults { get; set; } = new ObservableCollection<ItemModel>();

    public New_Penyesuaian()
	{
		InitializeComponent();
        
        PickerAdjustmentAccountNo.ItemsSource = new List<AdjustmentAccount>
        {
            new AdjustmentAccount { DisplayName = "STOCK_IN", Value = "ADJUSTMENT_IN" },
            new AdjustmentAccount { DisplayName = "STOCK_OUT", Value = "ADJUSTMENT_OUT" }
        };
        PickerAdjustmentAccountNo.ItemDisplayBinding = new Binding("DisplayName");
        
        List_AutoComplete.ItemsSource = AutoCompleteResults;
	}

    private void bOpsi_Clicked(object sender, EventArgs e)
    {
		if(formCOAket.IsVisible == false)
		{
			formCOAket.IsVisible = true;
		}
		else
		{
            formCOAket.IsVisible = false;
        }
    }
    
    private async void SearchBar_Item_TextChanged(object sender, TextChangedEventArgs e)
    {
        string keyword = e.NewTextValue;

        if (string.IsNullOrWhiteSpace(keyword) || keyword.Length < 2)
        {
            Border_AutoComplete.IsVisible = false;
            AutoCompleteResults.Clear();
            return;
        }

        await Task.Delay(400);

        if (keyword != SearchBar_Item.Text) return;

        await FetchItemsFromApi(keyword);
    }

    private async Task FetchItemsFromApi(string keyword)
    {
        try
        {
            string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
            string apiUrl = $"{App.API_HOST}item/list-lokal.php?search={Uri.EscapeDataString(keyword)}&limit=10";

            using (var client = new HttpClient())
            {
                if (!string.IsNullOrEmpty(cleanToken))
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);

                client.DefaultRequestHeaders.Add("User-Agent", "AccuratePOS-App/1.0");

                var response = await client.GetAsync(apiUrl);
                string responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    MainThread.BeginInvokeOnMainThread(() => Border_AutoComplete.IsVisible = false);
                    return;
                }

                var apiResult = JsonConvert.DeserializeObject<ApiResponse>(responseContent);

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    AutoCompleteResults.Clear();

                    if (apiResult != null && apiResult.status == "success")
                    {
                        if (apiResult.data != null && apiResult.data.Count > 0)
                        {
                            foreach (var item in apiResult.data)
                            {
                                AutoCompleteResults.Add(item);
                            }
                        }
                        Border_AutoComplete.IsVisible = true;
                    }
                });
            }
        }
        catch (Exception ex)
        {
            MainThread.BeginInvokeOnMainThread(() => Border_AutoComplete.IsVisible = false);
            System.Diagnostics.Debug.WriteLine("Fetch API Error: " + ex.Message);
        }
    }

    private void List_AutoComplete_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is ItemModel selectedItem)
        {
            List_AutoComplete.SelectedItem = null;
            SearchBar_Item.Text = selectedItem.item_no;
            Border_AutoComplete.IsVisible = false;
        }
    }

    private async void bSimpan_Clicked(object sender, EventArgs e)
    {
        string itemNo = "100003";
        await this.ShowPopupAsync(new PopUpBarangSelected(itemNo), new PopupOptions
        {
            Shape = new RoundRectangle
            {
                CornerRadius = new CornerRadius(20),
                Stroke = Colors.Transparent,
                StrokeThickness = 0
            }
        });
    }
}

public class ApiResponse
{
    public string status { get; set; }
    public string message { get; set; }
    public List<ItemModel> data { get; set; }
}

public class ItemModel
{
    public string item_no { get; set; }
    public string name { get; set; }
    public double balance { get; set; }
    public double price { get; set; }
    public string image { get; set; }
    public Color StockColor => balance > 0 ? Color.FromArgb("#006400") : Color.FromArgb("#FF0000");
}

public class AdjustmentAccount
{
    public string DisplayName { get; set; }
    public string Value { get; set; }
}