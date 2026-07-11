using CommunityToolkit.Maui.Views;
using Newtonsoft.Json;
using System.Net.Http.Headers;

namespace MyPosAccurate2026.Stok;

public partial class PopSelesai_SO : Popup
{
    private string _orderNumber;

    public PopSelesai_SO(string orderNumber)
    {
        InitializeComponent();
        _orderNumber = orderNumber;
        LabelOrderNumber.Text = _orderNumber;
        _ = LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        try
        {
            string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);

            // 1. Load Detail
            string detailUrl = $"{App.API_HOST}stokopname-order/detail.php?number={Uri.EscapeDataString(_orderNumber)}";
            var detailResp = await client.GetAsync(detailUrl);
            if (detailResp.IsSuccessStatusCode)
            {
                var content = await detailResp.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<SODetailResponse>(content);
                if (result?.status == "success" && result.data != null)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        LabelStatus.Text = result.data.statusName ?? "-";
                        int itemsCount = result.data.detailItem?.Count ?? 0;
                        LabelTotalItems.Text = $"{itemsCount} Items";
                    });
                }
            }

            // 2. Load OPR Results
            string resultUrl = $"{App.API_HOST}stokopname-result/list.php?search={Uri.EscapeDataString(_orderNumber)}";
            var oprResp = await client.GetAsync(resultUrl);
            if (oprResp.IsSuccessStatusCode)
            {
                var content = await oprResp.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<SOResultSearchResponse>(content);
                
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    OprListContainer.Children.Clear();
                    
                    if (result?.data != null && result.data.Count > 0)
                    {
                        foreach (var opr in result.data)
                        {
                            var border = new Border
                            {
                                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
                                Stroke = Color.FromArgb("#e1e3e3"),
                                StrokeThickness = 1,
                                BackgroundColor = Color.FromArgb("#f2f4f4"),
                                Padding = 15,
                                Margin = new Thickness(0,0,0,10)
                            };

                            var grid = new Grid
                            {
                                RowDefinitions =
                                {
                                    new RowDefinition { Height = GridLength.Auto },
                                    new RowDefinition { Height = GridLength.Auto },
                                    new RowDefinition { Height = GridLength.Auto }
                                },
                                ColumnDefinitions =
                                {
                                    new ColumnDefinition { Width = GridLength.Star },
                                    new ColumnDefinition { Width = GridLength.Auto }
                                },
                                RowSpacing = 5
                            };

                            // Row 0
                            grid.Add(new Label { Text = opr.number, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#191c1d"), FontSize = 16 }, 0, 0);
                            grid.Add(new Label { Text = "Selesai", TextColor = Color.FromArgb("#616363"), FontSize = 12, HorizontalOptions = LayoutOptions.End }, 1, 0); // Mock date or status

                            // Row 1
                            grid.Add(new Label { Text = "Stocker", TextColor = Color.FromArgb("#616363"), FontSize = 14 }, 0, 1);
                            grid.Add(new Label { Text = "System", TextColor = Color.FromArgb("#191c1d"), FontSize = 14 }, 1, 1); // Mock worker name

                            border.Content = grid;
                            OprListContainer.Children.Add(border);
                        }
                    }
                    else
                    {
                        OprListContainer.Children.Add(new Label { Text = "Belum ada transaksi hasil.", TextColor = Colors.Gray, FontAttributes = FontAttributes.Italic });
                    }
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error LoadDataAsync: {ex.Message}");
        }
        finally
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                LoadingIndicator.IsVisible = false;
                ContentArea.IsVisible = true;
            });
        }
    }

    private async void BtnClose_Tapped(object sender, TappedEventArgs e)
    {
        await CloseAsync();
    }

    private async void BtnExport_Clicked(object sender, EventArgs e)
    {
        // Export logic
        await CloseAsync();
    }

    private async void BtnSelesai_Clicked(object sender, EventArgs e)
    {
        // Finish logic
        await CloseAsync();
    }
}