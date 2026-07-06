using System.Globalization;
using System.Net.Http.Headers;
using Newtonsoft.Json;

namespace MyPosAccurate2026.Produk;

public partial class DetailScan : ContentPage
{
    private string _itemNo;

    public DetailScan(string itemNo)
    {
        InitializeComponent();
        _itemNo = itemNo;

        ProductImage.Source = BuildImageSource(_itemNo);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadDetailData();
    }

    private async Task LoadDetailData()
    {
        try
        {
            string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
            string apiUrl = $"{App.API_HOST}item/detail_byNo.php?no={Uri.EscapeDataString(_itemNo)}";

            using (var client = new HttpClient())
            {
                if (!string.IsNullOrEmpty(cleanToken))
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);

                client.DefaultRequestHeaders.Add("User-Agent", "AccuratePOS-App/1.0");

                var response = await client.GetAsync(apiUrl);
                if (!response.IsSuccessStatusCode) return;

                string responseContent = await response.Content.ReadAsStringAsync();
                
                // Cegah parsing jika respon HTML/PHP error
                if (string.IsNullOrWhiteSpace(responseContent) || responseContent.TrimStart().StartsWith("<")) return;

                var apiResult = JsonConvert.DeserializeObject<DetailScanResponse>(responseContent);
                if (apiResult?.status == "success" && apiResult.data?.d != null)
                {
                    var d = apiResult.data.d;
                    var idID = new CultureInfo("id-ID");

                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        LblCategory.Text = d.itemCategory?.name ?? "-";
                        LblName.Text = d.name ?? "-";
                        LblSKU.Text = d.no ?? "-";
                        LblUnitPrice.Text = $"Rp {d.unitPrice.ToString("N0", idID)}";
                        
                        string unitName = d.unit1?.name ?? "Unit";
                        LblStock.Text = $"{d.balance.ToString("N0", idID)} {unitName}";

                        var gudang = d.detailWarehouseData?.FirstOrDefault();
                        LblWarehouseName.Text = gudang?.name ?? "-";
                        LblWarehousePIC.Text = string.IsNullOrWhiteSpace(gudang?.pic) ? "-" : $"PIC: {gudang.pic}";

                        LblWeight.Text = d.weight > 0 ? $"{d.weight} gram" : "-";
                        LblTax.Text = d.tax1?.taxInfo ?? "-";
                        LblExpired.Text = d.manageExpired ? "Dikelola" : "Tidak dikelola";

                        // Reset harga jual default ke strip jika data tidak ada
                        LblPriceUmum.Text = "-";
                        LblPriceShopee.Text = "-";
                        LblPriceMembership.Text = "-";
                        LblPriceFree.Text = "-";

                        // Update Harga Jual (Daftar)
                        if (d.detailSellingPrice != null)
                        {
                            foreach(var sp in d.detailSellingPrice)
                            {
                                string catName = sp.priceCategory?.name?.ToLower();
                                string formattedPrice = $"Rp {sp.price.ToString("N0", idID)}";

                                if (catName == "umum") LblPriceUmum.Text = formattedPrice;
                                else if (catName == "shopee") LblPriceShopee.Text = formattedPrice;
                                else if (catName == "membership") LblPriceMembership.Text = formattedPrice;
                                else if (catName == "free") LblPriceFree.Text = formattedPrice;
                            }
                        }
                    });
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error LoadDetailData: {ex.Message}");
        }
    }

    private ImageSource BuildImageSource(string itemNo)
    {
        if (string.IsNullOrWhiteSpace(itemNo)) return "nophotoproduct150.jpg";
        string baseHost = App.API_HOST?.Replace("api/", "");
        return $"{baseHost}images/{itemNo}.jpg";
    }

    // ===== Response DTO =====
    public class DetailScanResponse
    {
        public string status { get; set; }
        public string message { get; set; }
        public DetailScanData data { get; set; }
    }

    public class DetailScanData
    {
        public bool s { get; set; }
        public Detail d { get; set; }
    }

    public class Detail
    {
        public double balance { get; set; }
        public List<WarehouseData> detailWarehouseData { get; set; }
        public List<SellingPrice> detailSellingPrice { get; set; }
        public double unitPrice { get; set; }
        public Category itemCategory { get; set; }
        public Unit unit1 { get; set; }
        public double weight { get; set; }
        public string upcNo { get; set; }
        public string name { get; set; }
        public bool manageExpired { get; set; }
        public string no { get; set; }
        public Tax tax1 { get; set; }
    }

    public class WarehouseData
    {
        public string balanceUnit { get; set; }
        public string pic { get; set; }
        public string name { get; set; }
    }

    public class SellingPrice
    {
        public double price { get; set; }
        public Category priceCategory { get; set; }
        public string effectiveDate { get; set; }
    }

    public class Category
    {
        public string name { get; set; }
    }

    public class Unit
    {
        public string name { get; set; }
    }

    public class Tax
    {
        public string taxInfo { get; set; }
    }
}
