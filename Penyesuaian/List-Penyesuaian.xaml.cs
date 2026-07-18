using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.Net.Http.Headers;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Maui.Views;
using Microsoft.Maui.Controls.Shapes;

namespace MyPosAccurate2026.Penyesuaian;

public partial class List_Penyesuaian : ContentPage
{
    private ObservableCollection<AdjustmentItem> _adjustments = new ObservableCollection<AdjustmentItem>();
    private CancellationTokenSource _cts = new CancellationTokenSource();
    private bool _loaded = false;
    private string _startDate = "";
    private string _endDate = "";

    public List_Penyesuaian()
    {
        InitializeComponent();
        CV_Adjustments.ItemsSource = _adjustments;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_loaded)
        {
            _loaded = true;
            await LoadDataAsync("");
        }
    }

    private async Task LoadDataAsync(string search)
    {
        MainThread.BeginInvokeOnMainThread(() => OverlayLoading.IsVisible = true);
        var delayTask = Task.Delay(3000);
        try
        {
            _cts.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            string url = $"{App.API_HOST}item-adjustment/list.php?limit=100&page=1";
            if (!string.IsNullOrWhiteSpace(search))
            {
                url += $"&search={Uri.EscapeDataString(search)}";
            }
            if (!string.IsNullOrWhiteSpace(_startDate) && !string.IsNullOrWhiteSpace(_endDate))
            {
                url += $"&startDate={Uri.EscapeDataString(_startDate)}&endDate={Uri.EscapeDataString(_endDate)}";
            }

            string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
            if (string.IsNullOrEmpty(cleanToken))
            {
                await DisplayAlertAsync("Sesi Habis", "Anda harus login kembali.", "OK");
                return;
            }

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);
                var response = await client.GetAsync(url, token);
                var content = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    MainThread.BeginInvokeOnMainThread(async () => {
                        await DisplayAlertAsync("API Error", $"Status {response.StatusCode}\n{content}", "OK");
                    });
                    return;
                }

                if (content.StartsWith("<"))
                {
                    MainThread.BeginInvokeOnMainThread(async () => {
                        await DisplayAlertAsync("API Error", "Respons berupa HTML, bukan JSON.", "OK");
                    });
                    return;
                }

                try
                {
                    var result = JsonConvert.DeserializeObject<AdjustmentResponse>(content);
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        _adjustments.Clear();
                        if (result?.data != null)
                        {
                            foreach (var item in result.data)
                            {
                                _adjustments.Add(item);
                            }
                        }
                    });
                }
                catch (Exception ex)
                {
                    MainThread.BeginInvokeOnMainThread(async () => {
                        await DisplayAlertAsync("JSON Error", ex.Message, "OK");
                    });
                }
            }
        }
        catch (TaskCanceledException) { }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
        finally
        {
            await delayTask;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                OverlayLoading.IsVisible = false;
                RV_Adjustments.IsRefreshing = false;
            });
        }
    }

    private async void OnRefreshing(object sender, EventArgs e)
    {
        await LoadDataAsync(T_Search.Text ?? "");
    }

    private void T_Search_SearchButtonPressed(object sender, EventArgs e)
    {
        _ = LoadDataAsync(T_Search.Text ?? "");
    }

    private void TapAdd_Tapped(object sender, TappedEventArgs e)
    {
        FormFilterDate.IsVisible = !FormFilterDate.IsVisible;
    }

    private async void BtnApplyFilter_Tapped(object sender, TappedEventArgs e)
    {
        _startDate = string.Format("{0:dd/MM/yyyy}", DP_StartDate.Date);
        _endDate = string.Format("{0:dd/MM/yyyy}", DP_EndDate.Date);
        FormFilterDate.IsVisible = false;
        await LoadDataAsync(T_Search.Text ?? "");
    }
    private async void OnItemTapped(object sender, TappedEventArgs e)
    {
        if (sender is Border border && border.BindingContext is AdjustmentItem item)
        {
            string action = await DisplayActionSheet("Pilih Aksi", "Batal", null, "Edit", "Export PDF");
            if (action == "Export PDF")
            {
                await ExportPdfAsync(item.id);
            }
        }
    }

    private async Task ExportPdfAsync(int id)
    {
        OverlayLoading.IsVisible = true;
        try
        {
            string apiUrl = $"{App.API_HOST}item-adjustment/detail.php?id={id}";
            using var client = new System.Net.Http.HttpClient();
            string cleanToken = Microsoft.Maui.Storage.Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
            if (!string.IsNullOrEmpty(cleanToken))
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", cleanToken);

            var response = await client.GetAsync(apiUrl);
            var responseContent = await response.Content.ReadAsStringAsync();
            var detailResponse = Newtonsoft.Json.JsonConvert.DeserializeObject<AdjustmentDetailResponse>(responseContent);
            if (detailResponse != null && detailResponse.data != null)
            {
#if ANDROID
                byte[] pdfBytes = await Task.Run(() => BuildReportPdfAndroid(detailResponse.data));
                string fileName = $"Penyesuaian_{detailResponse.data.number?.Replace("/", "_")}_{DateTime.Now:yyyyMMddHHmmss}.pdf";
                
                var context = Android.App.Application.Context;
                var values = new Android.Content.ContentValues();
                values.Put(Android.Provider.MediaStore.IMediaColumns.DisplayName, fileName);
                values.Put(Android.Provider.MediaStore.IMediaColumns.MimeType, "application/pdf");
                values.Put(Android.Provider.MediaStore.IMediaColumns.RelativePath, Android.OS.Environment.DirectoryDownloads);

                var uri = context.ContentResolver.Insert(Android.Provider.MediaStore.Downloads.ExternalContentUri, values);
                if (uri != null)
                {
                    using (var stream = context.ContentResolver.OpenOutputStream(uri))
                    {
                        stream.Write(pdfBytes, 0, pdfBytes.Length);
                    }
                    await DisplayAlert("Sukses", $"File PDF berhasil diunduh ke folder Download:\n{fileName}", "OK");
                }
                else
                {
                    await DisplayAlert("Gagal", "Tidak dapat membuat file di folder Download.", "OK");
                }
#else
                await DisplayAlert("Tidak Didukung", "Pembuatan PDF hanya didukung di Android.", "OK");
#endif
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", ex.Message, "OK");
        }
        finally
        {
            OverlayLoading.IsVisible = false;
        }
    }

#if ANDROID
    private byte[] BuildReportPdfAndroid(AdjustmentDetailData data)
    {
        const int pageW = 595;
        const int pageH = 842;
        const float margin = 40f;
        float contentTop = margin;
        float contentBottom = pageH - margin;

        var cBlack = Android.Graphics.Color.Argb(255, 26, 26, 26);
        var cValue = Android.Graphics.Color.Argb(255, 51, 51, 51);
        var cGrey = Android.Graphics.Color.Argb(255, 119, 119, 119);
        var cBand = Android.Graphics.Color.Argb(255, 232, 232, 232);
        var cLine = Android.Graphics.Color.Argb(255, 51, 51, 51);
        var cSoft = Android.Graphics.Color.Argb(255, 204, 204, 204);

        var pCompany = new Android.Graphics.Paint { Color = cBlack, TextSize = 18, FakeBoldText = true, AntiAlias = true };
        var pTitle = new Android.Graphics.Paint { Color = cBlack, TextSize = 15, FakeBoldText = true, AntiAlias = true };
        var pPeriode = new Android.Graphics.Paint { Color = cGrey, TextSize = 11, AntiAlias = true, TextSkewX = -0.25f };
        var pBody = new Android.Graphics.Paint { Color = cValue, TextSize = 11, AntiAlias = true };
        var pBodyB = new Android.Graphics.Paint { Color = cBlack, TextSize = 11, FakeBoldText = true, AntiAlias = true };
        var pGroup = new Android.Graphics.Paint { Color = cBlack, TextSize = 13, FakeBoldText = true, AntiAlias = true };
        var pFootDate = new Android.Graphics.Paint { Color = cGrey, TextSize = 9, AntiAlias = true };
        var pBg = new Android.Graphics.Paint { AntiAlias = true };
        var pLinePaint = new Android.Graphics.Paint { Color = cLine, StrokeWidth = 1f, AntiAlias = true };
        var pSoftLine = new Android.Graphics.Paint { Color = cSoft, StrokeWidth = 1f, AntiAlias = true };

        float xLeft = margin;
        float xRightTotal = pageW - margin;

        float TextH(Android.Graphics.Paint p) => p.Descent() - p.Ascent();
        float Baseline(float top, Android.Graphics.Paint p) => top - p.Ascent();

        var blocks = new System.Collections.Generic.List<(float h, Action<Android.Graphics.Canvas, float> draw)>();

        void AddCenter(string text, Android.Graphics.Paint p, float gap)
        {
            if (string.IsNullOrEmpty(text)) return;
            float h = TextH(p) + gap;
            blocks.Add((h, (canvas, top) =>
            {
                float w = p.MeasureText(text);
                canvas.DrawText(text, (pageW - w) / 2f, Baseline(top, p), p);
            }));
        }
        void AddRow2(string a, string val, Android.Graphics.Paint pa, Android.Graphics.Paint pv, float gap, float paddingLeft = 0)
        {
            float h = TextH(pa) + gap;
            blocks.Add((h, (canvas, top) =>
            {
                float bl = Baseline(top, pa);
                canvas.DrawText(a ?? "", xLeft + paddingLeft, bl, pa);
                if (val != null) canvas.DrawText(val, xRightTotal - pv.MeasureText(val), bl, pv);
            }));
        }
        void AddSeparator(float gapAbove, float gapBelow, bool soft = false)
        {
            float h = gapAbove + gapBelow + 1;
            blocks.Add((h, (canvas, top) =>
            {
                float ly = top + gapAbove;
                canvas.DrawLine(xLeft, ly, xRightTotal, ly, soft ? pSoftLine : pLinePaint);
            }));
        }
        void AddSpacer(float height) => blocks.Add((height, (canvas, top) => { }));

        AddCenter("BUKTI PENYESUAIAN BARANG", pCompany, 8);
        AddCenter($"No: {data.number}", pTitle, 4);
        AddCenter($"Tanggal: {data.transDateView}", pPeriode, 12);
        AddSeparator(2, 10);

        AddRow2("Akun Penyesuaian:", $"{data.adjustmentAccount?.no} - {data.adjustmentAccount?.name}", pBody, pBody, 4);
        AddRow2("Keterangan:", data.description, pBody, pBody, 8);
        AddSeparator(2, 10, soft: true);

        // Header Table
        blocks.Add((TextH(pBodyB) + 8, (canvas, top) =>
        {
            float bl = Baseline(top, pBodyB);
            canvas.DrawText("Nama Barang", xLeft, bl, pBodyB);
            canvas.DrawText("Tipe", xLeft + 200, bl, pBodyB);
            canvas.DrawText("Nilai", xRightTotal - pBodyB.MeasureText("Nilai"), bl, pBodyB);
        }));
        AddSeparator(2, 6, soft: true);

        if (data.detailItem != null)
        {
            foreach (var item in data.detailItem)
            {
                blocks.Add((TextH(pBody) + 6, (canvas, top) =>
                {
                    float bl = Baseline(top, pBody);
                    string name = item.detailName;
                    if (name.Length > 30) name = name.Substring(0, 27) + "...";
                    canvas.DrawText(name, xLeft, bl, pBody);
                    canvas.DrawText(item.itemAdjustmentTypeName ?? "", xLeft + 200, bl, pBody);
                    
                    string amt = $"Rp {item.unitCost:N0}";
                    canvas.DrawText(amt, xRightTotal - pBody.MeasureText(amt), bl, pBody);
                }));
            }
        }
        AddSpacer(8);
        AddSeparator(2, 10);
        
        string totalAmtStr = $"Rp {data.totalAmount:N0}";
        AddRow2("Total Penyesuaian:", totalAmtStr, pGroup, pGroup, 10);

        AddSpacer(20);
        AddCenter($"Dicetak pada: {DateTime.Now.ToString("dd MMM yyyy HH:mm:ss", new System.Globalization.CultureInfo("id-ID"))}", pFootDate, 0);

        using var document = new Android.Graphics.Pdf.PdfDocument();
        int pageNum = 1;
        var page = document.StartPage(new Android.Graphics.Pdf.PdfDocument.PageInfo.Builder(pageW, pageH, pageNum).Create());
        var canvasPage = page.Canvas;
        canvasPage.DrawColor(Android.Graphics.Color.White);
        float y = contentTop;

        foreach (var (h, draw) in blocks)
        {
            if (y + h > contentBottom)
            {
                document.FinishPage(page);
                pageNum++;
                page = document.StartPage(new Android.Graphics.Pdf.PdfDocument.PageInfo.Builder(pageW, pageH, pageNum).Create());
                canvasPage = page.Canvas;
                canvasPage.DrawColor(Android.Graphics.Color.White);
                y = contentTop;
            }
            draw(canvasPage, y);
            y += h;
        }

        document.FinishPage(page);
        using var ms = new System.IO.MemoryStream();
        document.WriteTo(ms);
        document.Close();
        return ms.ToArray();
    }
#endif
}

public class AdjustmentDetailResponse
{
    public AdjustmentDetailData data { get; set; }
}

public class AdjustmentDetailData
{
    public string number { get; set; }
    public string transDateView { get; set; }
    public double totalAmount { get; set; }
    public System.Collections.Generic.List<AdjustmentDetailItem> detailItem { get; set; }
    public AdjustmentDetailAccount adjustmentAccount { get; set; }
    public string description { get; set; }
}

public class AdjustmentDetailItem
{
    public ItemUnit itemUnit { get; set; }
    public string detailNotes { get; set; }
    public ItemWarehouse warehouse { get; set; }
    public string detailName { get; set; }
    public string itemAdjustmentTypeName { get; set; }
    public double unitCost { get; set; }
}

public class ItemUnit
{
    public string name { get; set; }
}

public class ItemWarehouse
{
    public string name { get; set; }
}

public class AdjustmentDetailAccount
{
    public string no { get; set; }
    public string name { get; set; }
}

public class AdjustmentResponse
{
    public string status { get; set; }
    public string message { get; set; }
    public System.Collections.Generic.List<AdjustmentItem> data { get; set; }
}

public class AdjustmentItem
{
    public int id { get; set; }
    public string number { get; set; }
    public string transDate { get; set; }
    public string transDateView { get; set; }
    public string description { get; set; }
    public double totalAmount { get; set; }
}